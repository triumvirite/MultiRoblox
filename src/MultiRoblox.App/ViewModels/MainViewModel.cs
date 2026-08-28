using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MultiRoblox.App.Services;
using MultiRoblox.App.Views;
using MultiRoblox.Core.Models;
using MultiRoblox.Core.Services;

namespace MultiRoblox.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    public const string AllCategories = "All Accounts";

    private readonly AppServices _svc;

    public ObservableCollection<AccountItemViewModel> Accounts { get; } = new();
    public ObservableCollection<InstanceItemViewModel> Instances { get; } = new();
    public ObservableCollection<string> Categories { get; } = new();
    public ICollectionView AccountsView { get; }

    [ObservableProperty] private AccountItemViewModel? _selectedAccount;
    [ObservableProperty] private string _selectedCategory = AllCategories;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private string _status = "Ready";
    [ObservableProperty] private bool _busy;

    // --- join panel -------------------------------------------------
    public enum JoinMode { Manual, Favorites, PlayerFinder }

    [ObservableProperty] private JoinMode _joinModeValue = JoinMode.Manual;
    [ObservableProperty] private string _placeIdInput = "";
    [ObservableProperty] private string _jobIdInput = "";

    public ObservableCollection<FavoriteGame> Favorites { get; } = new();
    [ObservableProperty] private FavoriteGame? _selectedFavorite;

    [ObservableProperty] private string _finderQuery = "";
    public ObservableCollection<PlayerFindResult> FinderResults { get; } = new();

    // --- theme toggle ---------------------------------------------
    [ObservableProperty] private string _themeToggleText = "";
    [ObservableProperty] private Brush _themeToggleBackground = Brushes.Transparent;
    [ObservableProperty] private Brush _themeToggleForeground = Brushes.White;

    // --- update panel --------------------------------------------
    [ObservableProperty] private UpdateStatus _updateState = UpdateStatus.Checking;
    [ObservableProperty] private string _updateTooltip = "Checking for updates…";
    [ObservableProperty] private string _updateButtonText = "Check for update";
    [ObservableProperty] private bool _updatePopupOpen;
    [ObservableProperty] private string _updateHeadline = "Checking…";
    [ObservableProperty] private string _updateInstalledText = "";
    [ObservableProperty] private string _updateLatestText = "";
    [ObservableProperty] private string _updateNotes = "";
    [ObservableProperty] private bool _updateHasNotes;
    [ObservableProperty] private bool _updateCanInstall;
    [ObservableProperty] private bool _updateInstalling;
    [ObservableProperty] private double _updateProgress;
    private UpdateInfo? _cachedUpdate;

    public MainViewModel(AppServices svc)
    {
        _svc = svc;

        AccountsView = CollectionViewSource.GetDefaultView(Accounts);
        AccountsView.Filter = FilterAccount;

        RebuildCategories();
        ReloadFavorites();
        ReloadAccounts();

        _svc.KeepAlive.HealthChanged += (_, e) => OnUi(() =>
        {
            var vm = Accounts.FirstOrDefault(a => a.Id == e.AccountId);
            if (vm is not null) vm.Health = e.Health;
        });
        _svc.Instances.InstanceChanged += (_, inst) => OnUi(() => SyncInstance(inst));
        _svc.Accounts.Changed += (_, _) => OnUi(() => { RebuildCategories(); ReloadAccounts(); });

        UpdateThemeToggle();
        _ = CheckForUpdateSilentlyAsync();
    }

    // --- categories ----------------------------------------------

    public void RebuildCategories()
    {
        var wanted = new List<string> { AllCategories };
        wanted.AddRange(_svc.Settings.Current.Categories);
        wanted.AddRange(_svc.Accounts.Accounts
            .Select(a => a.EffectiveGroup)
            .Where(g => g != "Default"));
        var distinct = wanted.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        Categories.Clear();
        foreach (var c in distinct) Categories.Add(c);
        if (!Categories.Contains(SelectedCategory)) SelectedCategory = AllCategories;
    }

    [RelayCommand]
    private void NewCategory()
    {
        var name = Dialogs.Prompt("New category", "Name for the new category:");
        if (string.IsNullOrWhiteSpace(name) || name.Equals(AllCategories, StringComparison.OrdinalIgnoreCase)) return;
        name = name.Trim();
        if (!_svc.Settings.Current.Categories.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            _svc.Settings.Current.Categories.Add(name);
            _svc.Settings.Save();
        }
        RebuildCategories();
        SelectedCategory = name;
    }

    public void AssignCategory(AccountItemViewModel item, string category)
    {
        item.Model.Group = category == AllCategories ? "" : category;
        _svc.Accounts.Update(item.Model);
        Status = $"Moved {item.Label} to {(string.IsNullOrEmpty(item.Model.Group) ? "Default" : category)}.";
    }

    partial void OnSelectedCategoryChanged(string value) => AccountsView.Refresh();

    // --- account list -------------------------------------------

    private void ReloadAccounts()
    {
        var selectedId = SelectedAccount?.Id;
        Accounts.Clear();
        foreach (var acc in _svc.Accounts.Accounts)
            Accounts.Add(new AccountItemViewModel(acc) { Health = _svc.KeepAlive.GetHealth(acc.Id) });
        AccountsView.Refresh();
        if (selectedId is not null)
            SelectedAccount = Accounts.FirstOrDefault(a => a.Id == selectedId);
    }

    private bool FilterAccount(object o)
    {
        if (o is not AccountItemViewModel vm) return false;
        if (SelectedCategory != AllCategories &&
            !vm.Group.Equals(SelectedCategory, StringComparison.OrdinalIgnoreCase))
            return false;
        if (string.IsNullOrWhiteSpace(SearchText)) return true;
        return vm.Label.Contains(SearchText, StringComparison.OrdinalIgnoreCase);
    }

    partial void OnSearchTextChanged(string value) => AccountsView.Refresh();

    partial void OnSelectedAccountChanged(AccountItemViewModel? value)
    {
        if (value is not null)
        {
            PlaceIdInput = value.Model.SavedPlaceId;
            JobIdInput = value.Model.SavedJobId;
        }
        JoinCommand.NotifyCanExecuteChanged();
        AddFavoriteCommand.NotifyCanExecuteChanged();
        RemoveAccountCommand.NotifyCanExecuteChanged();
        RefreshAccountCommand.NotifyCanExecuteChanged();
        OpenUtilitiesCommand.NotifyCanExecuteChanged();
        OpenInBrowserCommand.NotifyCanExecuteChanged();
        FindPlayersCommand.NotifyCanExecuteChanged();
    }

    // --- join: mode switching -----------------------------------

    [RelayCommand]
    private void SetJoinMode(string mode) => JoinModeValue = Enum.Parse<JoinMode>(mode);

    private bool CanJoin() =>
        SelectedAccount is not null && !Busy && GameLinkParser.TryParse(PlaceIdInput, out _);

    partial void OnPlaceIdInputChanged(string value)
    {
        JoinCommand.NotifyCanExecuteChanged();
        AddFavoriteCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanJoin))]
    private async Task JoinAsync()
    {
        var acc = SelectedAccount!.Model;
        if (!GameLinkParser.TryParse(PlaceIdInput, out var link)) { Status = "Couldn't find a place id in that."; return; }

        JoinRequest join = link.JobId is null && link.PrivateServerLinkCode is null && link.AccessCode is null
                           && !string.IsNullOrWhiteSpace(JobIdInput)
            ? JoinRequest.Server(link.PlaceId, JobIdInput.Trim())
            : link.ToJoinRequest();

        PlaceIdInput = link.PlaceId.ToString();
        acc.SavedPlaceId = link.PlaceId.ToString();
        acc.SavedJobId = join.JobId ?? "";
        acc.LastUsed = DateTimeOffset.Now;
        _svc.Accounts.Update(acc);
        await LaunchAsync(acc, join);
    }

    // --- join: favorites (local, app-managed) ----------------

    private bool HasSelection() => SelectedAccount is not null;

    private void ReloadFavorites()
    {
        Favorites.Clear();
        foreach (var f in _svc.Settings.Current.Favorites.OrderBy(f => f.Name))
            Favorites.Add(f);
    }

    private bool CanAddFavorite() => SelectedAccount is not null && GameLinkParser.TryParse(PlaceIdInput, out _);

    [RelayCommand(CanExecute = nameof(CanAddFavorite))]
    private async Task AddFavoriteAsync()
    {
        if (!GameLinkParser.TryParse(PlaceIdInput, out var link)) return;
        if (_svc.Settings.Current.Favorites.Any(f => f.PlaceId == link.PlaceId))
        {
            Status = "Already in favorites.";
            JoinModeValue = JoinMode.Favorites;
            return;
        }
        Status = "Adding to favorites…";
        string? name = null;
        try { name = await new GamesClient(_svc.Pool.Get(SelectedAccount!.Model)).GetPlaceNameAsync(link.PlaceId); }
        catch { }

        var fav = new FavoriteGame { PlaceId = link.PlaceId, Name = string.IsNullOrWhiteSpace(name) ? $"Place {link.PlaceId}" : name! };
        _svc.Settings.Current.Favorites.Add(fav);
        _svc.Settings.Save();
        ReloadFavorites();
        SelectedFavorite = Favorites.FirstOrDefault(f => f.PlaceId == fav.PlaceId);
        JoinModeValue = JoinMode.Favorites;
        Status = $"Favorited {fav.Name}.";
    }

    [RelayCommand]
    private void RemoveFavorite(FavoriteGame? game)
    {
        game ??= SelectedFavorite;
        if (game is null) return;
        _svc.Settings.Current.Favorites.RemoveAll(f => f.PlaceId == game.PlaceId);
        _svc.Settings.Save();
        ReloadFavorites();
        Status = $"Removed {game.Name} from favorites.";
    }

    [RelayCommand]
    private async Task JoinFavoriteAsync(FavoriteGame? game)
    {
        game ??= SelectedFavorite;
        if (game is null || SelectedAccount is null || game.PlaceId == 0) return;
        await LaunchAsync(SelectedAccount.Model, JoinRequest.Place(game.PlaceId));
    }

    // --- join: player finder ---------------------------------

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task FindPlayersAsync()
    {
        var acc = SelectedAccount?.Model;
        if (acc is null) return;
        FinderResults.Clear();
        var names = FinderQuery.Split(new[] { ',', ' ', '\n', '\r', ';' }, StringSplitOptions.RemoveEmptyEntries);
        if (names.Length == 0) { Status = "Enter usernames or IDs."; return; }

        try
        {
            Busy = true;
            var games = new GamesClient(_svc.Pool.Get(acc));
            var ids = new List<long>();
            foreach (var n in names)
                if (long.TryParse(n, out var raw)) ids.Add(raw);
                else { var r = await games.ResolveUsernameAsync(n); if (r is not null) ids.Add(r.Value); }

            if (ids.Count == 0) { Status = "No users resolved."; return; }

            foreach (var p in await games.FindPlayersAsync(ids))
            {
                bool joinable = p is { PresenceType: 2, PlaceId: > 0 } && !string.IsNullOrEmpty(p.GameId);
                string where = p.PresenceType switch
                {
                    0 => "offline",
                    1 => "online (website)",
                    2 => joinable ? $"in game — {p.LastLocation}" : "in game (can't join)",
                    3 => "in Studio",
                    _ => p.LastLocation ?? "?",
                };
                FinderResults.Add(new PlayerFindResult(p.UserId, $"{p.UserId} — {where}", p.PlaceId, p.GameId, joinable));
            }
            Status = $"{FinderResults.Count} result(s).";
        }
        catch (Exception ex) { Status = "Finder failed: " + ex.Message; }
        finally { Busy = false; }
    }

    [RelayCommand]
    private async Task JoinFoundPlayerAsync(PlayerFindResult? r)
    {
        if (r is null || !r.Joinable || SelectedAccount is null || r.PlaceId is not { } place || r.GameId is not { } job) return;
        await LaunchAsync(SelectedAccount.Model, JoinRequest.Server(place, job));
    }

    // --- launching & instances -------------------------------

    public async Task LaunchAsync(Account acc, JoinRequest join)
    {
        try
        {
            Busy = true;
            Status = $"Launching {acc.DisplayLabel}…";
            var result = await _svc.Launcher.LaunchAsync(acc, join);
            var inst = _svc.Instances.Register(acc, join, result.Process, result.BrowserTrackerId);
            OnUi(() => Instances.Add(new InstanceItemViewModel(inst)));
            Status = $"Launched {acc.DisplayLabel}.";
        }
        catch (Exception ex)
        {
            Status = "Launch failed: " + ex.Message;
            MessageBox.Show(ex.Message, "Launch failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { Busy = false; }
    }

    [RelayCommand]
    private void LeaveInstance(InstanceItemViewModel? item)
    {
        if (item is null) return;
        _svc.Instances.Terminate(item.Model);
        Instances.Remove(item);
        Status = $"Closed {item.AccountLabel}.";
    }

    [RelayCommand]
    private void TerminateAll()
    {
        _svc.Instances.TerminateAll();
        Instances.Clear();
        Status = "Closed all instances.";
    }

    // --- account commands -----------------------------------

    [RelayCommand]
    private void AddAccount()
    {
        var win = new AddAccountWindow(_svc) { Owner = Application.Current.MainWindow };
        if (win.ShowDialog() == true) { RebuildCategories(); ReloadAccounts(); Status = "Account added."; }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void RemoveAccount()
    {
        var vm = SelectedAccount!;
        if (MessageBox.Show($"Remove {vm.Label}? The stored cookie will be deleted.", "MultiRoblox",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _svc.Pool.Invalidate(vm.Id);
        _svc.Accounts.Remove(vm.Id);
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task RefreshAccountAsync()
    {
        var vm = SelectedAccount!;
        Status = $"Checking {vm.Label}…";
        var health = await _svc.KeepAlive.RefreshAsync(vm.Model);
        vm.Health = health;
        vm.Refresh();
        Status = health == AccountHealth.Valid ? "Session OK." : "Session needs a re-login.";
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void OpenUtilities()
    {
        var win = new UtilitiesWindow(_svc, SelectedAccount!.Model, this) { Owner = Application.Current.MainWindow };
        win.Show();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void OpenInBrowser()
    {
        var win = new BrowserWindow(SelectedAccount!.Model) { Owner = Application.Current.MainWindow };
        win.Show();
    }

    // --- reorder (drag) ------------------------------------

    public void PersistOrder() => _svc.Accounts.Reorder(Accounts.Select(a => a.Id).ToList());

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void MoveUp() => Move(-1);

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void MoveDown() => Move(1);

    private void Move(int delta)
    {
        var vm = SelectedAccount!;
        int i = Accounts.IndexOf(vm);
        int j = i + delta;
        if (j < 0 || j >= Accounts.Count) return;
        Accounts.Move(i, j);
        PersistOrder();
    }

    // --- theme -------------------------------------------

    private static readonly Brush LightBg = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
    private static readonly Brush LightFg = new SolidColorBrush(Color.FromRgb(0x14, 0x16, 0x1A));
    private static readonly Brush DarkBg = new SolidColorBrush(Color.FromRgb(0x1E, 0x1F, 0x22));
    private static readonly Brush DarkFg = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));

    private void UpdateThemeToggle()
    {
        bool currentlyLight = _svc.Settings.Current.ThemeName == "Light";
        ThemeToggleText = currentlyLight ? "Dark theme" : "Light theme";
        ThemeToggleBackground = currentlyLight ? DarkBg : LightBg;
        ThemeToggleForeground = currentlyLight ? DarkFg : LightFg;
    }

    [RelayCommand]
    private void ToggleTheme()
    {
        var s = _svc.Settings.Current;
        s.ThemeName = s.ThemeName == "Light" ? "Dark" : "Light";
        _svc.Settings.Save();
        ThemeManager.Apply(s.ThemeName);
        TitleBarTheme.ApplyToOpenWindows();
        UpdateThemeToggle();
    }

    [RelayCommand]
    private void OpenSettings()
    {
        var win = new SettingsWindow(_svc) { Owner = Application.Current.MainWindow };
        win.ShowDialog();
        ThemeManager.Apply(_svc.Settings.Current.ThemeName);
        UpdateThemeToggle();
    }

    // --- update -----------------------------------------

    public enum UpdateStatus { Checking, UpToDate, Available, Unknown }

    [RelayCommand]
    private async Task CheckForUpdateAsync()
    {
        UpdatePopupOpen = !UpdatePopupOpen;
        if (UpdatePopupOpen) { RenderUpdate(_cachedUpdate); await CheckForUpdateSilentlyAsync(); }
    }

    [RelayCommand]
    private void DismissUpdate() => UpdatePopupOpen = false;

    [RelayCommand]
    private async Task InstallUpdateAsync()
    {
        if (_cachedUpdate is null || !_cachedUpdate.UpdateAvailable) return;
        try
        {
            UpdateInstalling = true;
            UpdateHeadline = "Downloading…";
            var progress = new Progress<double>(p => OnUi(() =>
            {
                UpdateProgress = p;
                UpdateHeadline = p >= 1 ? "Installing…" : $"Downloading… {p:P0}";
            }));
            await new UpdateService().DownloadAndApplyAsync(_cachedUpdate, progress);
            UpdateHeadline = "Restarting…";
            await Task.Delay(400);
            ((App)Application.Current).ShutdownApp();
        }
        catch (Exception ex)
        {
            UpdateInstalling = false;
            UpdateHeadline = "Update failed: " + ex.Message;
        }
    }

    private async Task CheckForUpdateSilentlyAsync()
    {
        try
        {
            var info = await new UpdateService().CheckAsync();
            _cachedUpdate = info;
            OnUi(() =>
            {
                if (info.UpdateAvailable)
                {
                    UpdateState = UpdateStatus.Available;
                    UpdateButtonText = $"Update to {info.LatestTag}";
                    UpdateTooltip = $"Update available: {info.LatestTag} (installed v{info.Current.ToString(3)})";
                }
                else
                {
                    UpdateState = UpdateStatus.UpToDate;
                    UpdateButtonText = "Check for update";
                    UpdateTooltip = $"Up to date (v{info.Current.ToString(3)})";
                }
                if (UpdatePopupOpen) RenderUpdate(info);
            });
        }
        catch
        {
            OnUi(() =>
            {
                UpdateState = UpdateStatus.Unknown;
                UpdateTooltip = "Couldn't check for updates";
                if (UpdatePopupOpen) { UpdateHeadline = "Couldn't check for updates"; UpdateCanInstall = false; }
            });
        }
    }

    private void RenderUpdate(UpdateInfo? info)
    {
        if (info is null) { UpdateHeadline = "Checking…"; UpdateInstalledText = $"v{UpdateService.CurrentVersion.ToString(3)}"; UpdateLatestText = "…"; return; }
        UpdateInstalledText = $"v{info.Current.ToString(3)}";
        UpdateLatestText = info.LatestTag;
        UpdateHasNotes = !string.IsNullOrWhiteSpace(info.Notes);
        UpdateNotes = info.Notes.Trim();
        UpdateCanInstall = info.UpdateAvailable;
        UpdateHeadline = info.UpdateAvailable ? "Update available"
            : info.Latest > info.Current ? "Update available (no exe yet — try shortly)"
            : "You're up to date";
    }

    // --- helpers --------------------------------------

    private void SyncInstance(RobloxInstance inst)
    {
        var vm = Instances.FirstOrDefault(x => x.Model.Id == inst.Id);
        if (vm is null) return;
        vm.Sync();
        var acc = Accounts.FirstOrDefault(a => a.Id == inst.AccountId);
        if (acc is not null)
            acc.IsInGame = Instances.Any(x => x.Model.AccountId == inst.AccountId
                                              && x.Model.State is InstanceState.Running or InstanceState.Launching);

        if (inst.State is InstanceState.Disconnected && _svc.Settings.Current.AutoRelaunchOnDisconnect)
        {
            var account = _svc.Accounts.FindById(inst.AccountId);
            if (account is not null)
                _ = LaunchAsync(account, inst.JobId is { Length: > 0 }
                    ? JoinRequest.Server(inst.PlaceId, inst.JobId)
                    : JoinRequest.Place(inst.PlaceId));
            Instances.Remove(vm);
        }
    }

    private static void OnUi(Action a) => Application.Current.Dispatcher.Invoke(a);
}

public sealed record PlayerFindResult(long UserId, string Display, long? PlaceId, string? GameId, bool Joinable);
