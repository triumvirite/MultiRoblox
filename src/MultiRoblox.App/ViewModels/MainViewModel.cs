using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
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

    /// <summary>Full account list for the grid view, grouped by category (no sidebar filter).</summary>
    public ICollectionView AccountGrid { get; }

    [ObservableProperty] private AccountItemViewModel? _selectedAccount;
    [ObservableProperty] private string _selectedCategory = AllCategories;

    /// <summary>Main pane shows the account grid instead of the join panel + running instances.</summary>
    [ObservableProperty] private bool _showAccountGrid;

    public string AccountViewToggleText => ShowAccountGrid ? "Toggle Instance View" : "Toggle Account View";

    partial void OnShowAccountGridChanged(bool value) => OnPropertyChanged(nameof(AccountViewToggleText));

    [RelayCommand]
    private void ToggleAccountView() => ShowAccountGrid = !ShowAccountGrid;

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
        NotifyQuickJoinState();
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

        AccountGrid = new CollectionViewSource { Source = Accounts }.View;

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
        // Quick Join button (grayed / "Already in this game!") depends on which instances are running
        Instances.CollectionChanged += (_, _) => NotifyQuickJoinState();
        _svc.Accounts.Changed += (_, _) => OnUi(() => { RebuildCategories(); ReloadAccounts(); });

        if (Enum.TryParse<JoinMode>(_svc.Settings.Current.JoinMode, out var savedMode))
            JoinModeValue = savedMode;
        _joinModeLoaded = true;

        QuickJoinPlaceId = _svc.Settings.Current.QuickJoinPlaceId;
        QuickJoinName = _svc.Settings.Current.QuickJoinName;

        UpdateThemeToggle();
        _ = CheckForUpdateSilentlyAsync();

        _updateTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(15) };
        _updateTimer.Tick += (_, _) => _ = CheckForUpdateSilentlyAsync();
        _updateTimer.Start();
    }

    private readonly DispatcherTimer _updateTimer;

    private bool _joinModeLoaded;

    partial void OnJoinModeValueChanged(JoinMode value)
    {
        if (!_joinModeLoaded) return;
        _svc.Settings.Current.JoinMode = value.ToString();
        _svc.Settings.Save();
    }

    // --- categories ----------------------------------------------

    public const string NewCategoryItem = "Add new category…";

    /// <summary>The sentinel strings that must never be treated as a real, user-created category.</summary>
    private static bool IsReservedCategory(string c) =>
        string.IsNullOrWhiteSpace(c)
        || c.Equals(AllCategories, StringComparison.OrdinalIgnoreCase)
        || c.Equals(NewCategoryItem, StringComparison.OrdinalIgnoreCase);

    public void RebuildCategories()
    {
        // One-time cleanup: earlier builds let the "Add new category…" action row be clicked as if it
        // were a real category, which persisted the sentinel string onto accounts / settings. Strip it.
        bool cleaned = _svc.Settings.Current.Categories.RemoveAll(IsReservedCategory) > 0;
        foreach (var a in _svc.Accounts.Accounts)
            if (a.Categories.RemoveAll(IsReservedCategory) > 0) { _svc.Accounts.Update(a); cleaned = true; }
        if (cleaned) { _svc.Settings.Save(); Serilog.Log.Information("RebuildCategories: stripped reserved-name categories from stored data"); }

        var wanted = new List<string> { AllCategories };
        wanted.AddRange(_svc.Settings.Current.Categories);
        wanted.AddRange(_svc.Accounts.Accounts.SelectMany(a => a.Categories));
        var distinct = wanted.Where(c => !string.IsNullOrWhiteSpace(c) && !c.Equals(NewCategoryItem, StringComparison.OrdinalIgnoreCase))
                             .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        _rebuildingCategories = true;
        Categories.Clear();
        foreach (var c in distinct) Categories.Add(c);
        Categories.Add(NewCategoryItem);   // action row, pinned to the bottom
        if (!Categories.Contains(SelectedCategory) || SelectedCategory == NewCategoryItem)
            SelectedCategory = AllCategories;
        _rebuildingCategories = false;

        // keep the per-row "Categories" popups in sync with the real category list
        var real = _svc.Settings.Current.Categories
            .Concat(_svc.Accounts.Accounts.SelectMany(a => a.Categories))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var row in Accounts) row.SetCategoryUniverse(real);
    }

    private bool _rebuildingCategories;

    /// <summary>Prompt for a new category name and register it. Returns the name, or null if cancelled.</summary>
    public string? PromptNewCategory()
    {
        var name = Dialogs.Prompt("New category", "Name for the new category:");
        if (string.IsNullOrWhiteSpace(name) || IsReservedCategory(name.Trim())) return null;
        name = name.Trim();
        if (!_svc.Settings.Current.Categories.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            _svc.Settings.Current.Categories.Add(name);
            _svc.Settings.Save();
        }
        RebuildCategories();
        return name;
    }

    /// <summary>Create a category (via prompt) and move the given accounts into it.</summary>
    public void NewCategoryAndAssign(IEnumerable<AccountItemViewModel> items)
    {
        if (PromptNewCategory() is { } name)
            foreach (var it in items) AssignCategory(it, name);
    }

    /// <summary>"Add new category…" from a grid row's Categories popup.</summary>
    [RelayCommand]
    private void AddCategoryToRow(AccountItemViewModel? row)
    {
        if (row is null) return;
        if (PromptNewCategory() is { } name) row.SetCategoryMembership(name, true);
    }


    /// <summary>Add <paramref name="item"/> to <paramref name="category"/> if it isn't already in it.</summary>
    public void AssignCategory(AccountItemViewModel item, string category)
    {
        if (IsReservedCategory(category)) return;
        if (!item.Model.Categories.Any(c => c.Equals(category, StringComparison.OrdinalIgnoreCase)))
        {
            item.Model.Categories.Add(category);
            _svc.Accounts.Update(item.Model);
            item.Refresh();
        }
        Status = $"Added *{item.Label}* to *{category}*.";
    }

    public void UnassignCategory(AccountItemViewModel item, string category)
    {
        if (item.Model.Categories.RemoveAll(c => c.Equals(category, StringComparison.OrdinalIgnoreCase)) > 0)
        {
            _svc.Accounts.Update(item.Model);
            item.Refresh();
            Status = $"Removed *{item.Label}* from *{category}*.";
        }
    }

    /// <summary>Toggle membership — used by the checkable "Add to category" submenu.</summary>
    public void ToggleCategory(AccountItemViewModel item, string category)
    {
        if (item.InCategory(category)) UnassignCategory(item, category);
        else AssignCategory(item, category);
    }

    public void ClearCategories(AccountItemViewModel item)
    {
        if (item.Model.Categories.Count == 0) return;
        item.Model.Categories.Clear();
        _svc.Accounts.Update(item.Model);
        item.Refresh();
        Status = $"Removed *{item.Label}* from all categories.";
    }

    /// <summary>How many accounts currently sit in <paramref name="category"/>.</summary>
    public int CountInCategory(string category) =>
        _svc.Accounts.Accounts.Count(a => a.Categories.Any(c => c.Equals(category, StringComparison.OrdinalIgnoreCase)));

    /// <summary>Delete a category outright: drop it from the settings list and from every account.</summary>
    public void RemoveCategoryEverywhere(string category)
    {
        if (IsReservedCategory(category)) return;

        int touched = 0;
        foreach (var a in _svc.Accounts.Accounts)
            if (a.Categories.RemoveAll(c => c.Equals(category, StringComparison.OrdinalIgnoreCase)) > 0)
            {
                _svc.Accounts.Update(a);
                touched++;
            }

        _svc.Settings.Current.Categories.RemoveAll(c => c.Equals(category, StringComparison.OrdinalIgnoreCase));
        _svc.Settings.Save();

        if (SelectedCategory.Equals(category, StringComparison.OrdinalIgnoreCase))
            SelectedCategory = AllCategories;

        RebuildCategories();
        ReloadAccounts();
        Status = touched > 0
            ? $"Removed category *{category}* from {touched} account(s)."
            : $"Removed category *{category}*.";
    }

    private string _lastRealCategory = AllCategories;

    partial void OnSelectedCategoryChanged(string value)
    {
        if (_rebuildingCategories) return;

        if (value == NewCategoryItem)
        {
            // "Add new category…" is an action, not a filter. Bounce off it on the next dispatcher
            // tick (so the ComboBox finishes its own selection transaction), prompt, then land on
            // the created category — or fall back to the previous real selection if cancelled.
            var previous = _lastRealCategory;
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                SelectedCategory = previous;
                if (PromptNewCategory() is { } created) SelectedCategory = created;
            }), System.Windows.Threading.DispatcherPriority.Input);
            return;
        }

        _lastRealCategory = value;
        AccountsView.Refresh();
    }

    // --- account list -------------------------------------------

    private void ReloadAccounts()
    {
        var selectedId = SelectedAccount?.Id;
        Accounts.Clear();
        foreach (var acc in _svc.Accounts.Accounts)
        {
            // Alias/Note edits mutate the shared Account object in place, so a plain Save persists
            // them — no Changed event (which would rebuild every row mid-edit).
            var row = new AccountItemViewModel(acc)
            {
                Health = _svc.KeepAlive.GetHealth(acc.Id),
                Persist = () => _svc.Accounts.Save(),
            };
            row.SetCategoryUniverse(_svc.Settings.Current.Categories
                .Concat(_svc.Accounts.Accounts.SelectMany(a => a.Categories))
                .Distinct(StringComparer.OrdinalIgnoreCase));
            // carry over "in-game" state — ReloadAccounts rebuilds every row from scratch (e.g. after
            // a drag-reorder) and would otherwise drop it back to "Signed in".
            var inst = Instances.FirstOrDefault(x => x.Model.AccountId == acc.Id);
            if (inst is not null)
            {
                row.IsInGame = true;
                row.InGameName = !string.IsNullOrWhiteSpace(inst.GameName) ? inst.GameName : inst.PlaceLine;
            }
            Accounts.Add(row);
        }
        AccountsView.Refresh();
        if (selectedId is not null)
            SelectedAccount = Accounts.FirstOrDefault(a => a.Id == selectedId);
    }

    private bool FilterAccount(object o)
    {
        if (o is not AccountItemViewModel vm) return false;
        if (SelectedCategory != AllCategories && !vm.InCategory(SelectedCategory))
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
        SetManualQuickJoinCommand.NotifyCanExecuteChanged();
        NotifyQuickJoinState();
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
        SetManualQuickJoinCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(ManualQuickJoinText));
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
            if (accounts.Count > 1) Status = $"Launched *{accounts.Count} accounts* into place {join.PlaceId}.";
        }
        finally
        {
            _batchLaunching = false;
            JoinCommand.NotifyCanExecuteChanged();
            if (HasQuickJoin && join.PlaceId != QuickJoinPlaceId)
                StartQuickJoinCooldown();
            else
                NotifyQuickJoinState();
        }
    }

    // brief gray-out on the Quick Join button right after launching some other game
    private DateTime _quickJoinCooldownUntil;

    private void StartQuickJoinCooldown()
    {
        _quickJoinCooldownUntil = DateTime.UtcNow.AddSeconds(5);
        NotifyQuickJoinState();
        _ = Task.Delay(5050).ContinueWith(_ => OnUi(NotifyQuickJoinState));
    }

    // --- quick join (one designated game, persisted) ----------

    [ObservableProperty] private long _quickJoinPlaceId;
    [ObservableProperty] private string _quickJoinName = "";

    public bool HasQuickJoin => QuickJoinPlaceId != 0;

    /// <summary>The game name (or "Place {id}"), or "None" — shown after "Quick Join: " on the button.</summary>
    public string QuickJoinValueText => QuickJoinPlaceId == 0
        ? "None"
        : string.IsNullOrWhiteSpace(QuickJoinName) ? $"Place {QuickJoinPlaceId}" : QuickJoinName;

    partial void OnQuickJoinPlaceIdChanged(long value)
    {
        OnPropertyChanged(nameof(QuickJoinValueText));
        OnPropertyChanged(nameof(HasQuickJoin));
        NotifyQuickJoinButtonTexts();
        NotifyQuickJoinState();
        ClearQuickJoinCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(HasQuickJoin))]
    private void ClearQuickJoin() => SetQuickJoin(0, "");

    // --- "Set / Remove as Quick Join" button text (toggles when the target is already the Quick Join)

    private bool IsQuickJoin(long placeId) => placeId != 0 && placeId == QuickJoinPlaceId;

    private long ManualPlaceId => GameLinkParser.TryParse(PlaceIdInput, out var l) ? l.PlaceId : 0;

    public string ManualQuickJoinText => IsQuickJoin(ManualPlaceId) ? "Remove as Quick Join" : "Set as Quick Join";
    public string FavoriteQuickJoinText => IsQuickJoin(SelectedFavorite?.PlaceId ?? 0) ? "Remove as Quick Join" : "Set as Quick Join";
    public string RecentQuickJoinText => IsQuickJoin(SelectedRecent?.PlaceId ?? 0) ? "Remove as Quick Join" : "Set as Quick Join";

    private void NotifyQuickJoinButtonTexts()
    {
        OnPropertyChanged(nameof(ManualQuickJoinText));
        OnPropertyChanged(nameof(FavoriteQuickJoinText));
        OnPropertyChanged(nameof(RecentQuickJoinText));
    }

    partial void OnSelectedFavoriteChanged(FavoriteGame? value) => OnPropertyChanged(nameof(FavoriteQuickJoinText));
    partial void OnSelectedRecentChanged(RecentGame? value) => OnPropertyChanged(nameof(RecentQuickJoinText));

    partial void OnQuickJoinNameChanged(string value) => OnPropertyChanged(nameof(QuickJoinValueText));

    private void SetQuickJoin(long placeId, string name)
    {
        QuickJoinPlaceId = placeId;
        QuickJoinName = name ?? "";
        _svc.Settings.Current.QuickJoinPlaceId = placeId;
        _svc.Settings.Current.QuickJoinName = QuickJoinName;
        _svc.Settings.Save();
        Status = placeId == 0 ? "Quick Join cleared." : $"Quick Join set to *{(string.IsNullOrWhiteSpace(QuickJoinName) ? $"Place {placeId}" : QuickJoinName)}*.";
    }

    /// <summary>True when every account we'd Quick-Join is already in that game (nothing to launch).</summary>
    public bool AllTargetsInQuickJoinGame =>
        HasQuickJoin && TargetAccounts() is { Count: > 0 } t && t.All(a => IsAccountInPlace(a.Id, QuickJoinPlaceId));

    public string QuickJoinButtonText => AllTargetsInQuickJoinGame ? "Already in this game!" : "Quick Join";

    private bool CanQuickJoin() =>
        HasQuickJoin && SelectedAccount is not null && !Busy && !_batchLaunching
        && !AllTargetsInQuickJoinGame && DateTime.UtcNow >= _quickJoinCooldownUntil;

    private void NotifyQuickJoinState()
    {
        OnPropertyChanged(nameof(AllTargetsInQuickJoinGame));
        OnPropertyChanged(nameof(QuickJoinButtonText));
        QuickJoinCommand.NotifyCanExecuteChanged();
    }

    partial void OnBusyChanged(bool value) => NotifyQuickJoinState();

    [RelayCommand(CanExecute = nameof(CanQuickJoin))]
    private async Task QuickJoinAsync()
    {
        if (QuickJoinPlaceId == 0) return;
        // skip any selected account that's already in the Quick Join game
        var targets = TargetAccounts().Where(a => !IsAccountInPlace(a.Id, QuickJoinPlaceId)).ToList();
        if (targets.Count == 0) { Status = "Already in the Quick Join game."; return; }
        await LaunchManyAsync(targets, JoinRequest.Place(QuickJoinPlaceId));
    }

    /// <summary>Double-click an account row → launch just that account into the Quick Join game.</summary>
    public async Task QuickJoinAccountAsync(AccountItemViewModel account)
    {
        if (QuickJoinPlaceId == 0 || _batchLaunching) return;
        if (IsAccountInPlace(account.Id, QuickJoinPlaceId))
        {
            Status = $"*{account.Label}* is already in *{QuickJoinValueText}*.";
            return;
        }
        await LaunchManyAsync(new[] { account.Model }, JoinRequest.Place(QuickJoinPlaceId));
    }

    [RelayCommand(CanExecute = nameof(CanAddFavorite))]
    private async Task SetManualQuickJoinAsync()
    {
        if (!GameLinkParser.TryParse(PlaceIdInput, out var link)) { Status = "Enter a Place ID first."; return; }
        if (IsQuickJoin(link.PlaceId)) { SetQuickJoin(0, ""); return; }
        string name = GameName;
        if (string.IsNullOrWhiteSpace(name))
            try { name = await new GamesClient(_svc.Pool.Get(SelectedAccount!.Model)).GetPlaceNameAsync(link.PlaceId) ?? ""; }
            catch { }
        SetQuickJoin(link.PlaceId, name);
    }

    [RelayCommand]
    private void SetQuickJoinFromFavorite(FavoriteGame? game)
    {
        game ??= SelectedFavorite;
        if (game is null) return;
        if (IsQuickJoin(game.PlaceId)) SetQuickJoin(0, "");
        else SetQuickJoin(game.PlaceId, game.Name);
    }

    [RelayCommand]
    private void SetQuickJoinFromRecent(RecentGame? game)
    {
        game ??= SelectedRecent;
        if (game is null) return;
        if (IsQuickJoin(game.PlaceId)) { SetQuickJoin(0, ""); return; }
        SetQuickJoin(game.PlaceId, game.Name);
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
        Status = $"Favorited *{fav.Name}*.";
    }

    [RelayCommand]
    private void RemoveFavorite(FavoriteGame? game)
    {
        game ??= SelectedFavorite;
        if (game is null) return;
        _svc.Settings.Current.Favorites.RemoveAll(f => f.PlaceId == game.PlaceId);
        _svc.Settings.Save();
        ReloadFavorites();
        Status = $"Removed *{game.Name}* from favorites.";
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

    /// <summary>Is this account currently running an instance in the given place?</summary>
    private bool IsAccountInPlace(Guid accountId, long placeId) =>
        placeId != 0 && Instances.Any(x => x.Model.AccountId == accountId && x.Model.PlaceId == placeId);

    public async Task LaunchAsync(Account acc, JoinRequest join)
    {
        try
        {
            Busy = true;

            // One client per account: tear down any instance this account already has, and clear its
            // row, before launching the new one.
            var stale = Instances.FirstOrDefault(x => x.Model.AccountId == acc.Id);
            if (stale is not null)
            {
                Status = $"Closing *{acc.DisplayLabel}*'s current game…";
                OnUi(() => Instances.Remove(stale));
                try { _svc.Instances.Terminate(stale.Model); } catch { }
                await Task.Delay(1500);   // let the kill land + the singleton free before relaunch
            }

            Status = $"Launching *{acc.DisplayLabel}*…";
            var result = await _svc.Launcher.LaunchAsync(acc, join);
            var inst = _svc.Instances.Register(acc, join, result.Process, result.Group, result.BrowserTrackerId);
            InstanceItemViewModel row = new(inst);
            OnUi(() => Instances.Add(row));
            _ = FillInstanceGameNameAsync(row, acc);
            Status = $"Launched *{acc.DisplayLabel}*.";
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
                OnUi(() =>
                {
                    row.GameName = name!;
                    var av = Accounts.FirstOrDefault(a => a.Id == acc.Id);
                    if (av is not null && av.IsInGame) av.InGameName = name!;
                });
            }
        }
        catch { /* name is a nicety; leave the id-only line */ }
    }

    /// <summary>Right-click "Leave current instance" — close whatever this account is currently running.</summary>
    public void LeaveInstanceForAccount(AccountItemViewModel account)
    {
        var rows = Instances.Where(x => x.Model.AccountId == account.Id).ToList();
        if (rows.Count == 0) { Status = $"*{account.Label}* has no running instance."; return; }
        foreach (var r in rows)
        {
            Instances.Remove(r);
            try { _svc.Instances.Terminate(r.Model); }
            catch (Exception ex) { Status = "Close failed: " + ex.Message; return; }
        }
        Status = $"Closed *{account.Label}*.";
    }

    [RelayCommand]
    private void LeaveInstance(InstanceItemViewModel? item)
    {
        if (item is null) return;
        Instances.Remove(item);                       // drop the row now, don't wait on the kill
        var label = item.AccountLabel;
        try { _svc.Instances.Terminate(item.Model); } catch (Exception ex) { Status = "Close failed: " + ex.Message; return; }
        Status = $"Closed *{label}*.";
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

    /// <summary>Import accounts from ic3w0lf22's Roblox Account Manager (its AccountData.json).</summary>
    [RelayCommand]
    private async Task ImportAccountsAsync()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select Roblox Account Manager's AccountData.json",
            Filter = "RAM account data (AccountData.json)|AccountData.json|JSON files (*.json)|*.json|All files (*.*)|*.*",
            CheckFileExists = true,
        };
        if (dlg.ShowDialog() != true) return;

        var (status, ramAccounts) = RamImporter.Read(dlg.FileName);
        switch (status)
        {
            case RamImporter.Result.PasswordProtected:
                MessageBox.Show(
                    "That file is password-protected. Open RAM, remove the encryption password " +
                    "(Settings → Account Encryption → Default), then import again.",
                    "Import from RAM", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            case RamImporter.Result.FileMissing:
            case RamImporter.Result.Unreadable:
                MessageBox.Show("Couldn't read that file as a Roblox Account Manager account list.",
                    "Import from RAM", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            case RamImporter.Result.Empty:
                Status = "No accounts found in that file.";
                return;
        }

        var existing = _svc.Accounts.Accounts;
        var toAdd = new List<Account>();
        int skipped = 0;
        foreach (var r in ramAccounts)
        {
            if (existing.Any(a => a.UserId == r.UserID ||
                    string.Equals(a.SecurityToken, r.SecurityToken, StringComparison.Ordinal)) ||
                toAdd.Any(a => a.UserId == r.UserID))
            {
                skipped++;
                continue;
            }
            var acc = new Account
            {
                Username = r.Username,
                DisplayName = r.Username,
                UserId = r.UserID,
                SecurityToken = r.SecurityToken,
                Alias = (r.Alias ?? "").Trim(),
                Note = BuildImportedNote(r),
            };
            if (!string.IsNullOrWhiteSpace(r.Group) && !r.Group.Trim().Equals("Default", StringComparison.OrdinalIgnoreCase))
                acc.Categories.Add(r.Group.Trim());
            if (long.TryParse(r.BrowserTrackerID, out var btid) && btid > 0)
                acc.BrowserTrackerId = btid;
            toAdd.Add(acc);
        }

        int added = _svc.Accounts.AddMany(toAdd);
        RebuildCategories();
        ReloadAccounts();
        Status = added == 0
            ? $"Nothing imported — all *{skipped} account(s)* are already here."
            : $"Imported *{added} account(s)*" + (skipped > 0 ? $", skipped {skipped} already present." : ".");

        // check the freshly imported cookies in the background so the health dots settle
        foreach (var acc in toAdd)
            _ = _svc.KeepAlive.RefreshAsync(acc);
        await Task.CompletedTask;
    }

    private static string BuildImportedNote(RamImporter.RamAccount r)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(r.Description)) parts.Add(r.Description.Trim());
        if (r.Fields is { Count: > 0 })
            parts.AddRange(r.Fields.Where(f => !string.IsNullOrWhiteSpace(f.Value))
                                   .Select(f => $"{f.Key}: {f.Value}"));
        return string.Join("\n", parts);
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void RemoveAccount() => RemoveAccounts(new[] { SelectedAccount! });

    /// <summary>Confirm and delete one or more accounts (used by the Remove button and the right-click menu).</summary>
    public void RemoveAccounts(IReadOnlyList<AccountItemViewModel> items)
    {
        if (items.Count == 0) return;
        string what = items.Count == 1 ? items[0].Label : $"{items.Count} accounts";
        if (MessageBox.Show(
                $"Remove {what}? The stored cookie{(items.Count == 1 ? "" : "s")} will be deleted.",
                "MultiRoblox", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
            return;
        foreach (var vm in items)
        {
            _svc.Pool.Invalidate(vm.Id);
            _svc.Accounts.Remove(vm.Id);
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task RefreshAccountAsync()
    {
        var vm = SelectedAccount!;
        Status = $"Checking *{vm.Label}*…";
        var health = await _svc.KeepAlive.RefreshAsync(vm.Model);
        vm.Health = health;
        vm.Refresh();
        Status = health == AccountHealth.Valid ? "Session OK." : "Session needs a re-login.";
    }

    /// <summary>Re-capture a fresh cookie for an existing account (right-click / double-click a signed-out row).</summary>
    public void ReLogin(AccountItemViewModel? item)
    {
        var vm = item ?? SelectedAccount;
        if (vm is null) return;
        var win = new AddAccountWindow(_svc, vm.Model) { Owner = Application.Current.MainWindow };
        if (win.ShowDialog() == true)
        {
            _ = RefreshAccountHealthAsync(vm);
            Status = $"Re-logged in *{vm.Label}*.";
        }
    }

    private async Task RefreshAccountHealthAsync(AccountItemViewModel vm)
    {
        vm.Refresh();
        var health = await _svc.KeepAlive.RefreshAsync(vm.Model);
        vm.Health = health;
        vm.Refresh();
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

    /// <summary>Open the update dropdown and (re)check. The toggle/close is handled in the view so a
    /// click on the button while it's open dismisses it instead of re-opening.</summary>
    [RelayCommand]
    private async Task CheckForUpdateAsync()
    {
        UpdatePopupOpen = true;
        RenderUpdate(_cachedUpdate);
        await CheckForUpdateSilentlyAsync();
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
            if (inst.State is InstanceState.Closed) Status = $"*{inst.AccountLabel}* closed.";
        }
        else vm?.Sync();

        var acc = Accounts.FirstOrDefault(a => a.Id == inst.AccountId);
        if (acc is not null)
        {
            var mine = Instances.FirstOrDefault(x => x.Model.AccountId == inst.AccountId);
            acc.IsInGame = mine is not null;
            acc.InGameName = mine is null ? "" :
                !string.IsNullOrWhiteSpace(mine.GameName) ? mine.GameName : mine.PlaceLine;
        }
    }

    private static void OnUi(Action a) => Application.Current.Dispatcher.Invoke(a);
}

public sealed record PlayerFindResult(long UserId, string Display, long? PlaceId, string? GameId, bool Joinable);
