using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MultiRoblox.App.Services;
using MultiRoblox.App.Views;
using MultiRoblox.Core.Models;
using MultiRoblox.Core.Services;

namespace MultiRoblox.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly AppServices _svc;

    public ObservableCollection<AccountItemViewModel> Accounts { get; } = new();
    public ObservableCollection<InstanceItemViewModel> Instances { get; } = new();
    public ICollectionView AccountsView { get; }

    [ObservableProperty] private AccountItemViewModel? _selectedAccount;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private string _placeIdInput = "";
    [ObservableProperty] private string _jobIdInput = "";
    [ObservableProperty] private string _status = "Ready";
    [ObservableProperty] private bool _busy;

    public MainViewModel(AppServices svc)
    {
        _svc = svc;

        AccountsView = CollectionViewSource.GetDefaultView(Accounts);
        AccountsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(AccountItemViewModel.Group)));
        AccountsView.Filter = FilterAccount;

        ReloadAccounts();

        _svc.KeepAlive.HealthChanged += (_, e) => OnUi(() =>
        {
            var vm = Accounts.FirstOrDefault(a => a.Id == e.AccountId);
            if (vm is not null) vm.Health = e.Health;
        });

        _svc.Instances.InstanceChanged += (_, inst) => OnUi(() => SyncInstance(inst));
        _svc.Accounts.Changed += (_, _) => OnUi(ReloadAccounts);
    }

    // --- account list ------------------------------------------------

    private void ReloadAccounts()
    {
        var selectedId = SelectedAccount?.Id;
        Accounts.Clear();
        foreach (var acc in _svc.Accounts.Accounts)
        {
            var vm = new AccountItemViewModel(acc) { Health = _svc.KeepAlive.GetHealth(acc.Id) };
            Accounts.Add(vm);
        }
        AccountsView.Refresh();
        if (selectedId is not null)
            SelectedAccount = Accounts.FirstOrDefault(a => a.Id == selectedId);
    }

    private bool FilterAccount(object o)
    {
        if (o is not AccountItemViewModel vm) return false;
        if (string.IsNullOrWhiteSpace(SearchText)) return true;
        return vm.Label.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
               || vm.Group.Contains(SearchText, StringComparison.OrdinalIgnoreCase);
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
        RemoveAccountCommand.NotifyCanExecuteChanged();
        RefreshAccountCommand.NotifyCanExecuteChanged();
        OpenServerBrowserCommand.NotifyCanExecuteChanged();
        OpenUtilitiesCommand.NotifyCanExecuteChanged();
        OpenInBrowserCommand.NotifyCanExecuteChanged();
        CopyCookieCommand.NotifyCanExecuteChanged();
    }

    // --- commands ---------------------------------------------------

    [RelayCommand]
    private void AddAccount()
    {
        var win = new AddAccountWindow(_svc) { Owner = Application.Current.MainWindow };
        if (win.ShowDialog() == true)
        {
            ReloadAccounts();
            Status = "Account added.";
        }
    }

    private bool HasSelection() => SelectedAccount is not null;

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

    private bool CanJoin() =>
        SelectedAccount is not null && !Busy && GameLinkParser.TryParse(PlaceIdInput, out _);

    partial void OnPlaceIdInputChanged(string value) => JoinCommand.NotifyCanExecuteChanged();

    [RelayCommand(CanExecute = nameof(CanJoin))]
    private async Task JoinAsync()
    {
        var acc = SelectedAccount!.Model;
        if (!GameLinkParser.TryParse(PlaceIdInput, out var link))
        {
            Status = "Couldn't find a place id in that.";
            return;
        }

        // A pasted link's own server info wins; otherwise use the Job ID box.
        JoinRequest join = link.JobId is null && link.PrivateServerLinkCode is null && link.AccessCode is null
                           && !string.IsNullOrWhiteSpace(JobIdInput)
            ? JoinRequest.Server(link.PlaceId, JobIdInput.Trim())
            : link.ToJoinRequest();

        // Normalise the box to the resolved id so it's clean next time.
        PlaceIdInput = link.PlaceId.ToString();
        acc.SavedPlaceId = link.PlaceId.ToString();
        acc.SavedJobId = join.JobId ?? "";
        acc.LastUsed = DateTimeOffset.Now;
        _svc.Accounts.Update(acc);

        await LaunchAsync(acc, join);
    }

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
        finally
        {
            Busy = false;
        }
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

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void OpenServerBrowser()
    {
        long placeId = GameLinkParser.TryParse(PlaceIdInput, out var link) ? link.PlaceId : 0;
        var win = new ServerBrowserWindow(_svc, SelectedAccount!.Model, placeId, this)
        {
            Owner = Application.Current.MainWindow
        };
        win.Show();
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

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void CopyCookie()
    {
        Clipboard.SetText(SelectedAccount!.Model.SecurityToken);
        Status = "Cookie copied to clipboard.";
    }

    [RelayCommand]
    private void CheckForUpdate()
    {
        var win = new UpdateWindow { Owner = Application.Current.MainWindow };
        win.ShowDialog();
    }

    [RelayCommand]
    private void OpenSettings()
    {
        var win = new SettingsWindow(_svc) { Owner = Application.Current.MainWindow };
        win.ShowDialog();
        ThemeManager.Apply(_svc.Settings.Current.ThemeName);
    }

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
        _svc.Accounts.Reorder(Accounts.Select(a => a.Id).ToList());
    }

    public void ReorderFromView(IReadOnlyList<Guid> orderedIds) => _svc.Accounts.Reorder(orderedIds);

    // --- helpers --------------------------------------------------

    private void SyncInstance(RobloxInstance inst)
    {
        var vm = Instances.FirstOrDefault(x => x.Model.Id == inst.Id);
        if (vm is null) return;
        vm.Sync();
        var acc = Accounts.FirstOrDefault(a => a.Id == inst.AccountId);
        if (acc is not null)
            acc.IsInGame = Instances.Any(x => x.Model.AccountId == inst.AccountId
                                              && x.Model.State is InstanceState.Running or InstanceState.Launching);

        if (inst.State is InstanceState.Disconnected
            && _svc.Settings.Current.AutoRelaunchOnDisconnect)
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
