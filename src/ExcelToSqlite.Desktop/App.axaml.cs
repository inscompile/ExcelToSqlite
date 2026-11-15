using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ExcelToSqlite.Desktop.ViewModels;
using ExcelToSqlite.Desktop.Views;

namespace ExcelToSqlite.Desktop;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var vm = new MainWindowViewModel();
            var window = new MainWindow { DataContext = vm };

            // 종료 시 직전 화면 설정 저장
            desktop.ShutdownRequested += (_, _) => vm.SaveState();
            window.Closing += (_, _) => vm.SaveState();

            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
