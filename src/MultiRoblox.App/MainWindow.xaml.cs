using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using MultiRoblox.App.ViewModels;

namespace MultiRoblox.App;

public partial class MainWindow : Window
{
    private Point _dragStart;
    private AccountItemViewModel? _dragItem;
    private bool _menuOpen;

    public MainWindow()
    {
        InitializeComponent();
        UpdatePopup.CustomPopupPlacementCallback = CenterUnderTarget;
        AddHandler(PreviewMouseDownEvent, new MouseButtonEventHandler(DismissCatPopupOnOutsideClick), handledEventsToo: true);
        AddHandler(PreviewMouseDownEvent, new MouseButtonEventHandler(CommitGridEditOnOutsideClick), handledEventsToo: true);
        Deactivated += (_, _) => { CloseCatPopup(); AccountsGrid?.CommitEdit(DataGridEditingUnit.Row, true); };
    }

    private void AccountList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        Vm.SetSelectedAccounts(AccountList.SelectedItems.OfType<AccountItemViewModel>());

    // Toggle the update dropdown on the button; a StaysOpen=False popup would otherwise be dismissed
    // on mouse-down and re-opened by the click, so we track the last close.
    private DateTime _updatePopupClosedAt;

    private void UpdatePopup_Closed(object sender, EventArgs e) => _updatePopupClosedAt = DateTime.UtcNow;

    private void UpdateButton_Click(object sender, RoutedEventArgs e)
    {
        bool recentlyClosed = (DateTime.UtcNow - _updatePopupClosedAt).TotalMilliseconds < 250;
        if (UpdatePopup.IsOpen || Vm.UpdatePopupOpen || recentlyClosed)
        {
            UpdatePopup.IsOpen = false;
            Vm.UpdatePopupOpen = false;
            return;
        }
        Vm.CheckForUpdateCommand.Execute(null);
    }

    /// <summary>Centre the update dropdown horizontally under the button.</summary>
    private static CustomPopupPlacement[] CenterUnderTarget(Size popupSize, Size targetSize, Point offset)
        => new[]
        {
            new CustomPopupPlacement(
                new Point((targetSize.Width - popupSize.Width) / 2, targetSize.Height + offset.Y),
                PopupPrimaryAxis.Horizontal),
        };

    private MainViewModel Vm => (MainViewModel)DataContext;

    // ---------- account grid: "Categories" cell popup ----------
    // A ToggleButton + StaysOpen=False Popup normally re-opens on the click that just closed it
    // (the Win32 outside-click dismiss and the button's toggle fight). We take over: swallow the
    // button's own toggle, and set the final open state on the next input tick — after the dismiss.

    // The "Categories" popups use StaysOpen=True and are toggled/dismissed entirely from here — a
    // StaysOpen=False popup fights the click that opens or closes it (dismiss on mouse-down vs.
    // toggle on mouse-up), which is why it seemed stuck open.
    private System.Windows.Controls.Primitives.Popup? _openCatPopup;

    private void CatBtn_PreviewDown(object sender, MouseButtonEventArgs e)
    {
        var tb = (System.Windows.Controls.Primitives.ToggleButton)sender;
        var popup = ((Panel)tb.Parent).Children.OfType<System.Windows.Controls.Primitives.Popup>().FirstOrDefault();
        if (popup is null) return;
        e.Handled = true;

        if (ReferenceEquals(_openCatPopup, popup))
        {
            CloseCatPopup();               // clicking the open cell again → close
            return;
        }
        CloseCatPopup();                   // close any other row's popup first
        popup.IsOpen = true;
        tb.IsChecked = true;
        _openCatPopup = popup;
    }

    private void CloseCatPopup()
    {
        if (_openCatPopup is not { } p) return;
        p.IsOpen = false;
        if (p.PlacementTarget is System.Windows.Controls.Primitives.ToggleButton t) t.IsChecked = false;
        _openCatPopup = null;
    }

    private void CatPopup_Closed(object sender, EventArgs e)
    {
        if (ReferenceEquals(_openCatPopup, sender)) _openCatPopup = null;
    }

    private void DismissCatPopupOnOutsideClick(object sender, MouseButtonEventArgs e)
    {
        if (_openCatPopup is not { } p) return;
        var src = e.OriginalSource as System.Windows.Media.Visual;
        bool insidePopup = src is not null && p.Child is System.Windows.Media.Visual pc && src.IsDescendantOf(pc);
        bool onOwner = src is not null && p.PlacementTarget is System.Windows.Media.Visual pt && src.IsDescendantOf(pt);
        if (!insidePopup && !onOwner) CloseCatPopup();
    }

