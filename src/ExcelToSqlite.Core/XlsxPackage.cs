using System.Globalization;
using System.IO.Compression;
using System.Xml;

namespace ExcelToSqlite.Core;

/// <summary>워크시트 안의 사각 영역. 1-based, 끝 포함.</summary>
public readonly record struct CellArea(int FirstRow, int LastRow, int FirstCol, int LastCol)
{
    public bool IsSingleCell => FirstRow == LastRow && FirstCol == LastCol;
    public int RowCount => LastRow - FirstRow + 1;
    public int ColCount => LastCol - FirstCol + 1;
}

/// <summary>엑셀에 정의된 이름 하나.</summary>
public sealed record DefinedNameEntry(string Name, string? SheetName, CellArea? Area, bool IsBroken)
{
    /// <summary>UI/CLI에 보여줄 이름. 시트 범위 이름은 "Sheet!Name".</summary>
    public string DisplayName => Scope is null ? Name : $"{Scope}!{Name}";

    /// <summary>시트 범위(local scope) 이름이면 그 시트명. 통합문서 범위면 null.</summary>
    public string? Scope { get; init; }
}

/// <summary>
/// .xlsx/.xlsm 을 <b>스트리밍</b>으로 읽기 위한 패키지 핸들.
/// ClosedXML 처럼 통합문서 전체를 객체 모델로 올리지 않고, zip 안에서 필요한 파트만 그때그때 읽는다.
/// 35MB(압축 해제 340MB, 시트 53개) 파일에서 이름 목록을 얻는 데 10초 넘게 걸리던 것이 이 방식에서는 즉시다.
/// </summary>
public sealed class XlsxPackage : IDisposable
{
    private const string Ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    private readonly ZipArchive _zip;
    private readonly Dictionary<string, string> _sheetPart;   // 시트명 → zip 엔트리 경로
    private readonly bool _date1904;

    private string[]? _sharedStrings;   // 처음 쓸 때만 로드 (13MB인 파일도 있다)
    private bool[]? _styleIsDate;

    public string FilePath { get; }
    public List<string> SheetNames { get; }
    public List<DefinedNameEntry> DefinedNames { get; }

    private XlsxPackage(string filePath, ZipArchive zip)
    {
        FilePath = filePath;
        _zip = zip;

        var (sheets, names, date1904) = ReadWorkbookPart();
        _date1904 = date1904;
        _sheetPart = sheets;
        SheetNames = sheets.Keys.ToList();
        DefinedNames = names;
    }

    public static XlsxPackage Open(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"엑셀 파일을 찾을 수 없습니다: {filePath}", filePath);

        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        if (ext == ".xls")
            throw new NotSupportedException("구형 .xls는 지원하지 않습니다. .xlsx / .xlsm 으로 저장하세요.");

