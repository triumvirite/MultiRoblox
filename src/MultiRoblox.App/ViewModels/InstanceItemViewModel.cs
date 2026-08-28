using CommunityToolkit.Mvvm.ComponentModel;
using MultiRoblox.Core.Models;

namespace MultiRoblox.App.ViewModels;

public partial class InstanceItemViewModel : ObservableObject
{
    public RobloxInstance Model { get; }

    [ObservableProperty] private string _stateText;

    /// <summary>Resolved place name; shown on the second line of the "Where" column. "" until fetched.</summary>
    [ObservableProperty] private string _gameName = "";

    public InstanceItemViewModel(RobloxInstance model)
    {
        Model = model;
        _stateText = model.State.ToString();
    }

    public string AccountLabel => Model.AccountLabel;
    public long PlaceId => Model.PlaceId;

    /// <summary>First "Where" line: the place id, plus the job id when joining a specific server.</summary>
    public string PlaceLine => string.IsNullOrEmpty(Model.JobId)
        ? Model.PlaceId.ToString()
        : $"{Model.PlaceId} · {Model.JobId}";

    public void Sync() { StateText = Model.State.ToString(); }
}