    // ---------- account grid: single-click to edit a text cell ----------

    private void AccountsGrid_CellSingleClick(object sender, MouseButtonEventArgs e)
    {
        var grid = (DataGrid)sender;
        var cell = FindAncestor<DataGridCell>(e.OriginalSource as DependencyObject);

        // Any click that isn't inside the cell currently being edited commits & closes that editor —
        // including empty space in the grid, a read-only cell, or another row.
        if (cell is null || !cell.IsEditing)
        {
            grid.CommitEdit(DataGridEditingUnit.Cell, true);
            grid.CommitEdit(DataGridEditingUnit.Row, true);
        }

        if (cell is not { IsEditing: false, IsReadOnly: false }) return;

        // one click straight into edit mode on an editable text cell
        if (!cell.IsFocused) cell.Focus();
        grid.SelectedCells.Clear();
        grid.CurrentCell = new DataGridCellInfo(cell);
        grid.BeginEdit(e);
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (cell.Content is TextBox tb) { tb.Focus(); tb.SelectAll(); }
        }), System.Windows.Threading.DispatcherPriority.Input);
    }

    private void CommitGridEditOnOutsideClick(object sender, MouseButtonEventArgs e)
    {
        if (AccountsGrid is null || !AccountsGrid.IsVisible) return;
        if (FindAncestor<DataGrid>(e.OriginalSource as DependencyObject) == AccountsGrid) return; // click landed inside it
        AccountsGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        AccountsGrid.CommitEdit(DataGridEditingUnit.Row, true);
    }

    // ---------- Shift+wheel = horizontal scroll in the grids ----------

    private void Grid_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Shift) == 0) return;
        if (FindDescendant<ScrollViewer>((DependencyObject)sender) is not { } sv) return;
        sv.ScrollToHorizontalOffset(sv.HorizontalOffset - e.Delta);
        e.Handled = true;
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T hit) return hit;
            if (FindDescendant<T>(child) is { } deep) return deep;
        }
        return null;
    }

    // ---------- account drag-to-reorder with live preview ----------

    private void AccountList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(null);
        _dragItem = _menuOpen ? null : ItemUnder(e.OriginalSource);
    }

    private void AccountList_MouseMove(object sender, MouseEventArgs e)
    {
        if (_menuOpen || e.LeftButton != MouseButtonState.Pressed || _dragItem is null) return;
        // let Ctrl/Shift multi-select gestures through instead of starting a reorder drag
        if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) != 0) return;
        if (AccountList.SelectedItems.Count > 1) return;
        var diff = _dragStart - e.GetPosition(null);
        if (Math.Abs(diff.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(diff.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        _dragItem.IsDragging = true;
        try { DragDrop.DoDragDrop(AccountList, _dragItem, DragDropEffects.Move); }
        finally
        {
            if (_dragItem is not null) _dragItem.IsDragging = false;
            Vm.PersistOrder();
            _dragItem = null;
        }
    }

    private void AccountList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ItemUnder(e.OriginalSource) is not { } acct) return;

        if (acct.NeedsReLogin)
            Vm.ReLogin(acct);                       // signed out → re-login
        else if (Vm.HasQuickJoin)
            _ = Vm.QuickJoinAccountAsync(acct);     // otherwise → launch into the Quick Join game
        else
            return;

        e.Handled = true;
    }

    private void AccountList_DragOver(object sender, DragEventArgs e)
    {
        if (_dragItem is null) { e.Effects = DragDropEffects.None; return; }
        e.Effects = DragDropEffects.Move;

        var over = ItemUnder(e.OriginalSource);
        int from = Vm.Accounts.IndexOf(_dragItem);
        int to = over is null ? Vm.Accounts.Count - 1 : Vm.Accounts.IndexOf(over);
        if (from >= 0 && to >= 0 && from != to)
            Vm.Accounts.Move(from, to);   // live translucent preview: the row appears at the drop slot
        e.Handled = true;
    }

    private void AccountList_Drop(object sender, DragEventArgs e)
    {
        if (_dragItem is not null) _dragItem.IsDragging = false;
        Vm.PersistOrder();
    }

    private AccountItemViewModel? ItemUnder(object source) =>
        (source as DependencyObject) is { } d
            ? FindAncestor<ListBoxItem>(d)?.DataContext as AccountItemViewModel
              ?? (d as FrameworkElement)?.DataContext as AccountItemViewModel
            : null;

    private static T? FindAncestor<T>(DependencyObject? d) where T : DependencyObject
    {
        while (d is not null and not T) d = VisualTreeHelper.GetParent(d);
        return d as T;
    }

    // ---------- right-click: Add to category ----------

    private void AccountList_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var item = ItemUnder(e.OriginalSource);
        if (item is null) { e.Handled = true; return; }

        // If the right-clicked row isn't part of the current multi-selection, make it the selection.
        if (!AccountList.SelectedItems.Contains(item))
        {
            AccountList.SelectedItems.Clear();
            AccountList.SelectedItems.Add(item);
        }
        var targets = AccountList.SelectedItems.OfType<AccountItemViewModel>().ToList();
        var menu = new ContextMenu();

        if (targets.Count == 1)
        {
            var one = targets[0];
            menu.Items.Add(new MenuItem
            {
                Header = one.NeedsReLogin ? "Re-login (needed)" : "Re-login",
                Command = new SimpleCommand(() => Vm.ReLogin(one)),
            });
            menu.Items.Add(new Separator());
        }

        var realCategories = Vm.Categories
            .Where(c => c != MainViewModel.AllCategories && c != MainViewModel.NewCategoryItem)
            .ToList();

        // "Add to category" — a Windows-style fly-out submenu. Each category is a checkbox: an account
        // can be in several at once, so clicking toggles membership for every selected account.
        var addTo = new MenuItem { Header = "Add to category" };
        foreach (var cat in realCategories)
        {
            var c = cat;
            bool allIn = targets.All(t => t.InCategory(c));
            addTo.Items.Add(new MenuItem
            {
                Header = c,
                IsCheckable = true,
                IsChecked = allIn,
                StaysOpenOnClick = true,
                Command = new SimpleCommand(() =>
                {
                    foreach (var t in targets)
                        if (allIn) Vm.UnassignCategory(t, c); else Vm.AssignCategory(t, c);
                }),
            });
        }
        if (realCategories.Count > 0) addTo.Items.Add(new Separator());
        addTo.Items.Add(new MenuItem
        {
            Header = "New category…",
            Command = new SimpleCommand(() => Vm.NewCategoryAndAssign(targets)),
        });
        menu.Items.Add(addTo);

        // Quick "remove from all categories" when any selected account is in one
        if (targets.Any(t => t.Model.Categories.Count > 0))
            menu.Items.Add(new MenuItem
            {
                Header = "Remove from all categories",
                Command = new SimpleCommand(() => { foreach (var t in targets) Vm.ClearCategories(t); }),
            });

        // "Leave Current Instance" — only for accounts that are actually in-game right now.
        var inGame = targets.Where(t => t.IsInGame).ToList();
        if (inGame.Count > 0)
        {
            menu.Items.Add(new Separator());
            menu.Items.Add(new MenuItem
            {
                Header = inGame.Count > 1
                    ? $"Leave current instances ({inGame.Count})"
                    : "Leave current instance",
                Command = new SimpleCommand(() =>
                {
                    foreach (var t in inGame) Vm.LeaveInstanceForAccount(t);
                }),
            });
        }

        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem
        {
            Header = targets.Count > 1 ? $"Remove {targets.Count} accounts" : "Remove account",
            Command = new SimpleCommand(() => Vm.RemoveAccounts(targets)),
        });

        // Suppress drag-reorder while the menu is up.
        _menuOpen = true;
        _dragItem = null;
        menu.Closed += (_, _) => _menuOpen = false;
        AccountList.ContextMenu = menu;
    }

    // ---------- favorites ----------

    private void Favorite_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Vm.SelectedFavorite is not null && Vm.JoinFavoriteCommand.CanExecute(Vm.SelectedFavorite))
            Vm.JoinFavoriteCommand.Execute(Vm.SelectedFavorite);
    }

    private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }

    private void Recent_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Vm.SelectedRecent is not null && Vm.JoinRecentCommand.CanExecute(Vm.SelectedRecent))
            Vm.JoinRecentCommand.Execute(Vm.SelectedRecent);
    }

    private sealed class SimpleCommand(Action run) : ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => run();
    }
}
