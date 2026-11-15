namespace ExcelToSqlite.Core;

/// <summary>엑셀에 정의된 이름 하나. 단일 셀 이름은 적재 대상에서 제외된다.</summary>
public sealed record DefinedNameInfo(string Name, bool IsSingleCell);

/// <summary>
/// .xlsx / .xlsm 을 Excel 설치 없이 읽는다. 구형 .xls는 미지원.
/// zip 안의 필요한 파트만 SAX 스트리밍으로 읽으므로 통합문서 크기와 거의 무관하게 빠르다.
/// </summary>
public static class ExcelSourceReader
{
    /// <summary>
    /// UI의 "이름 목록" 드롭다운 채우기용.
    /// 단일 셀만 가리키는 이름은 테이블이 아니라 스칼라 파라미터이므로 제외한다.
    /// </summary>
    public static List<string> ListDefinedNames(string filePath)
        => ListDefinedNamesDetailed(filePath)
           .Where(n => !n.IsSingleCell)
           .Select(n => n.Name)
           .ToList();

    /// <summary>진단용. 단일 셀 이름까지 포함해 전부 돌려준다.</summary>
    public static List<DefinedNameInfo> ListDefinedNamesDetailed(string filePath)
    {
        using var pkg = XlsxPackage.Open(filePath);
        return Describe(pkg);
    }

    public static List<string> ListSheets(string filePath)
    {
        using var pkg = XlsxPackage.Open(filePath);
        return pkg.SheetNames;
    }

    /// <summary>이름 목록과 시트 목록을 한 번에. 파일을 한 번만 연다.</summary>
    public static (List<DefinedNameInfo> Names, List<string> Sheets) Inspect(string filePath)
    {
        using var pkg = XlsxPackage.Open(filePath);
        return (Describe(pkg), pkg.SheetNames);
    }

    private static List<DefinedNameInfo> Describe(XlsxPackage pkg)
        => pkg.DefinedNames
              // 깨진(#REF!) 이름은 단일 셀로 보지 않는다 — 적재할 때 오류로 보고되어야 한다.
              .Select(n => new DefinedNameInfo(n.DisplayName, n.Area?.IsSingleCell ?? false))
              .ToList();

    /// <summary>
    /// 워크북을 열어 호출자에게 넘긴다. 같은 파일의 소스를 연속 처리할 때
    /// <see cref="Read(XlsxPackage, SourceSpec)"/> 와 함께 쓴다. 호출자가 Dispose 책임을 진다.
    /// </summary>
    public static XlsxPackage OpenWorkbook(string filePath) => XlsxPackage.Open(filePath);

    public static TableData Read(SourceSpec spec)
    {
        using var pkg = XlsxPackage.Open(spec.FilePath);
        return Read(pkg, spec);
    }

    /// <summary>이미 열린 패키지에서 읽는다. 같은 파일의 이름을 여러 개 적재할 때 쓴다.</summary>
    public static TableData Read(XlsxPackage pkg, SourceSpec spec)
    {
        var (sheetName, area) = Resolve(pkg, spec);

        var rows = new List<object?[]>();
        List<string>? columns = null;
        var colCount = area?.ColCount ?? 0;

        // 헤더 앞에서 건너뛸 행 + 헤더 행을 세기 위한 카운터.
        var seen = 0;
        var headerTaken = !spec.HasHeader;

        foreach (var (_, raw) in pkg.ReadRows(sheetName, area))
        {
            seen++;
            if (seen <= spec.SkipRows) continue;

            var values = Fit(raw, colCount);

            if (!headerTaken)
            {
                columns = Naming.NormalizeHeaders(values.Select(v => v?.ToString()).ToList());
                colCount = columns.Count;
                headerTaken = true;
                continue;
            }

            if (values.All(v => v is null || (v is string s && s.Length == 0))) continue;   // 완전 공백 행 skip

            if (colCount > 0 && values.Length != colCount) values = Fit(values, colCount);
            rows.Add(values);
        }

        if (spec.HasHeader && columns is null)
            throw new InvalidOperationException(
                spec.SkipRows > 0
                    ? "SkipRows가 범위 크기보다 큽니다."
                    : $"범위가 비어 있습니다: {spec.ObjectName ?? sheetName}");

        columns ??= Enumerable.Range(1, colCount).Select(i => $"COL{i}").ToList();

        if (columns.Count == 0)
            throw new InvalidOperationException($"범위가 비어 있습니다: {spec.ObjectName ?? sheetName}");

        return new TableData
        {
            TableName = Naming.Sanitize(
                spec.TableName ?? spec.ObjectName ?? Path.GetFileNameWithoutExtension(spec.FilePath),
                "TABLE1"),
            SourcePath = spec.FilePath,
            SourceObject = spec.ObjectName,
            Columns = columns,
            Rows = rows
        };
    }

    /// <summary>행 길이를 컬럼 수에 맞춘다. 0이면 원본 길이를 그대로 쓴다(시트 전체 읽기).</summary>
    private static object?[] Fit(string?[] raw, int width)
    {
        if (width <= 0) return raw.Cast<object?>().ToArray();

        var values = new object?[width];
        for (var i = 0; i < width && i < raw.Length; i++) values[i] = raw[i];
        return values;
    }

    private static object?[] Fit(object?[] raw, int width)
    {
        if (raw.Length == width) return raw;

        var values = new object?[width];
        for (var i = 0; i < width && i < raw.Length; i++) values[i] = raw[i];
        return values;
    }

    /// <summary>소스 지정을 실제 (시트, 영역)으로 바꾼다. 영역이 null이면 시트 전체.</summary>
    private static (string Sheet, CellArea? Area) Resolve(XlsxPackage pkg, SourceSpec spec)
    {
        if (spec.Kind == SourceKind.ExcelSheet)
        {
            var sheet = spec.ObjectName ?? pkg.SheetNames.FirstOrDefault()
                        ?? throw new InvalidOperationException("시트가 없습니다.");

            if (!pkg.TryGetSheetPart(sheet, out _))
                throw new InvalidOperationException($"시트를 찾을 수 없습니다: {sheet}");

            return (sheet, null);
        }

        var name = spec.ObjectName
                   ?? throw new InvalidOperationException("명명범위(Name)가 지정되지 않았습니다.");

        // "Sheet1!MyName" 으로 시트 범위 이름을 지정할 수 있다.
        string? scope = null;
        var bang = name.IndexOf('!');
        if (bang > 0)
        {
            scope = name[..bang].Trim('\'');
            name = name[(bang + 1)..];
        }

        var match = pkg.DefinedNames.FirstOrDefault(n =>
                        string.Equals(n.Name, name, StringComparison.OrdinalIgnoreCase)
                        && (scope is null
                            || string.Equals(n.Scope, scope, StringComparison.OrdinalIgnoreCase)))
                    ?? throw new InvalidOperationException(
                        $"이름 '{name}' 을(를) 찾을 수 없습니다. 파일: {Path.GetFileName(spec.FilePath)}");

        if (match.IsBroken || match.Area is null || match.SheetName is null)
            throw new InvalidOperationException(
                $"이름 '{name}' 이(가) 유효한 범위를 가리키지 않습니다(#REF! 가능).");

        if (!pkg.TryGetSheetPart(match.SheetName, out _))
            throw new InvalidOperationException(
                $"이름 '{name}' 이 가리키는 시트를 찾을 수 없습니다: {match.SheetName}");

        return (match.SheetName, match.Area);
    }
}
