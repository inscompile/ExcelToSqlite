namespace ExcelToSqlite.Core;

public enum FileCategory { Excel, Text, SqliteDb, Unsupported }

/// <summary>
/// 드롭된 파일 1개 → 테이블 N개로 전개한다.
/// 엑셀: 명명범위(Name)마다 1테이블, 테이블명 = 이름.
/// 텍스트: 파일당 1테이블, 테이블명 = 파일명(확장자 제외).
/// </summary>
public static class SourceExpander
{
    private static readonly string[] ExcelExt = { ".xlsx", ".xlsm", ".xltx", ".xltm" };
    private static readonly string[] DbExt = { ".db", ".sqlite", ".sqlite3", ".db3" };
    private static readonly string[] TextExt = { ".txt", ".tsv", ".csv", ".dat", ".prn", ".tab", ".psv" };

    /// <summary>대상 DB 경로에 확장자가 없으면 <c>.db</c> 를 붙인다. 이미 SQLite 확장자면 그대로 둔다.</summary>
    public static string EnsureDbExtension(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return path;

        var ext = Path.GetExtension(path);
        if (ext.Length > 0 && Array.IndexOf(DbExt, ext.ToLowerInvariant()) >= 0) return path;

        // "요율_2026.1" 처럼 확장자로 보이는 꼬리가 있어도 지우지 않고 뒤에 붙인다.
        return path + ".db";
    }

    /// <summary>
    /// 이름만 입력한 대상 DB 경로(<c>요율</c>, <c>out/요율.db</c>)를 <paramref name="baseDir"/> 기준 전체 경로로 바꾼다.
    /// <c>~/</c> 는 홈 폴더로 푼다. 이미 전체 경로면 그대로 둔다.
    /// </summary>
    /// <remarks>
    /// 화면 앱은 현재 폴더가 맥(.app)에서는 <c>/</c>, 윈도에서는 exe 폴더라서
    /// 상대 경로를 그대로 쓰면 저장에 실패하거나 엉뚱한 곳에 만들어진다.
    /// </remarks>
    public static string ResolveDbPath(string path, string baseDir)
    {
        if (string.IsNullOrWhiteSpace(path)) return path;

        path = path.Trim();
        if (path == "~" || path.StartsWith("~/") || path.StartsWith("~\\"))
            path = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path[1..].TrimStart('/', '\\'));

        return Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(baseDir, path));
    }

    public static FileCategory Categorize(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (Array.IndexOf(ExcelExt, ext) >= 0) return FileCategory.Excel;
        if (Array.IndexOf(DbExt, ext) >= 0) return FileCategory.SqliteDb;
        if (Array.IndexOf(TextExt, ext) >= 0) return FileCategory.Text;
        if (ext == ".xls") return FileCategory.Unsupported;
        return FileCategory.Text;   // 확장자 없는 추출 파일도 텍스트로 취급
    }

    public static List<SourceSpec> Expand(string path, int defaultKeyCount = 1)
    {
        var category = Categorize(path);

        if (category == FileCategory.Unsupported)
            throw new NotSupportedException(
                $"지원하지 않는 형식입니다: {Path.GetFileName(path)} (.xlsx / .xlsm 으로 저장하세요)");

        if (category != FileCategory.Excel)
        {
            return new List<SourceSpec>
            {
                new()
                {
                    FilePath = path,
                    Kind = SourceKind.DelimitedText,
                    TableName = Naming.Sanitize(Path.GetFileNameWithoutExtension(path), "TABLE1"),
                    KeyColumnCount = defaultKeyCount
                }
            };
        }

        var names = ExcelSourceReader.ListDefinedNames(path);
        var specs = new List<SourceSpec>(names.Count);

        foreach (var full in names)
        {
            // "Sheet1!Name" 이면 테이블명은 뒤쪽 이름만 사용
            var bang = full.IndexOf('!');
            var bare = bang >= 0 ? full[(bang + 1)..] : full;

            specs.Add(new SourceSpec
            {
                FilePath = path,
                Kind = SourceKind.ExcelNamedRange,
                ObjectName = full,
                TableName = Naming.Sanitize(bare, "TABLE1"),
                KeyColumnCount = defaultKeyCount
            });
        }

        return specs;
    }
}
