using System.Windows;
using MultiRoblox.Core.Services;

namespace MultiRoblox.App.Views;

public partial class UpdateWindow : Window
{
    private readonly UpdateService _svc = new();
    private UpdateInfo? _info;

    public UpdateWindow(UpdateInfo? prefetched = null)
    {
        InitializeComponent();
        _info = prefetched;
        Loaded += async (_, _) =>
        {
            if (_info is not null) Render(_info);
            else await CheckAsync();
        };
    }

    private async Task CheckAsync()
    {
        Headline.Text = "Checking…";
        CurrentText.Text = $"v{UpdateService.CurrentVersion.ToString(3)}";
        LatestText.Text = "…";
        try
        {
            _info = await _svc.CheckAsync();
            Render(_info);
        }
        catch (Exception ex)
        {
            Headline.Text = "Couldn't check for updates";
            StatusText.Text = ex.Message;
        }
    }

    private void Render(UpdateInfo info)
    {
        CurrentText.Text = $"v{info.Current.ToString(3)}";
        LatestText.Text = info.LatestTag;

        if (info.UpdateAvailable)
        {
            Headline.Text = "Update available";
            YesButton.Visibility = Visibility.Visible;
            NoButton.Content = "Later";
            if (!string.IsNullOrWhiteSpace(info.Notes))
            {
                NotesText.Text = info.Notes.Trim();
                NotesBox.Visibility = Visibility.Visible;
            }
        }
        else if (info.Latest > info.Current)
        {
            Headline.Text = "Update available";
            StatusText.Text = "The latest release has no downloadable exe yet — try again shortly.";
        }
        else
        {
            Headline.Text = "You're up to date";
        }
    }

    private async void Yes_Click(object sender, RoutedEventArgs e)
    {
        if (_info is null) return;
        YesButton.IsEnabled = false;
        NoButton.IsEnabled = false;
        Progress.Visibility = Visibility.Visible;
        StatusText.Text = "Downloading…";

        var progress = new Progress<double>(p =>
        {
            Progress.Value = p;
            StatusText.Text = p >= 1 ? "Installing…" : $"Downloading… {p:P0}";
        });

        try
        {
            await _svc.DownloadAndApplyAsync(_info, progress);
            StatusText.Text = "Restarting…";
            await Task.Delay(400);
            ((App)Application.Current).ShutdownApp();
        }
        catch (Exception ex)
        {
            StatusText.Text = "Update failed: " + ex.Message;
            YesButton.IsEnabled = true;
            NoButton.IsEnabled = true;
        }
    }

    private void No_Click(object sender, RoutedEventArgs e) => Close();
}
