using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MultiRoblox.Core.Models;
using MultiRoblox.Core.Services;

namespace MultiRoblox.App.ViewModels;

public enum AccountStatusKind { Unknown, NeedsLogin, SignedIn, InGame }

/// <summary>One checkable row in an account's "Categories" popup.</summary>
public sealed partial class CategoryChoice : ObservableObject
{
    private readonly AccountItemViewModel _owner;
    public string Name { get; }

    public CategoryChoice(AccountItemViewModel owner, string name)
    {
        _owner = owner;
        Name = name;
        _isMember = owner.InCategory(name);
    }

    [ObservableProperty] private bool _isMember;

    partial void OnIsMemberChanged(bool value) => _owner.SetCategoryMembership(Name, value);
}

public partial class AccountItemViewModel : ObservableObject
{
    public Account Model { get; }

    [ObservableProperty] private string _label;
    [ObservableProperty] private AccountHealth _health;
    [ObservableProperty] private bool _isInGame;

    /// <summary>Name of the game this account is currently in (when <see cref="IsInGame"/>).</summary>
    [ObservableProperty] private string _inGameName = "";

    /// <summary>True while this row is the one being dragged (rendered semi-transparent).</summary>
    [ObservableProperty] private bool _isDragging;

    /// <summary>Editable alias / description for the account grid. Setter persists via <see cref="Persist"/>.</summary>
    [ObservableProperty] private string _alias;
    [ObservableProperty] private string _note;

    /// <summary>Invoked when an editable field changes so the owner can save the model.</summary>
    public Action? Persist { get; init; }

    public AccountItemViewModel(Account model)
    {
        Model = model;
        _label = model.DisplayLabel;
        _alias = model.Alias;
        _note = model.Note;
        _health = AccountHealth.Unknown;
    }

    /// <summary>Account-list row label: "username - alias" when an alias is set, else just the username.
    /// Only the account list uses this; everywhere else shows the bare username.</summary>
    public string ListLabel => string.IsNullOrWhiteSpace(Alias) ? Label : $"{Label} - {Alias}";

    partial void OnAliasChanged(string value)
    {
        OnPropertyChanged(nameof(ListLabel));
        if (Model.Alias == value) return;
        Model.Alias = value;
        Persist?.Invoke();
    }

    partial void OnLabelChanged(string value) => OnPropertyChanged(nameof(ListLabel));

    /// <summary>Has a non-empty description — drives the yellow tint on the row's description icon.</summary>
    public bool HasNote => !string.IsNullOrWhiteSpace(Note);

    partial void OnNoteChanged(string value)
    {
        OnPropertyChanged(nameof(HasNote));
        if (Model.Note == value) return;
        Model.Note = value;
        Persist?.Invoke();
    }

    public Guid Id => Model.Id;
    public IReadOnlyList<string> Categories => Model.Categories;
    public bool InCategory(string name) => Model.Categories.Any(c => c.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Comma-joined category list shown in the account grid's "Categories" cell.</summary>
    public string CategoriesText => Model.Categories.Count > 0 ? string.Join(", ", Model.Categories) : "—";

    /// <summary>Checkable list backing the "Categories" popup. Rebuilt via <see cref="SetCategoryUniverse"/>.</summary>
    public ObservableCollection<CategoryChoice> CategoryChoices { get; } = new();

    public void SetCategoryUniverse(IEnumerable<string> allCategories)
    {
        CategoryChoices.Clear();
        foreach (var n in allCategories)
            CategoryChoices.Add(new CategoryChoice(this, n));
    }

    public void SetCategoryMembership(string name, bool member)
    {
        bool changed = member
            ? !InCategory(name) && Add(name)
            : Model.Categories.RemoveAll(c => c.Equals(name, StringComparison.OrdinalIgnoreCase)) > 0;
        if (!changed) return;
        Persist?.Invoke();
        OnPropertyChanged(nameof(Categories));
        OnPropertyChanged(nameof(CategoriesText));

        bool Add(string n) { Model.Categories.Add(n); return true; }
    }

    public AccountStatusKind StatusKind =>
        IsInGame ? AccountStatusKind.InGame
        : Health switch
        {
            AccountHealth.Valid => AccountStatusKind.SignedIn,
            AccountHealth.NeedsAttention => AccountStatusKind.NeedsLogin,
            _ => AccountStatusKind.Unknown,
        };

    public string StatusText => StatusKind switch
    {
        AccountStatusKind.InGame => string.IsNullOrWhiteSpace(InGameName) ? "In-game" : $"In-game: {InGameName}",
        AccountStatusKind.SignedIn => "Signed in",
        AccountStatusKind.NeedsLogin => "Needs re-login",
        _ => "Not checked yet",
    };

    /// <summary>Session is dead — the row is double-click-to-re-login.</summary>
    public bool NeedsReLogin => StatusKind == AccountStatusKind.NeedsLogin;

    public void Refresh()
    {
        Label = Model.DisplayLabel;
        Alias = Model.Alias;
        Note = Model.Note;
        OnPropertyChanged(nameof(ListLabel));
        OnPropertyChanged(nameof(HasNote));
        OnPropertyChanged(nameof(Categories));
        OnPropertyChanged(nameof(CategoriesText));
        NotifyStatus();
    }

    partial void OnHealthChanged(AccountHealth value) => NotifyStatus();
    partial void OnIsInGameChanged(bool value) => NotifyStatus();
    partial void OnInGameNameChanged(string value) => NotifyStatus();

    private void NotifyStatus()
    {
        OnPropertyChanged(nameof(StatusKind));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(NeedsReLogin));
    }
}
