using System.Text;

namespace ExcelToSqlite.Core;

/// <summary>TSV/CSV/파이프 구분 텍스트를 읽는다(따옴표 이스케이프 지원).</summary>
public static class DelimitedTextReader
{
    private static readonly char[] CandidateDelimiters = { '\t', ',', '|', ';' };

    // CP949(EUC-KR) 등 코드 페이지 인코딩은 등록해야 GetEncoding 으로 꺼낼 수 있다.
    // 인코딩을 쓰는 곳이 이 클래스뿐이라 정적 생성자에서 한 번 등록한다 (첫 호출 전에 반드시 실행됨).
    static DelimitedTextReader() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public static TableData Read(SourceSpec spec)
    {
        if (!File.Exists(spec.FilePath))
            throw new FileNotFoundException($"텍스트 파일을 찾을 수 없습니다: {spec.FilePath}", spec.FilePath);

        var bytes = File.ReadAllBytes(spec.FilePath);
        var encoding = ResolveEncoding(bytes, spec.Encoding);
        var text = encoding.GetString(bytes);

        if (text.Length > 0 && text[0] == '\uFEFF') text = text[1..];

        var records = ParseRecords(text, ResolveDelimiter(text, spec.Delimiter));

        for (var i = 0; i < spec.SkipRows && records.Count > 0; i++)
            records.RemoveAt(0);

        if (records.Count == 0)
            throw new InvalidOperationException($"데이터가 없습니다: {Path.GetFileName(spec.FilePath)}");

        List<string> columns;
        var dataStart = 0;

        if (spec.HasHeader)
        {
            columns = Naming.NormalizeHeaders(records[0].Select(x => (string?)x).ToList());
            dataStart = 1;
        }
        else
        {
            var width = records.Max(r => r.Count);
            columns = Enumerable.Range(1, width).Select(i => $"COL{i}").ToList();
        }

        var colCount = columns.Count;
        var rows = new List<object?[]>(records.Count - dataStart);

        for (var i = dataStart; i < records.Count; i++)
        {
            var rec = records[i];
            if (rec.Count == 1 && rec[0].Trim().Length == 0) continue;   // 빈 줄

            var values = new object?[colCount];
            for (var c = 0; c < colCount; c++)
                values[c] = c < rec.Count ? rec[c] : null;

            rows.Add(values);
        }

        return new TableData
        {
            TableName = Naming.Sanitize(
                spec.TableName ?? Path.GetFileNameWithoutExtension(spec.FilePath), "TABLE1"),
            SourcePath = spec.FilePath,
            SourceObject = null,
            Columns = columns,
            Rows = rows
        };
    }

    /// <summary>BOM → 엄격 UTF-8 → CP949 순으로 판별. 한글 레거시 파일 대응.</summary>
    public static Encoding ResolveEncoding(byte[] bytes, string? requested)
    {
        if (!string.IsNullOrWhiteSpace(requested) &&
            !requested.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            return requested.Trim().ToLowerInvariant() switch
            {
                // .NET 이름표에는 "cp949"가 없고, "euc-kr"은 확장 한글이 빠진 51949라 '똠'·'햏' 같은 글자가 깨진다.
                // 한국에서 EUC-KR이라 부르는 파일은 사실상 CP949이므로 둘 다 949(상위 호환)로 읽는다.
                "cp949" or "ms949" or "windows-949" or "euc-kr" or "euckr" => Encoding.GetEncoding(949),
                var name when int.TryParse(name, out var codePage) => Encoding.GetEncoding(codePage),
                var name => Encoding.GetEncoding(name)
            };
        }

        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) return new UTF8Encoding(true);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) return Encoding.Unicode;
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF) return Encoding.BigEndianUnicode;

        try
        {
            new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
            return new UTF8Encoding(false);
        }
        catch (DecoderFallbackException)
        {
            try { return Encoding.GetEncoding(949); }   // CP949 / EUC-KR
            catch { return Encoding.Latin1; }
        }
    }

    public static char ResolveDelimiter(string text, string? requested)
    {
        if (!string.IsNullOrEmpty(requested))
        {
            return requested switch
            {
                "\\t" or "tab" or "TAB" => '\t',
                _ => requested[0]
            };
        }

        var end = text.IndexOfAny(new[] { '\r', '\n' });
        var firstLine = end < 0 ? text : text[..end];

        var best = '\t';
        var bestCount = -1;

        foreach (var d in CandidateDelimiters)
        {
            var count = firstLine.Count(ch => ch == d);
            if (count > bestCount) { bestCount = count; best = d; }
        }

        return bestCount <= 0 ? '\t' : best;
    }

    /// <summary>RFC4180 스타일 파서. 따옴표 안의 구분자/줄바꿈을 보존한다.</summary>
    private static List<List<string>> ParseRecords(string text, char delimiter)
    {
        var records = new List<List<string>>();
        var record = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];

            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else inQuotes = false;
                }
                else field.Append(ch);
                continue;
            }

            if (ch == '"' && field.Length == 0) { inQuotes = true; continue; }

            if (ch == delimiter)
            {
                record.Add(field.ToString());
                field.Clear();
                continue;
            }

            if (ch == '\r') continue;

            if (ch == '\n')
            {
                record.Add(field.ToString());
                field.Clear();
                records.Add(record);
                record = new List<string>();
                continue;
            }

            field.Append(ch);
        }

        if (field.Length > 0 || record.Count > 0)
        {
            record.Add(field.ToString());
            records.Add(record);
        }

        return records;
    }
}
