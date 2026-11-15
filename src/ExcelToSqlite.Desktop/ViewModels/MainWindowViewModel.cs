using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExcelToSqlite.Desktop.Services;
using ExcelToSqlite.Core;

namespace ExcelToSqlite.Desktop.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly ImportService _service = new();
    private CancellationTokenSource? _cts;

    public MainWindowViewModel()
    {
        Files.CollectionChanged += OnCollectionChanged;
        Tables.CollectionChanged += OnCollectionChanged;
        RestoreState(UiStateStore.Load());
    }

    // ────────────────────────────── 상태

    public ObservableCollection<FileItemViewModel> Files { get; } = new();
    public ObservableCollection<TableRowViewModel> Tables { get; } = new();
    public ObservableCollection<LogItem> Log { get; } = new();

    [ObservableProperty] private string _dbPath = "";
    [ObservableProperty] private bool _isAppendMode;          // false = 데이터 교체, true = 추가 insert
    [ObservableProperty] private int _duplicateIndex;         // 0 오류 / 1 덮어쓰기 / 2 건너뛰기
    [ObservableProperty] private int _defaultKeyCount = 1;
    [ObservableProperty] private bool _allowAddColumns;
    [ObservableProperty] private bool _keyColumnsNotNull = true;
    [ObservableProperty] private bool _writeManifest = true;
    [ObservableProperty] private bool _continueOnError = true;

    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private double _progressValue;
    [ObservableProperty] private double _progressMax = 1;
    [ObservableProperty] private string _statusText = "엑셀·텍스트 파일을 창에 끌어다 놓으세요.";
    [ObservableProperty] private bool _isDragOver;

    public double WindowWidth { get; set; } = 1180;
    public double WindowHeight { get; set; } = 800;

    /// <summary>RadioButton 양방향 바인딩용(부정 바인딩은 쓰기 불가).</summary>
    public bool IsReplaceMode
    {
        get => !IsAppendMode;
        set { if (value) IsAppendMode = false; }
    }

    public bool DbFileExists => !string.IsNullOrWhiteSpace(DbPath) && File.Exists(DbPath);

    public string DbModeHint => !DbFileExists
        ? "새 DB 파일이 생성됩니다."
        : IsAppendMode
            ? "기존 테이블에 행을 추가합니다. 컬럼 구성이 다르면 오류로 보고됩니다."
            : "동일 이름 테이블을 DROP 후 다시 만듭니다. 기존 데이터는 사라집니다.";

    public int IncludedCount => Tables.Count(t => t.Include);
    public bool CanRun => !IsRunning && IncludedCount > 0 && !string.IsNullOrWhiteSpace(DbPath);

    partial void OnDbPathChanged(string value) => RefreshComputed();
    partial void OnIsAppendModeChanged(bool value) => RefreshComputed();
    partial void OnIsRunningChanged(bool value) => RefreshComputed();

    private void OnCollectionChanged(object? s, NotifyCollectionChangedEventArgs e) => RefreshComputed();

    private void RefreshComputed()
    {
        OnPropertyChanged(nameof(IsReplaceMode));
        OnPropertyChanged(nameof(DbFileExists));
        OnPropertyChanged(nameof(DbModeHint));
        OnPropertyChanged(nameof(IncludedCount));
        OnPropertyChanged(nameof(CanRun));
        RunCommand.NotifyCanExecuteChanged();
    }

    // ────────────────────────────── 파일 추가 (드래그 & 드롭)

    /// <summary>드롭된 경로들을 처리한다. .db 는 대상 DB로, 나머지는 소스로 분류.</summary>
    public async Task AddPathsAsync(IEnumerable<string> paths)
    {
        foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (Directory.Exists(path))
            {
                await AddPathsAsync(Directory.GetFiles(path));
                continue;
            }

            if (!File.Exists(path)) continue;

            var category = SourceExpander.Categorize(path);

            if (category == FileCategory.SqliteDb)
            {
                DbPath = path;
                AddLog($"대상 DB 지정: {Path.GetFileName(path)}", false);
                continue;
            }

            if (Files.Any(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase)))
            {
                AddLog($"이미 추가된 파일입니다: {Path.GetFileName(path)}", false);
                continue;
            }

            try
            {
                var specs = await Task.Run(() => SourceExpander.Expand(path, DefaultKeyCount));

                if (specs.Count == 0)
                {
                    AddLog($"{Path.GetFileName(path)}: 정의된 이름(Name)이 없습니다.", true);
                    continue;
                }

                Files.Add(new FileItemViewModel(path, category, specs.Count));
                foreach (var spec in specs) AddRow(new TableRowViewModel(spec));

                AddLog($"{Path.GetFileName(path)}: 테이블 {specs.Count}개 인식", false);
            }
            catch (Exception ex)
            {
                AddLog($"{Path.GetFileName(path)}: {ex.Message}", true);
            }
        }

        ValidateTableNames();
        StatusText = $"파일 {Files.Count}개 · 테이블 {Tables.Count}개";
    }

    [RelayCommand]
    private void RemoveFile(FileItemViewModel? file)
    {
        if (file is null) return;

        Files.Remove(file);
        foreach (var row in Tables.Where(t =>
                     string.Equals(t.FilePath, file.Path, StringComparison.OrdinalIgnoreCase)).ToList())
            Tables.Remove(row);

        ValidateTableNames();
    }

    [RelayCommand]
    private void Reset()
    {
        Files.Clear();
        Tables.Clear();
        Log.Clear();

        DbPath = "";
        IsAppendMode = false;
        DuplicateIndex = 0;
        DefaultKeyCount = 1;
        AllowAddColumns = false;
        KeyColumnsNotNull = true;
        WriteManifest = true;
        ContinueOnError = true;
        ProgressValue = 0;

        UiStateStore.Clear();
        StatusText = "초기화했습니다. 파일을 끌어다 놓으세요.";
    }

    [RelayCommand]
    private void SelectAll() { foreach (var t in Tables) t.Include = true; RefreshComputed(); }

    [RelayCommand]
    private void SelectNone() { foreach (var t in Tables) t.Include = false; RefreshComputed(); }

    /// <summary>PK 개수 일괄 적용.</summary>
    [RelayCommand]
    private void ApplyKeyCountToAll()
    {
        foreach (var t in Tables) t.KeyColumnCount = DefaultKeyCount;
    }

    // ────────────────────────────── 실행

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RunAsync()
    {
        if (!ValidateTableNames())
        {
            AddLog("테이블명이 중복됩니다. 수정 후 다시 실행하세요.", true);
            return;
        }

        // 이름만 입력했으면 첫 번째 원본 파일 폴더에 만든다. 실제로 만들 전체 경로를 화면에도 보여 준다.
        var sourceDir = Path.GetDirectoryName(Tables.First(t => t.Include).FilePath) ?? "";
        var dbPath = SourceExpander.EnsureDbExtension(SourceExpander.ResolveDbPath(DbPath, sourceDir));
        if (dbPath != DbPath)
        {
            DbPath = dbPath;
            AddLog($"대상 DB 경로: {dbPath}", false);
        }

        var request = new ImportRequest
        {
            DbPath = DbPath,
            Sources = Tables.Where(t => t.Include).Select(t => t.ToSpec()).ToList(),
            Options = new ImportOptions
            {
                DropExistingTable = !IsAppendMode,
                DuplicateKey = DuplicateIndex switch
                {
                    1 => DuplicateKeyPolicy.Replace,
                    2 => DuplicateKeyPolicy.Ignore,
                    _ => DuplicateKeyPolicy.Fail
                },
                AllowAddColumns = AllowAddColumns,
                KeyColumnsNotNull = KeyColumnsNotNull,
                WriteManifest = WriteManifest,
                ContinueOnError = ContinueOnError
            }
        };

        foreach (var t in Tables) { t.Status = ""; t.HasError = false; }

        IsRunning = true;
        ProgressValue = 0;
        ProgressMax = request.Sources.Count;
        _cts = new CancellationTokenSource();

        var progress = new Progress<ImportProgress>(p =>
        {
            ProgressValue = p.SourceIndex + (p.RowsTotal > 0 ? Math.Min(1.0, (double)p.RowsDone / p.RowsTotal) : 0);
            StatusText = p.RowsTotal > 0
                ? $"[{p.SourceIndex + 1}/{p.SourceCount}] {p.TableName} — {p.RowsDone:N0}/{p.RowsTotal:N0}행"
                : $"[{p.SourceIndex + 1}/{p.SourceCount}] {p.TableName} — 읽는 중";
        });

        try
        {
            AddLog($"── 실행 시작 ({(IsAppendMode ? "추가" : "교체")} 모드) → {DbPath}", false);

            var result = await _service.RunAsync(request, progress, _cts.Token);

            foreach (var r in result.Tables)
            {
                var row = Tables.FirstOrDefault(t => t.Include && t.TableName == r.TableName);

                if (r.Success)
                {
                    if (row is not null) row.Status = $"{r.RowCount:N0}행";
                    AddLog($"[OK] {r.TableName} — {r.RowCount:N0}행 · {r.ColumnCount}컬럼 · " +
                           $"PK({string.Join(",", r.KeyColumns)}) · {r.Elapsed.TotalSeconds:F1}s", false);
                }
                else
                {
                    if (row is not null) { row.Status = "실패"; row.HasError = true; }
                    AddLog($"[실패] {r.TableName} — {r.Error}", true);
                }
            }

            ProgressValue = ProgressMax;
            var ok = result.Tables.Count(t => t.Success);
            StatusText = $"완료: 성공 {ok} / 전체 {result.Tables.Count} · {result.Elapsed.TotalSeconds:F1}초";
            AddLog($"── {StatusText}", !result.Success);
        }
        catch (OperationCanceledException)
        {
            StatusText = "사용자가 취소했습니다.";
            AddLog("실행 취소됨", true);
        }
        catch (Exception ex)
        {
            StatusText = "오류로 중단되었습니다.";
            AddLog("오류: " + ex.Message, true);
        }
        finally
        {
            IsRunning = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    private bool ValidateTableNames()
    {
        var groups = Tables.Where(t => t.Include)
                           .GroupBy(t => t.TableName, StringComparer.OrdinalIgnoreCase)
                           .ToList();

        var valid = true;

        foreach (var g in groups)
        {
            var duplicated = g.Count() > 1;
            var empty = string.IsNullOrWhiteSpace(g.Key);

            foreach (var t in g)
            {
                if (t.Status is "중복" or "이름없음") t.Status = "";
                if (duplicated) { t.Status = "중복"; t.HasError = true; valid = false; }
                else if (empty) { t.Status = "이름없음"; t.HasError = true; valid = false; }
                else if (t.Status is "" ) t.HasError = false;
            }
        }

        return valid;
    }

    /// <summary>행의 사용 여부·테이블명 변경이 실행 버튼과 중복 검사에 반영되도록 구독한다.</summary>
    private void AddRow(TableRowViewModel row)
    {
        row.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TableRowViewModel.Include))
            {
                RefreshComputed();
                ValidateTableNames();
            }
            else if (e.PropertyName == nameof(TableRowViewModel.TableName))
            {
                ValidateTableNames();
            }
        };

        Tables.Add(row);
    }

    private void AddLog(string message, bool isError)
    {
        Log.Add(new LogItem(DateTime.Now.ToString("HH:mm:ss"), message, isError));
        while (Log.Count > 500) Log.RemoveAt(0);
    }

    // ────────────────────────────── 직전 화면 설정

    public void SaveState()
    {
        var state = new UiState
        {
            DbPath = DbPath,
            WriteMode = IsAppendMode ? "Append" : "Replace",
            DuplicateKey = DuplicateIndex switch { 1 => "Replace", 2 => "Ignore", _ => "Fail" },
            DefaultKeyCount = DefaultKeyCount,
            AllowAddColumns = AllowAddColumns,
            KeyColumnsNotNull = KeyColumnsNotNull,
            WriteManifest = WriteManifest,
            ContinueOnError = ContinueOnError,
            WindowWidth = WindowWidth,
            WindowHeight = WindowHeight
        };

        foreach (var file in Files)
        {
            state.Files.Add(new UiFile
            {
                Path = file.Path,
                Tables = Tables
                    .Where(t => string.Equals(t.FilePath, file.Path, StringComparison.OrdinalIgnoreCase))
                    .Select(t => t.ToState())
                    .ToList()
            });
        }

        UiStateStore.Save(state);
    }

    private void RestoreState(UiState state)
    {
        DbPath = state.DbPath ?? "";
        IsAppendMode = state.WriteMode == "Append";
        DuplicateIndex = state.DuplicateKey switch { "Replace" => 1, "Ignore" => 2, _ => 0 };
        DefaultKeyCount = state.DefaultKeyCount;
        AllowAddColumns = state.AllowAddColumns;
        KeyColumnsNotNull = state.KeyColumnsNotNull;
        WriteManifest = state.WriteManifest;
        ContinueOnError = state.ContinueOnError;
        WindowWidth = state.WindowWidth;
        WindowHeight = state.WindowHeight;

        foreach (var file in state.Files)
        {
            var category = SourceExpander.Categorize(file.Path);
            Files.Add(new FileItemViewModel(file.Path, category, file.Tables.Count));

            foreach (var t in file.Tables)
                AddRow(TableRowViewModel.FromState(file.Path, t));
        }

        if (Files.Count > 0)
        {
            var missing = Files.Count(f => !f.Exists);
            StatusText = missing > 0
                ? $"직전 설정 복원 · 파일 {Files.Count}개 (누락 {missing}개)"
                : $"직전 설정 복원 · 파일 {Files.Count}개 · 테이블 {Tables.Count}개";
        }
    }
}

public sealed record LogItem(string Time, string Message, bool IsError);
