using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using MultiRoblox.Core.Services;

namespace MultiRoblox.App;

public sealed class HealthToBrushConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
    {
        var app = Application.Current;
        return value switch
        {
            AccountHealth.Valid => app.TryFindResource("Ok") ?? Brushes.Green,
            AccountHealth.NeedsAttention => app.TryFindResource("Danger") ?? Brushes.Red,
            _ => app.TryFindResource("SubtleText") ?? Brushes.Gray,
        };
    }

    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type t, object p, CultureInfo c)
    {
        bool b = value is bool x && x;
        if (Invert) b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

public sealed class NullToBoolConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) => value is not null;
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}
