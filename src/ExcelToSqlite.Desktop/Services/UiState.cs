using System.Text.Json;
using System.Text.Json.Serialization;

namespace ExcelToSqlite.Desktop.Services;

/// <summary>직전 화면 설정(재실행 시 복원 대상).</summary>
public sealed class UiState
{
    public string? DbPath { get; set; }
    public string WriteMode { get; set; } = "Replace";      // Replace | Append
    public string DuplicateKey { get; set; } = "Fail";      // Fail | Replace | Ignore
    public int DefaultKeyCount { get; set; } = 1;
    public bool AllowAddColumns { get; set; }
    public bool KeyColumnsNotNull { get; set; } = true;
    public bool WriteManifest { get; set; } = true;
    public bool ContinueOnError { get; set; } = true;
    public double WindowWidth { get; set; } = 1180;
    public double WindowHeight { get; set; } = 800;
    public List<UiFile> Files { get; set; } = new();
}

public sealed class UiFile
{
    public string Path { get; set; } = "";
    public List<UiTable> Tables { get; set; } = new();
}

public sealed class UiTable
{
    public bool Include { get; set; } = true;
    public string Kind { get; set; } = "ExcelNamedRange";
    public string? ObjectName { get; set; }
    public string TableName { get; set; } = "";
    public int KeyColumnCount { get; set; } = 1;
    public string? IndexColumns { get; set; }
    public string? Delimiter { get; set; }
    public string? Encoding { get; set; }
    public bool HasHeader { get; set; } = true;
    public int SkipRows { get; set; }
}

/// <summary>OS별 사용자 설정 폴더에 상태를 저장/복원한다.</summary>
public static class UiStateStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string FilePath
    {
        get
        {
            var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (string.IsNullOrEmpty(root))
                root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");

            var dir = Path.Combine(root, "ExcelToSqlite");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "ui-state.json");
        }
    }

    public static UiState Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new UiState();
            return JsonSerializer.Deserialize<UiState>(File.ReadAllText(FilePath), Options) ?? new UiState();
        }
        catch
        {
            return new UiState();   // 손상된 상태 파일은 무시하고 기본값으로 시작
        }
    }

    public static void Save(UiState state)
    {
        try { File.WriteAllText(FilePath, JsonSerializer.Serialize(state, Options)); }
        catch { /* 설정 저장 실패가 앱을 막지 않게 한다 */ }
    }

    public static void Clear()
    {
        try { if (File.Exists(FilePath)) File.Delete(FilePath); } catch { }
    }
}
