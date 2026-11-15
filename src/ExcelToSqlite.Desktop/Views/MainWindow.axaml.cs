using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using ExcelToSqlite.Core;
using ExcelToSqlite.Desktop.ViewModels;

namespace ExcelToSqlite.Desktop.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // 창 제목에 버전 — 문의를 받을 때 어떤 판인지 알 수 있게 (<Version>은 Directory.Build.props)
        Title += $" {typeof(MainWindow).Assembly.GetName().Version?.ToString(3)}";

        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private MainWindowViewModel? Vm => DataContext as MainWindowViewModel;

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (Vm is null) return;

        // 직전 창 크기 복원
        if (Vm.WindowWidth > 400) Width = Vm.WindowWidth;
        if (Vm.WindowHeight > 300) Height = Vm.WindowHeight;
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (Vm is not null) { Vm.WindowWidth = Width; Vm.WindowHeight = Height; }
        base.OnClosing(e);
    }

    // ────────── 드래그 & 드롭

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        if (Vm is null) return;

        var items = e.DataTransfer.TryGetFiles();
        if (items is null) return;

        var paths = new List<string>();
        foreach (var item in items)
        {
            var path = item.TryGetLocalPath();
            if (!string.IsNullOrEmpty(path)) paths.Add(path);
        }

        if (paths.Count > 0) await Vm.AddPathsAsync(paths);
    }

    // ────────── DB 선택 / 생성

    private async void OnBrowseDb(object? sender, RoutedEventArgs e)
    {
        if (Vm is null) return;

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "기존 SQLite DB 선택",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("SQLite DB") { Patterns = new[] { "*.db", "*.sqlite", "*.sqlite3", "*.db3" } },
                FilePickerFileTypes.All
            }
        });

        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrEmpty(path)) Vm.DbPath = path;
    }

    private async void OnCreateDb(object? sender, RoutedEventArgs e)
    {
        if (Vm is null) return;

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "새 SQLite DB 만들기",
            DefaultExtension = "db",
            SuggestedFileName = "import.db",
            ShowOverwritePrompt = true,
            FileTypeChoices = new[]
            {
                new FilePickerFileType("SQLite DB") { Patterns = new[] { "*.db" } }
            }
        });

        var path = file?.TryGetLocalPath();

        // 확장자 없이 "요율" 처럼 입력해도 .db 가 붙도록. 실행 전에 화면에도 보여야 한다.
        if (!string.IsNullOrEmpty(path)) Vm.DbPath = SourceExpander.EnsureDbExtension(path);
    }
}
