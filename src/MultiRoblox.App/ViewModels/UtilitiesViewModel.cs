using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MultiRoblox.Core.Models;
using MultiRoblox.Core.Services;

namespace MultiRoblox.App.ViewModels;

public partial class UtilitiesViewModel : ObservableObject
{
    private readonly AppServices _svc;
    private readonly Account _account;                       // primary account (overview / block)
    private readonly IReadOnlyList<Account> _accounts;       // every selected account (group actions)
    private readonly AccountUtilities _util;
    private readonly GamesClient _games;
    private readonly MainViewModel _main;

    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _busy;

    // overview
    [ObservableProperty] private string _overviewText = "Loading…";
    [ObservableProperty] private string _description = "";

    // group
    [ObservableProperty] private string _groupIdInput = "";
    public string GroupScopeText => _accounts.Count > 1
        ? $"Groups — applies to all {_accounts.Count} selected accounts"
        : "Groups";
    public ObservableCollection<string> GroupResults { get; } = new();

    public string WindowTitle => _accounts.Count > 1
        ? $"Account utilities — {_account.Username} (+{_accounts.Count - 1} more)"
        : $"Account utilities — {_account.Username}";

    // block
    [ObservableProperty] private string _blockUserInput = "";

    // player finder
    [ObservableProperty] private string _finderInput = "";
    public ObservableCollection<string> FinderResults { get; } = new();

    public UtilitiesViewModel(AppServices svc, IReadOnlyList<Account> accounts, MainViewModel main)
    {
        _svc = svc;
        _accounts = accounts.Count > 0 ? accounts : throw new ArgumentException("no accounts", nameof(accounts));
        _account = _accounts[0];
        _main = main;
        var client = svc.Pool.Get(_account);
        _util = new AccountUtilities(client);
        _games = new GamesClient(client);
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        try
        {
            Busy = true;
            var o = await _util.GetOverviewAsync(_account.UserId);
            Description = o.Description;
            OverviewText =
                $"@{o.Username}  ·  id {o.UserId}\n" +
                $"Robux: {o.Robux:N0}    Premium: {(o.Premium ? "yes" : "no")}\n" +
                $"Birthdate: {o.Birthdate ?? "?"}    Email verified: {(o.EmailVerified ? "yes" : "no")}";
            Status = "Loaded.";
        }
        catch (Exception ex) { Status = "Failed: " + ex.Message; }
        finally { Busy = false; }
    }

    [RelayCommand]
    private async Task SaveDescriptionAsync()
    {
        try { await _util.SetDescriptionAsync(Description); Status = "Description saved."; }
        catch (Exception ex) { Status = "Failed: " + ex.Message; }
    }

    [RelayCommand]
    private async Task LogoutOthersAsync()
    {
        try { await _util.LogoutOtherSessionsAsync(); Status = "Other sessions signed out."; }
        catch (Exception ex) { Status = "Failed: " + ex.Message; }
    }

    [RelayCommand(CanExecute = nameof(NotBusy))]
    private Task JoinGroupAsync() => GroupActionAsync(join: true);

    [RelayCommand(CanExecute = nameof(NotBusy))]
    private Task LeaveGroupAsync() => GroupActionAsync(join: false);

    private bool NotBusy() => !Busy;

    partial void OnBusyChanged(bool value)
    {
        JoinGroupCommand.NotifyCanExecuteChanged();
        LeaveGroupCommand.NotifyCanExecuteChanged();
    }

    private async Task GroupActionAsync(bool join)
    {
        if (!long.TryParse(GroupIdInput.Trim(), out long id) || id <= 0) { Status = "Enter a valid group id."; return; }

        GroupResults.Clear();
        int ok = 0, fail = 0;
        try
        {
            Busy = true;
            Status = join ? $"Joining group {id}…" : $"Leaving group {id}…";
            foreach (var acc in _accounts)
            {
                var util = new AccountUtilities(_svc.Pool.Get(acc));
                try
                {
                    if (join)
                    {
                        await util.JoinGroupAsync(id);
                        // approval-required groups accept the POST (200) but only create a pending request
                        bool member = await util.IsGroupMemberAsync(acc.UserId, id);
                        GroupResults.Add(member
                            ? $"✓ {acc.Username} — joined"
                            : $"✓ {acc.Username} — join request sent (awaiting approval)");
                    }
                    else
                    {
                        await util.LeaveGroupAsync(id, acc.UserId);
                        GroupResults.Add($"✓ {acc.Username} — left");
                    }
                    ok++;
                }
                catch (Exception ex)
                {
                    fail++;
                    GroupResults.Add($"✗ {acc.Username} — {ex.Message}");
                }
            }
            Status = $"{(join ? "Join" : "Leave")} group {id}: {ok} ok" + (fail > 0 ? $", {fail} failed" : "") + ".";
        }
        finally { Busy = false; }
    }

    [RelayCommand]
    private async Task BlockAsync()
    {
        long? id = await ResolveAsync(BlockUserInput);
        if (id is null) { Status = "User not found."; return; }
        try { await _util.BlockUserAsync(id.Value); Status = "Blocked."; }
        catch (Exception ex) { Status = "Failed: " + ex.Message; }
    }

    [RelayCommand]
    private async Task UnblockAsync()
    {
        long? id = await ResolveAsync(BlockUserInput);
        if (id is null) { Status = "User not found."; return; }
        try { await _util.UnblockUserAsync(id.Value); Status = "Unblocked."; }
        catch (Exception ex) { Status = "Failed: " + ex.Message; }
    }

    [RelayCommand]
    private async Task FindAsync()
    {
        FinderResults.Clear();
        var names = FinderInput.Split(new[] { ',', ' ', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        var ids = new List<long>();
        foreach (var n in names)
        {
            var id = long.TryParse(n, out var raw) ? raw : await _games.ResolveUsernameAsync(n);
            if (id is not null) ids.Add(id.Value);
        }
        if (ids.Count == 0) { Status = "No users resolved."; return; }

        try
        {
            foreach (var p in await _games.FindPlayersAsync(ids))
            {
                string where = p.PresenceType switch
                {
                    0 => "offline",
                    1 => "online (website)",
                    2 => p.PlaceId is { } pl ? $"in game — place {pl} (job {p.GameId})" : "in game",
                    3 => "in Studio",
                    _ => p.LastLocation ?? "?",
                };
                FinderResults.Add($"{p.UserId}: {where}");
            }
            Status = $"{FinderResults.Count} result(s).";
        }
        catch (Exception ex) { Status = "Failed: " + ex.Message; }
    }

    [RelayCommand]
    private async Task JoinPlayerAsync(string? row)
    {
        // row form "userId: in game — place P (job J)"
        if (row is null) return;
        var parts = row.Split(':', 2);
        if (!long.TryParse(parts[0], out long uid)) return;
        try
        {
            var loc = (await _games.FindPlayersAsync(new[] { uid })).FirstOrDefault();
            if (loc?.PlaceId is { } place && loc.GameId is { Length: > 0 } job)
                await _main.LaunchAsync(_account, JoinRequest.Server(place, job));
            else
                Status = "That user isn't in a joinable game.";
        }
        catch (Exception ex) { Status = "Failed: " + ex.Message; }
    }

    private async Task<long?> ResolveAsync(string input) =>
        long.TryParse(input.Trim(), out var raw) ? raw : await _games.ResolveUsernameAsync(input.Trim());
}
