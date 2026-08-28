using CommunityToolkit.Mvvm.ComponentModel;
using MultiRoblox.Core.Models;
using MultiRoblox.Core.Services;

namespace MultiRoblox.App.ViewModels;

public partial class AccountItemViewModel : ObservableObject
{
    public Account Model { get; }

    [ObservableProperty] private string _label;
    [ObservableProperty] private AccountHealth _health;
    [ObservableProperty] private bool _isInGame;

    /// <summary>True while this row is the one being dragged (rendered semi-transparent).</summary>
    [ObservableProperty] private bool _isDragging;

    public AccountItemViewModel(Account model)
    {
        Model = model;
        _label = model.DisplayLabel;
        _health = AccountHealth.Unknown;
    }

    public Guid Id => Model.Id;
    public string Group => Model.EffectiveGroup;

    public string HealthText => Health switch
    {
        AccountHealth.Valid => "Signed in",
        AccountHealth.NeedsAttention => "Needs re-login",
        _ => "Not checked yet",
    };

    public void Refresh()
    {
        Label = Model.DisplayLabel;
        OnPropertyChanged(nameof(Group));
        OnPropertyChanged(nameof(HealthText));
    }

    partial void OnHealthChanged(AccountHealth value) => OnPropertyChanged(nameof(HealthText));
}
