using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using MultiRoblox.App.ViewModels;
using MultiRoblox.Core.Services;

namespace MultiRoblox.App;

/// <summary>
/// Attached property for a <see cref="TextBlock"/> that renders a lightweight markup string where
/// text wrapped in <c>*asterisks*</c> is shown bold and in a high-contrast colour — used for the
/// status bar so the subject of a message ("Re-logged in *Account Name*") stands out.
/// </summary>
public static class StatusMarkup
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.RegisterAttached(
            "Text", typeof(string), typeof(StatusMarkup),
            new PropertyMetadata(null, OnTextChanged));

    public static string GetText(DependencyObject o) => (string)o.GetValue(TextProperty);
    public static void SetText(DependencyObject o, string v) => o.SetValue(TextProperty, v);

    private static void OnTextChanged(DependencyObject o, DependencyPropertyChangedEventArgs e)
    {
        if (o is not TextBlock tb) return;
        tb.Inlines.Clear();

        // Split on '*'; every odd-indexed segment sat between a pair of asterisks → emphasise it.
        var parts = ((e.NewValue as string) ?? "").Split('*');
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length == 0) continue;

            if (i % 2 == 1)   // sat between a pair of asterisks
            {
                var span = new Span(new Run(parts[i])) { FontWeight = FontWeights.SemiBold };
                tb.Inlines.Add(span);
                // pure white (dark) / pure black (light). DynamicResource so it re-colours on theme
                // switch; set after parenting so the reference resolves against the live tree.
                span.SetResourceReference(TextElement.ForegroundProperty, "StatusEmphasisText");
            }
            else
            {
                tb.Inlines.Add(new Run(parts[i]));
            }
        }
    }
}

/// <summary>Account status dot / label colour: in-game = green, signed in = blue, needs re-login = red.</summary>
public sealed class AccountStatusToBrushConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
    {
        var app = Application.Current;
        return value switch
        {
            AccountStatusKind.InGame => app.TryFindResource("Ok") ?? Brushes.MediumSeaGreen,
            AccountStatusKind.SignedIn => app.TryFindResource("Info") ?? Brushes.DodgerBlue,
            AccountStatusKind.NeedsLogin => app.TryFindResource("Danger") ?? Brushes.IndianRed,
            _ => app.TryFindResource("SubtleText") ?? Brushes.Gray,
        };
    }

    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

public sealed class RelativeTimeConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
    {
        if (value is not DateTimeOffset dt) return "";
        var local = dt.LocalDateTime;
        return local.Date == DateTime.Today
            ? "Today " + local.ToString("h:mm tt", c)
            : local.ToString("MMM d, yyyy  h:mm tt", c);
    }

    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

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

public sealed class UpdateStatusToBrushConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
    {
        var app = Application.Current;
        return value?.ToString() switch
        {
            "UpToDate" => app.TryFindResource("Ok") ?? Brushes.Green,
            "Available" => app.TryFindResource("Warn") ?? Brushes.Orange,
            "Unknown" => app.TryFindResource("Danger") ?? Brushes.Red,
            "Failed" => app.TryFindResource("Danger") ?? Brushes.Red,
            "Critical" => app.TryFindResource("Danger") ?? Brushes.Red,
            "Installing" => app.TryFindResource("Info") ?? Brushes.DodgerBlue,
            _ => app.TryFindResource("SubtleText") ?? Brushes.Gray,
        };
    }

    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

public sealed class EqualsToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) =>
        string.Equals(value?.ToString(), p?.ToString(), StringComparison.Ordinal)
            ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

public sealed class NullToBoolConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) => value is not null;
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>Non-empty string → Visible, empty/null → Collapsed.</summary>
public sealed class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) =>
        string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}
