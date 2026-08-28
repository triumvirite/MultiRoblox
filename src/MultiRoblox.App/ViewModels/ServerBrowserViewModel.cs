using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MultiRoblox.Core.Models;
using MultiRoblox.Core.Services;

namespace MultiRoblox.App.ViewModels;

public partial class ServerRowViewModel : ObservableObject
{
    public required string JobId { get; init; }
    public int Playing { get; init; }
    public int MaxPlayers { get; init; }
    public int Ping { get; init; }
    public double Fps { get; init; }
    public string Slots => $"{Playing}/{MaxPlayers}";
}

public partial class ServerBrowserViewModel : ObservableObject
{
    private readonly AppServices _svc;
    private readonly Account _account;
    private readonly MainViewModel _main;
    private readonly GamesClient _games;

    public ObservableCollection<ServerRowViewModel> Servers { get; } = new();

    [ObservableProperty] private string _placeIdInput;
    [ObservableProperty] private ServerRowViewModel? _selected;
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _busy;

    public ObservableCollection<GameSummary> RecentGames { get; } = new();
    public ObservableCollection<GameSummary> FavoriteGames { get; } = new();

    public ServerBrowserViewModel(AppServices svc, Account account, long placeId, MainViewModel main)
    {
        _svc = svc;
        _account = account;
        _main = main;
        _games = new GamesClient(_svc.Pool.Get(account));
        _placeIdInput = placeId > 0 ? placeId.ToString() : account.SavedPlaceId;
    }

    private long CurrentPlaceId() =>
        GameLinkParser.TryParse(PlaceIdInput, out var l) ? l.PlaceId : 0;

    [RelayCommand]
    private async Task RefreshAsync()
    {
        long placeId = CurrentPlaceId();
        if (placeId <= 0)
        {
            Status = "Enter a Place ID or paste a game link.";
            return;
        }
        try
        {
            Busy = true;
            Status = "Loading servers…";
            Servers.Clear();
            var list = await _games.GetAllPublicServersAsync(placeId, maxPages: 5);
            foreach (var s in list.OrderBy(s => s.Ping == 0 ? int.MaxValue : s.Ping))
                Servers.Add(new ServerRowViewModel
                {
                    JobId = s.Id, Playing = s.Playing, MaxPlayers = s.MaxPlayers, Ping = s.Ping, Fps = s.Fps
                });
            Status = $"{Servers.Count} servers.";
        }
        catch (Exception ex) { Status = "Failed: " + ex.Message; }
        finally { Busy = false; }
    }

    [RelayCommand]
    private async Task LoadListsAsync()
    {
        try
        {
            if (_account.UserId == 0) return;
            RecentGames.Clear();
            FavoriteGames.Clear();
            foreach (var g in await _games.GetRecentGamesAsync(_account.UserId)) RecentGames.Add(g);
            foreach (var g in await _games.GetFavoriteGamesAsync(_account.UserId)) FavoriteGames.Add(g);
        }
        catch (Exception ex) { Status = "Lists failed: " + ex.Message; }
    }

    [RelayCommand]
    private async Task JoinSelectedAsync()
    {
        long placeId = CurrentPlaceId();
        if (Selected is null || placeId <= 0) return;
        await _main.LaunchAsync(_account, JoinRequest.Server(placeId, Selected.JobId));
        Status = $"Launching into {Selected.JobId}…";
    }

    [RelayCommand]
    private async Task JoinSmallestAsync()
    {
        long placeId = CurrentPlaceId();
        if (placeId <= 0) return;
        Busy = true;
        try
        {
            var s = await _games.GetSmallestJoinableServerAsync(placeId);
            if (s is null) { Status = "No joinable server found."; return; }
            await _main.LaunchAsync(_account, JoinRequest.Server(placeId, s.Id));
        }
        finally { Busy = false; }
    }

    [RelayCommand]
    private void UseGame(GameSummary? g)
    {
        if (g is null || g.PlaceId == 0) return;
        PlaceIdInput = g.PlaceId.ToString();
    }

    [RelayCommand]
    private void CopyJobId()
    {
        if (Selected is not null) Clipboard.SetText(Selected.JobId);
    }
}
