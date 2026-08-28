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

    /// <summary>All rows currently highlighted in the sidebar (Ctrl / Shift multi-select).</summary>
    public IReadOnlyList<AccountItemViewModel> SelectedAccounts { get; private set; } = Array.Empty<AccountItemViewModel>();

    public string SelectionSummary =>
        SelectedAccounts.Count > 1 ? string.Join(", ", SelectedAccounts.Select(a => a.Label))
        : SelectedAccount?.Label ?? "Select an account";

    public void SetSelectedAccounts(IEnumerable<AccountItemViewModel> items)
    {
        SelectedAccounts = items.ToList();
        if (SelectedAccounts.Count == 1) SelectedAccount = SelectedAccounts[0];
        else if (SelectedAccounts.Count > 1 && (SelectedAccount is null || !SelectedAccounts.Contains(SelectedAccount)))
            SelectedAccount = SelectedAccounts[^1];
        OnPropertyChanged(nameof(SelectionSummary));
        JoinCommand.NotifyCanExecuteChanged();
        AddFavoriteCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Accounts an action should target: every selected row, or the single primary one.</summary>
    private IReadOnlyList<Account> TargetAccounts() =>
        SelectedAccounts.Count > 0
            ? SelectedAccounts.Select(a => a.Model).ToList()
            : SelectedAccount is { } s ? new List<Account> { s.Model } : new List<Account>();
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private string _status = "Ready";
    [ObservableProperty] private bool _busy;

    // --- join panel -------------------------------------------------
    public enum JoinMode { Manual, Favorites, Recents, PlayerFinder }

    [ObservableProperty] private JoinMode _joinModeValue = JoinMode.Manual;
    [ObservableProperty] private string _placeIdInput = "";
    [ObservableProperty] private string _jobIdInput = "";
    [ObservableProperty] private string _gameName = "";
    private CancellationTokenSource? _gameNameCts;
    private readonly Dictionary<long, string> _gameNameCache = new();

    public ObservableCollection<FavoriteGame> Favorites { get; } = new();
    [ObservableProperty] private FavoriteGame? _selectedFavorite;

    public ObservableCollection<RecentGame> Recents { get; } = new();
    [ObservableProperty] private RecentGame? _selectedRecent;

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
        ReloadRecents();
        ReloadAccounts();

        _svc.KeepAlive.HealthChanged += (_, e) => OnUi(() =>
        {
            var vm = Accounts.FirstOrDefault(a => a.Id == e.AccountId);
            if (vm is not null) vm.Health = e.Health;
        });
        // non-blocking: this event fires from the WinEvent-hook / poll threads
        _svc.Instances.InstanceChanged += (_, inst) =>
            Application.Current?.Dispatcher.BeginInvoke(() => SyncInstance(inst));
        _svc.Accounts.Changed += (_, _) => OnUi(() => { RebuildCategories(); ReloadAccounts(); });

        if (Enum.TryParse<JoinMode>(_svc.Settings.Current.JoinMode, out var savedMode))
            JoinModeValue = savedMode;
        _joinModeLoaded = true;

        UpdateThemeToggle();
        _ = CheckForUpdateSilentlyAsync();
    }

    private bool _joinModeLoaded;

    partial void OnJoinModeValueChanged(JoinMode value)
    {
        if (!_joinModeLoaded) return;
        _svc.Settings.Current.JoinMode = value.ToString();
        _svc.Settings.Save();
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
        bool clear = category == AllCategories;
        item.Model.Group = clear ? "" : category;
        _svc.Accounts.Update(item.Model);
        Status = clear ? $"Removed {item.Label} from its category." : $"Moved {item.Label} to {category}.";
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
        OnPropertyChanged(nameof(SelectionSummary));
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
        SelectedAccount is not null && !Busy && !_batchLaunching && GameLinkParser.TryParse(PlaceIdInput, out _);

    partial void OnPlaceIdInputChanged(string value)
    {
        JoinCommand.NotifyCanExecuteChanged();
        AddFavoriteCommand.NotifyCanExecuteChanged();
        _ = ResolveGameNameAsync(value);
    }

    /// <summary>Debounced lookup of the game name for whatever's in the Place ID box.</summary>
    private async Task ResolveGameNameAsync(string input)
    {
        _gameNameCts?.Cancel();
        if (!GameLinkParser.TryParse(input, out var link)) { GameName = ""; return; }

        if (_gameNameCache.TryGetValue(link.PlaceId, out var cached)) { GameName = cached; return; }

        var cts = _gameNameCts = new CancellationTokenSource();
        try
        {
            await Task.Delay(400, cts.Token);
            var acc = SelectedAccount?.Model;
            string? name = acc is null
                ? null
                : await new GamesClient(_svc.Pool.Get(acc)).GetPlaceNameAsync(link.PlaceId, cts.Token);
            if (cts.Token.IsCancellationRequested) return;
            name = string.IsNullOrWhiteSpace(name) ? "" : name!;
            if (name.Length > 0) _gameNameCache[link.PlaceId] = name;
            GameName = name;
        }
        catch (OperationCanceledException) { }
        catch { GameName = ""; }
    }

    [RelayCommand(CanExecute = nameof(CanJoin))]
    private async Task JoinAsync()
    {
        if (!GameLinkParser.TryParse(PlaceIdInput, out var link)) { Status = "Couldn't find a place id in that."; return; }

        JoinRequest join = link.JobId is null && link.PrivateServerLinkCode is null && link.AccessCode is null
                           && !string.IsNullOrWhiteSpace(JobIdInput)
            ? JoinRequest.Server(link.PlaceId, JobIdInput.Trim())
            : link.ToJoinRequest();

        PlaceIdInput = link.PlaceId.ToString();
        await LaunchManyAsync(TargetAccounts(), join, savePlace: true);
    }

    private bool _batchLaunching;

    /// <summary>Launch one or several accounts into the same request, staggered a little.</summary>
    private async Task LaunchManyAsync(IReadOnlyList<Account> accounts, JoinRequest join, bool savePlace = false)
    {
        if (accounts.Count == 0 || _batchLaunching) return;
        _batchLaunching = true;
        JoinCommand.NotifyCanExecuteChanged();
        try
        {
            for (int i = 0; i < accounts.Count; i++)
            {
                var acc = accounts[i];
                if (savePlace)
                {
                    acc.SavedPlaceId = join.PlaceId.ToString();
                    acc.SavedJobId = join.JobId ?? "";
                }
                acc.LastUsed = DateTimeOffset.Now;
                _svc.Accounts.Update(acc);
                await LaunchAsync(acc, join);
                if (i < accounts.Count - 1) await Task.Delay(2000);   // let Roblox settle between launches
            }
            if (accounts.Count > 1) Status = $"Launched {accounts.Count} accounts into place {join.PlaceId}.";
        }
        finally
        {
            _batchLaunching = false;
            JoinCommand.NotifyCanExecuteChanged();
        }
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
        if (game is null || game.PlaceId == 0) return;
        await LaunchManyAsync(TargetAccounts(), JoinRequest.Place(game.PlaceId));
    }

    // --- join: recents (games launched through the app) -------

    private const int RecentsCap = 20;

    private void ReloadRecents()
    {
        Recents.Clear();
        foreach (var r in _svc.Settings.Current.Recents.OrderByDescending(r => r.LastPlayed))
            Recents.Add(r);
    }

    private async Task RecordRecentAsync(long placeId, Account acc)
    {
        if (placeId <= 0) return;
        var list = _svc.Settings.Current.Recents;
        var existing = list.FirstOrDefault(r => r.PlaceId == placeId);
        string name = existing?.Name ?? "";
        if (string.IsNullOrEmpty(name))
        {
            try { name = await new GamesClient(_svc.Pool.Get(acc)).GetPlaceNameAsync(placeId) ?? ""; } catch { }
            if (string.IsNullOrEmpty(name)) name = $"Place {placeId}";
        }
        list.RemoveAll(r => r.PlaceId == placeId);
        list.Insert(0, new RecentGame { PlaceId = placeId, Name = name, LastPlayed = DateTimeOffset.Now });
        if (list.Count > RecentsCap) list.RemoveRange(RecentsCap, list.Count - RecentsCap);
        _svc.Settings.Save();
        OnUi(ReloadRecents);
    }

    [RelayCommand]
    private async Task JoinRecentAsync(RecentGame? game)
    {
        game ??= SelectedRecent;
        if (game is null || game.PlaceId == 0) return;
        await LaunchManyAsync(TargetAccounts(), JoinRequest.Place(game.PlaceId));
    }

    [RelayCommand]
    private void ClearRecents()
    {
        _svc.Settings.Current.Recents.Clear();
        _svc.Settings.Save();
        ReloadRecents();
        Status = "Cleared recent games.";
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
        if (r is null || !r.Joinable || r.PlaceId is not { } place || r.GameId is not { } job) return;
        await LaunchManyAsync(TargetAccounts(), JoinRequest.Server(place, job));
    }

    // --- launching & instances -------------------------------

    public async Task LaunchAsync(Account acc, JoinRequest join)
    {
        try
        {
            Busy = true;
            Status = $"Launching {acc.DisplayLabel}…";
            var result = await _svc.Launcher.LaunchAsync(acc, join);
            var inst = _svc.Instances.Register(acc, join, result.Process, result.Group, result.BrowserTrackerId);
            InstanceItemViewModel row = new(inst);
            OnUi(() => Instances.Add(row));
            _ = FillInstanceGameNameAsync(row, acc);
            Status = $"Launched {acc.DisplayLabel}.";
            _ = RecordRecentAsync(join.PlaceId, acc);
        }
        catch (Exception ex)
        {
            Status = "Launch failed: " + ex.Message;
            MessageBox.Show(ex.Message, "Launch failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { Busy = false; }
    }

    /// <summary>Look up the place name for a running-instances row (second line of the "Where" column).</summary>
    private async Task FillInstanceGameNameAsync(InstanceItemViewModel row, Account acc)
    {
        long placeId = row.PlaceId;
        if (_gameNameCache.TryGetValue(placeId, out var cached)) { OnUi(() => row.GameName = cached); return; }
        try
        {
            var name = await new GamesClient(_svc.Pool.Get(acc)).GetPlaceNameAsync(placeId);
            if (!string.IsNullOrWhiteSpace(name))
            {
                _gameNameCache[placeId] = name!;
                OnUi(() => row.GameName = name!);
            }
        }
        catch { /* name is a nicety; leave the id-only line */ }
    }

    [RelayCommand]
    private void LeaveInstance(InstanceItemViewModel? item)
    {
        if (item is null) return;
        Instances.Remove(item);                       // drop the row now, don't wait on the kill
        var label = item.AccountLabel;
        try { _svc.Instances.Terminate(item.Model); } catch (Exception ex) { Status = "Close failed: " + ex.Message; return; }
        Status = $"Closed {label}.";
    }

    [RelayCommand]
    private void TerminateAll()
    {
        if (Instances.Count == 0) return;
        if (MessageBox.Show("Are you sure you want to close all instances?", "MultiRoblox",
                MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
            return;

        Instances.Clear();
        try { _svc.Instances.TerminateAll(); } catch (Exception ex) { Status = "Close all failed: " + ex.Message; return; }
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
                    UpdateButtonText = "Update available";
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
        UpdateCanInstall = info.UpdateAvailable;
        UpdateHeadline = info.UpdateAvailable ? "Update available"
            : info.Latest > info.Current ? "Update available (no exe yet — try shortly)"
            : "You're up to date";
    }

    // --- helpers --------------------------------------

    private void SyncInstance(RobloxInstance inst)
    {
        var vm = Instances.FirstOrDefault(x => x.Model.Id == inst.Id);

        // Client gone (closed via X / Alt+F4, exited, crashed, or we terminated it) — drop the row.
        if (inst.State is InstanceState.Closed or InstanceState.Terminated)
        {
            if (vm is not null) Instances.Remove(vm);
            if (inst.State is InstanceState.Closed) Status = $"{inst.AccountLabel} closed.";
        }
        else vm?.Sync();

        var acc = Accounts.FirstOrDefault(a => a.Id == inst.AccountId);
        if (acc is not null)
            acc.IsInGame = Instances.Any(x => x.Model.AccountId == inst.AccountId);
    }

    private static void OnUi(Action a) => Application.Current.Dispatcher.Invoke(a);
}

public sealed record PlayerFindResult(long UserId, string Display, long? PlaceId, string? GameId, bool Joinable);
