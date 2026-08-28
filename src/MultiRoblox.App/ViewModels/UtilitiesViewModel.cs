using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MultiRoblox.Core.Models;
using MultiRoblox.Core.Services;

namespace MultiRoblox.App.ViewModels;

public partial class UtilitiesViewModel : ObservableObject
{
    private readonly AppServices _svc;
    private readonly Account _account;
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

    // block
    [ObservableProperty] private string _blockUserInput = "";

    // player finder
    [ObservableProperty] private string _finderInput = "";
    public ObservableCollection<string> FinderResults { get; } = new();

    public UtilitiesViewModel(AppServices svc, Account account, MainViewModel main)
    {
        _svc = svc;
        _account = account;
        _main = main;
        var client = svc.Pool.Get(account);
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

    [RelayCommand]
    private async Task JoinGroupAsync()
    {
        if (!long.TryParse(GroupIdInput.Trim(), out long id)) { Status = "Bad group id."; return; }
        try { await _util.JoinGroupAsync(id); Status = $"Requested to join group {id}."; }
        catch (Exception ex) { Status = "Failed: " + ex.Message; }
    }

    [RelayCommand]
    private async Task LeaveGroupAsync()
    {
        if (!long.TryParse(GroupIdInput.Trim(), out long id)) { Status = "Bad group id."; return; }
        try { await _util.LeaveGroupAsync(id, _account.UserId); Status = $"Left group {id}."; }
        catch (Exception ex) { Status = "Failed: " + ex.Message; }
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