        // Excel에서 열려 있는 파일도 읽히도록 FileShare.ReadWrite.
        var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        try
        {
            return new XlsxPackage(filePath, new ZipArchive(fs, ZipArchiveMode.Read));
        }
        catch
        {
            fs.Dispose();
            throw;
        }
    }

    public void Dispose() => _zip.Dispose();

    // ── 워크북 파트 ────────────────────────────────────────────────────────────

    private (Dictionary<string, string> Sheets, List<DefinedNameEntry> Names, bool Date1904) ReadWorkbookPart()
    {
        var rels = ReadRelationships();
        var sheets = new Dictionary<string, string>(StringComparer.Ordinal);
        var names = new List<DefinedNameEntry>();
        var sheetOrder = new List<string>();
        var date1904 = false;

        var entry = _zip.GetEntry("xl/workbook.xml")
                    ?? throw new InvalidDataException("xl/workbook.xml 이 없습니다. 손상된 엑셀 파일입니다.");

        // definedName 의 localSheetId 는 시트 순서 인덱스라 시트를 먼저 다 읽어야 한다.
        var pendingNames = new List<(string Name, int? LocalSheetId, string Ref)>();

        using (var xr = XmlReader.Create(entry.Open(), XmlSettings()))
        {
            // ReadElementContentAsString 은 리더를 이미 다음 노드로 옮기므로,
            // 그 경우에는 루프에서 Read()를 또 부르면 안 된다(이름이 하나 걸러 누락된다).
            while (!xr.EOF)
            {
                if (xr.NodeType != XmlNodeType.Element)
                {
                    xr.Read();
                    continue;
                }

                if (xr.LocalName == "definedName" && xr.NamespaceURI == Ns)
                {
                    var name = xr.GetAttribute("name");
                    var local = xr.GetAttribute("localSheetId");
                    // ReadElementContentAsString 이 </definedName> 다음 노드까지 옮겨 준다.
                    // 여기서 Read()를 더 부르면 바로 뒤에 붙어 있는 다음 이름을 건너뛴다.
                    var refText = xr.ReadElementContentAsString();

                    if (name is not null)
                        pendingNames.Add((name,
                                          local is not null && int.TryParse(local, out var li) ? li : null,
                                          refText));
                    continue;
                }

                if (xr.LocalName == "workbookPr")
                {
                    var d1904 = xr.GetAttribute("date1904");
                    date1904 = d1904 is "1" or "true";
                }
                else if (xr.LocalName == "sheet" && xr.NamespaceURI == Ns)
                {
                    var name = xr.GetAttribute("name");
                    var rid = xr.GetAttribute("id", RelNs);
                    if (name is not null && rid is not null && rels.TryGetValue(rid, out var target))
                    {
                        sheets[name] = target;
                        sheetOrder.Add(name);
                    }
                }

                xr.Read();
            }
        }

        foreach (var (name, localId, refText) in pendingNames)
        {
            if (name.StartsWith("_xlnm", StringComparison.OrdinalIgnoreCase)) continue;   // 인쇄영역 등 내장 이름

            var scope = localId is int idx && idx >= 0 && idx < sheetOrder.Count ? sheetOrder[idx] : null;
            var parsed = RefParser.TryParseFirstArea(refText, out var sheet, out var area);

            names.Add(new DefinedNameEntry(name, sheet, parsed ? area : null, !parsed) { Scope = scope });
        }

        names.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.DisplayName, b.DisplayName));
        return (sheets, names, date1904);
    }

    private Dictionary<string, string> ReadRelationships()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var entry = _zip.GetEntry("xl/_rels/workbook.xml.rels");
        if (entry is null) return map;

        using var xr = XmlReader.Create(entry.Open(), XmlSettings());
        while (xr.Read())
        {
            if (xr.NodeType != XmlNodeType.Element || xr.LocalName != "Relationship") continue;

            var id = xr.GetAttribute("Id");
            var target = xr.GetAttribute("Target");
            if (id is null || target is null) continue;

            // "worksheets/sheet1.xml" / "/xl/worksheets/sheet1.xml" 둘 다 온다.
            target = target.TrimStart('/');
            map[id] = target.StartsWith("xl/", StringComparison.Ordinal) ? target : "xl/" + target;
        }

        return map;
    }

    // ── 공유 문자열 · 서식 (지연 로드) ──────────────────────────────────────────

    private string[] SharedStrings => _sharedStrings ??= LoadSharedStrings();

    private string[] LoadSharedStrings()
    {
        var entry = _zip.GetEntry("xl/sharedStrings.xml");
        if (entry is null) return Array.Empty<string>();

        var list = new List<string>();
        using var xr = XmlReader.Create(entry.Open(), XmlSettings());

        while (xr.Read())
        {
            if (xr.NodeType != XmlNodeType.Element || xr.LocalName != "si") continue;

            // <si> 안에 <t> 가 하나이거나, 서식이 섞이면 <r><t> 조각들로 나뉜다. 전부 이어붙인다.
            var sb = new System.Text.StringBuilder();
            using var si = xr.ReadSubtree();
            si.Read();
            while (si.Read())
                if (si.NodeType == XmlNodeType.Element && si.LocalName == "t")
                    sb.Append(si.ReadElementContentAsString());

            list.Add(sb.ToString());
        }

        return list.ToArray();
    }

    /// <summary>셀 스타일 인덱스 → 날짜 서식인지. 숫자 셀을 날짜로 해석할지 판단하는 데 쓴다.</summary>
    private bool[] StyleIsDate => _styleIsDate ??= LoadStyleDateFlags();

    private bool[] LoadStyleDateFlags()
    {
        var entry = _zip.GetEntry("xl/styles.xml");
        if (entry is null) return Array.Empty<bool>();

        var customIsDate = new Dictionary<int, bool>();
        var flags = new List<bool>();
        var inCellXfs = false;

        using var xr = XmlReader.Create(entry.Open(), XmlSettings());
        while (xr.Read())
        {
            if (xr.NodeType == XmlNodeType.Element && xr.LocalName == "numFmt")
            {
                if (int.TryParse(xr.GetAttribute("numFmtId"), out var id))
                    customIsDate[id] = NumberFormat.IsDateFormatCode(xr.GetAttribute("formatCode"));
            }
            else if (xr.NodeType == XmlNodeType.Element && xr.LocalName == "cellXfs")
            {
                inCellXfs = true;
            }
            else if (xr.NodeType == XmlNodeType.EndElement && xr.LocalName == "cellXfs")
            {
                inCellXfs = false;
            }
            else if (inCellXfs && xr.NodeType == XmlNodeType.Element && xr.LocalName == "xf")
            {
                var id = int.TryParse(xr.GetAttribute("numFmtId"), out var n) ? n : 0;
                flags.Add(customIsDate.TryGetValue(id, out var custom)
                    ? custom
                    : NumberFormat.IsBuiltinDateId(id));
            }
        }

        return flags.ToArray();
    }

    // ── 시트 읽기 ──────────────────────────────────────────────────────────────

    public bool TryGetSheetPart(string sheetName, out string part) => _sheetPart.TryGetValue(sheetName, out part!);

    /// <summary>
    /// 시트의 지정 영역을 행 단위로 흘려보낸다. 영역 밖의 행은 XML 서브트리째 건너뛰고,
    /// 마지막 행을 지나면 즉시 멈춘다 — 113MB짜리 시트에서도 필요한 부분만 읽는다.
    /// </summary>
    /// <param name="area">null이면 시트 전체(사용 영역)를 읽는다.</param>
    public IEnumerable<(int RowIndex, string?[] Values)> ReadRows(string sheetName, CellArea? area)
    {
        if (!_sheetPart.TryGetValue(sheetName, out var part))
            throw new InvalidOperationException($"시트를 찾을 수 없습니다: {sheetName}");

        var entry = _zip.GetEntry(part)
                    ?? throw new InvalidDataException($"시트 파트가 없습니다: {part}");

        var firstRow = area?.FirstRow ?? 1;
        var lastRow = area?.LastRow ?? int.MaxValue;
        var firstCol = area?.FirstCol ?? 1;
        var lastCol = area?.LastCol ?? int.MaxValue;

        using var stream = entry.Open();
        using var xr = XmlReader.Create(stream, XmlSettings());

        while (!xr.EOF)
        {
            if (xr.NodeType != XmlNodeType.Element || xr.LocalName != "row")
            {
                xr.Read();
                continue;
            }

            var rowIndex = int.TryParse(xr.GetAttribute("r"), out var r) ? r : 0;

            if (rowIndex > lastRow) yield break;
            if (rowIndex < firstRow) { xr.Skip(); continue; }

            var values = ReadRowCells(xr, firstCol, lastCol);
            yield return (rowIndex, values);
        }
    }

    /// <summary>&lt;row&gt; 하나를 읽고 리더를 그 다음 노드로 옮긴다.</summary>
    private string?[] ReadRowCells(XmlReader xr, int firstCol, int lastCol)
    {
        var bounded = lastCol != int.MaxValue;
        var values = bounded ? new string?[lastCol - firstCol + 1] : Array.Empty<string?>();
        var loose = bounded ? null : new List<string?>();

        if (xr.IsEmptyElement)
        {
            xr.Read();
            return values;
        }

        xr.Read();   // <row> 안으로

        while (!(xr.NodeType == XmlNodeType.EndElement && xr.LocalName == "row"))
        {
            if (xr.EOF) break;

            if (xr.NodeType == XmlNodeType.Element && xr.LocalName == "c")
            {
                var (col, text) = ReadCell(xr);   // ReadCell 이 </c> 다음으로 옮겨 준다
                if (col <= 0) continue;

                if (bounded)
                {
                    if (col >= firstCol && col <= lastCol) values[col - firstCol] = text;
                }
                else
                {
                    while (loose!.Count < col) loose.Add(null);
                    loose[col - 1] = text;
                }
                continue;
            }

            xr.Read();
        }

        xr.Read();   // </row> 다음으로
        return bounded ? values : loose!.ToArray();
    }

    /// <summary>&lt;c&gt; 하나를 문자열로 읽고 리더를 그 다음 노드로 옮긴다.</summary>
    private (int Col, string? Text) ReadCell(XmlReader xr)
    {
        var reference = xr.GetAttribute("r");
        var type = xr.GetAttribute("t") ?? "n";
        var styleAttr = xr.GetAttribute("s");
        var col = reference is null ? -1 : RefParser.ColumnOf(reference);

        if (xr.IsEmptyElement)
        {
            xr.Read();
            return (col, null);
        }

        string? raw = null;
        var inlineText = (string?)null;

        xr.Read();
        while (!(xr.NodeType == XmlNodeType.EndElement && xr.LocalName == "c"))
        {
            if (xr.EOF) break;

            if (xr.NodeType == XmlNodeType.Element)
            {
                switch (xr.LocalName)
                {
                    case "v":
                        raw = xr.ReadElementContentAsString();
                        continue;

                    case "is":
                    {
                        var sb = new System.Text.StringBuilder();
                        using (var sub = xr.ReadSubtree())
                        {
                            sub.Read();
                            while (sub.Read())
                                if (sub.NodeType == XmlNodeType.Element && sub.LocalName == "t")
                                    sb.Append(sub.ReadElementContentAsString());
                        }
                        inlineText = sb.ToString();
                        xr.Read();
                        continue;
                    }

                    case "f":       // 수식 자체는 버리고 <v> 의 캐시값을 쓴다
                        xr.Skip();
                        continue;
                }
            }

            xr.Read();
        }

        xr.Read();   // </c> 다음으로

        var text = ResolveCellText(type, raw, inlineText, styleAttr);
        return (col, text);
    }

    private string? ResolveCellText(string type, string? raw, string? inlineText, string? styleAttr)
    {
        switch (type)
        {
            case "s":       // 공유 문자열 인덱스
                if (raw is null || !int.TryParse(raw, out var si)) return null;
                var pool = SharedStrings;
                return si >= 0 && si < pool.Length ? pool[si] : null;

            case "inlineStr":
                return inlineText;

            case "str":     // 수식 결과 문자열
                return raw;

            case "b":
                return raw == "1" ? "1" : "0";

            case "e":       // #REF! 등 오류값은 보이는 대로 남긴다
                return raw;

            case "d":       // ISO 8601 날짜(엄격 스키마)
                return raw;

            default:        // 숫자
                if (raw is null) return null;
                return IsDateStyle(styleAttr) ? SerialToDateText(raw) : NormalizeNumber(raw);
        }
    }

    /// <summary>
    /// 엑셀은 0.097 을 "9.7000000000000003E-2" 로 적어 두기도 한다. 값은 같지만 TEXT 컬럼에
    /// 그대로 들어가면 쓰기 어려우므로 최단 왕복 표기("R")로 정규화한다 — 정밀도 손실은 없다.
    /// 정수 리터럴은 손대지 않는다(15자리를 넘는 코드값이 지수 표기로 뭉개지는 것을 막는다).
    /// </summary>
    private static string NormalizeNumber(string raw)
    {
        if (raw.IndexOf('.') < 0 && raw.IndexOf('E') < 0 && raw.IndexOf('e') < 0) return raw;

        return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
            ? d.ToString("R", CultureInfo.InvariantCulture)
            : raw;
    }

    private bool IsDateStyle(string? styleAttr)
    {
        if (styleAttr is null || !int.TryParse(styleAttr, out var idx)) return false;
        var flags = StyleIsDate;
        return idx >= 0 && idx < flags.Length && flags[idx];
    }

    private string SerialToDateText(string raw)
    {
        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var serial))
            return raw;

        DateTime epoch;

        if (_date1904)
        {
            // 1904 체계: 시리얼 0 = 1904-01-01
            epoch = new DateTime(1904, 1, 1);
        }
        else
        {
            // 1900 체계에는 존재하지 않는 1900-02-29(시리얼 60)이 들어 있다.
            // 60 미만은 하루 앞선 기준일을, 61 이상은 1899-12-30 을 쓴다.
            if (serial >= 60 && serial < 61) return "1900-02-29";
            epoch = serial < 60 ? new DateTime(1899, 12, 31) : new DateTime(1899, 12, 30);
        }

        try
        {
            var dt = epoch.AddDays(serial);
            return dt.TimeOfDay == TimeSpan.Zero
                ? dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                : dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }
        catch (ArgumentOutOfRangeException)
        {
            return raw;   // 날짜 범위를 벗어난 값은 원문 유지
        }
    }

    private static XmlReaderSettings XmlSettings() => new()
    {
        IgnoreWhitespace = false,   // <t xml:space="preserve"> 의 공백을 지켜야 한다
        IgnoreComments = true,
        IgnoreProcessingInstructions = true,
        DtdProcessing = DtdProcessing.Prohibit,
        CloseInput = true
    };
}
