using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace ExcelToSqlite.Desktop.ViewModels;

public static class Converters
{
    /// <summary>true → 빨강, false → 기본 텍스트색.</summary>
    public static readonly IValueConverter ErrorBrush =
        new FuncValueConverter<bool, IBrush>(isError =>
            isError ? new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28)) : Brushes.Gray);
}
