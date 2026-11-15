using CommunityToolkit.Mvvm.ComponentModel;
using ExcelToSqlite.Core;

namespace ExcelToSqlite.Desktop.ViewModels;

/// <summary>왼쪽 파일 목록의 한 줄.</summary>
public sealed partial class FileItemViewModel : ObservableObject
{
    public FileItemViewModel(string path, FileCategory category, int tableCount)
    {
        Path = path;
        Category = category;
        _tableCount = tableCount;
        _exists = File.Exists(path);
    }

    public string Path { get; }
    public FileCategory Category { get; }
    public string FileName => System.IO.Path.GetFileName(Path);
    public string Directory => System.IO.Path.GetDirectoryName(Path) ?? "";
    public string CategoryLabel => Category == FileCategory.Excel ? "XLSX" : "TEXT";

    [ObservableProperty] private int _tableCount;
    [ObservableProperty] private bool _exists;

    partial void OnTableCountChanged(int value) => OnPropertyChanged(nameof(Detail));
    partial void OnExistsChanged(bool value) => OnPropertyChanged(nameof(Detail));

    public string Detail => Exists
        ? (Category == FileCategory.Excel ? $"이름 {TableCount}개" : "테이블 1개")
        : "파일 없음";
}
