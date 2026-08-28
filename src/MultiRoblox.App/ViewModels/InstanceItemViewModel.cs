using CommunityToolkit.Mvvm.ComponentModel;
using MultiRoblox.Core.Models;

namespace MultiRoblox.App.ViewModels;

public partial class InstanceItemViewModel : ObservableObject
{
    public RobloxInstance Model { get; }

    [ObservableProperty] private string _stateText;

    public InstanceItemViewModel(RobloxInstance model)
    {
        Model = model;
        _stateText = model.State.ToString();
    }

    public string AccountLabel => Model.AccountLabel;
    public long PlaceId => Model.PlaceId;
    public string Where => string.IsNullOrEmpty(Model.JobId) ? $"Place {Model.PlaceId}" : $"Place {Model.PlaceId} · {Model.JobId}";

    public void Sync() { StateText = Model.State.ToString(); }
}
