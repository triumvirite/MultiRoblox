using System.Windows;
using System.Windows.Controls;

namespace MultiRoblox.App.Services;

/// <summary>Tiny code-built prompt so we don't need a XAML window for one text field.</summary>
public static class Dialogs
{
    public static string? Prompt(string title, string message, string initial = "")
    {
        var win = new Window
        {
            Title = title,
            SizeToContent = SizeToContent.Height,
            Width = 380,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Application.Current.MainWindow,
            ResizeMode = ResizeMode.NoResize,
            Background = (System.Windows.Media.Brush)Application.Current.TryFindResource("Bg"),
        };

        var box = new TextBox { Text = initial, Margin = new Thickness(0, 10, 0, 0) };
        var ok = new Button { Content = "OK", Width = 80, IsDefault = true,
            Style = (Style)Application.Current.TryFindResource("AccentButton") };
        var cancel = new Button { Content = "Cancel", Width = 80, Margin = new Thickness(8, 0, 0, 0), IsCancel = true };

        string? result = null;
        ok.Click += (_, _) => { result = box.Text.Trim(); win.DialogResult = true; };
        cancel.Click += (_, _) => win.DialogResult = false;

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0) };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(box);
        panel.Children.Add(buttons);
        win.Content = panel;

        box.Loaded += (_, _) => { box.Focus(); box.SelectAll(); };
        return win.ShowDialog() == true && !string.IsNullOrWhiteSpace(result) ? result : null;
    }
}
