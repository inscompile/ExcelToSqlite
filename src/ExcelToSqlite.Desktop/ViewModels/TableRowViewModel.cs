using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ExcelToSqlite.Desktop.Services;
using ExcelToSqlite.Core;

namespace ExcelToSqlite.Desktop.ViewModels;

/// <summary>그리드 한 줄 = 생성될 테이블 하나.</summary>
public sealed partial class TableRowViewModel : ObservableObject
{
    public TableRowViewModel(SourceSpec spec)
    {
        _spec = spec;
        _tableName = spec.TableName ?? "";
        _keyColumnCount = spec.KeyColumnCount;
        _indexColumns = spec.IndexColumns ?? "";
        _delimiter = spec.Delimiter ?? "자동";
        _encoding = spec.Encoding ?? "자동";
        _hasHeader = spec.HasHeader;
        _skipRows = spec.SkipRows;
    }

    private readonly SourceSpec _spec;

    public string FilePath => _spec.FilePath;
    public string FileName => Path.GetFileName(_spec.FilePath);
    public SourceKind Kind => _spec.Kind;
    public bool IsText => _spec.Kind == SourceKind.DelimitedText;
    public string KindLabel => _spec.Kind == SourceKind.DelimitedText ? "텍스트" : "엑셀";
    public string ObjectLabel => _spec.ObjectName ?? "(파일 전체)";

    [ObservableProperty] private bool _include = true;
    [ObservableProperty] private string _tableName;
    [ObservableProperty] private int _keyColumnCount;
    [ObservableProperty] private string _indexColumns;
    [ObservableProperty] private string _delimiter;
    [ObservableProperty] private string _encoding;
    [ObservableProperty] private bool _hasHeader;
    [ObservableProperty] private int _skipRows;
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _hasError;

    public ObservableCollection<string> DelimiterOptions { get; } = new() { "자동", "탭", ",", "|", ";" };
    public ObservableCollection<string> EncodingOptions { get; } = new() { "자동", "utf-8", "cp949" };

    public SourceSpec ToSpec()
    {
        var s = _spec.Clone();
        s.TableName = TableName;
        s.KeyColumnCount = KeyColumnCount;
        s.IndexColumns = string.IsNullOrWhiteSpace(IndexColumns) ? null : IndexColumns;
        s.HasHeader = HasHeader;
        s.SkipRows = SkipRows;
        s.Delimiter = Delimiter switch { "자동" => null, "탭" => "\t", var d => d };
        s.Encoding = Encoding == "자동" ? null : Encoding;
        s.Enabled = Include;
        return s;
    }

    public UiTable ToState() => new()
    {
        Include = Include,
        Kind = Kind.ToString(),
        ObjectName = _spec.ObjectName,
        TableName = TableName,
        KeyColumnCount = KeyColumnCount,
        IndexColumns = string.IsNullOrWhiteSpace(IndexColumns) ? null : IndexColumns,
        Delimiter = Delimiter,
        Encoding = Encoding,
        HasHeader = HasHeader,
        SkipRows = SkipRows
    };

    public static TableRowViewModel FromState(string filePath, UiTable t)
    {
        var spec = new SourceSpec
        {
            FilePath = filePath,
            Kind = Enum.TryParse<SourceKind>(t.Kind, out var k) ? k : SourceKind.DelimitedText,
            ObjectName = t.ObjectName,
            TableName = t.TableName,
            KeyColumnCount = t.KeyColumnCount,
            IndexColumns = t.IndexColumns,
            HasHeader = t.HasHeader,
            SkipRows = t.SkipRows
        };

        return new TableRowViewModel(spec)
        {
            Include = t.Include,
            Delimiter = t.Delimiter ?? "자동",
            Encoding = t.Encoding ?? "자동"
        };
    }
}
