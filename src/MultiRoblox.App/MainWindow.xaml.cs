using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using MultiRoblox.App.ViewModels;
using MultiRoblox.Core.Models;

namespace MultiRoblox.App;

public partial class MainWindow : Window
{
    private Point _dragStart;
    private AccountItemViewModel? _dragItem;
    private bool _menuOpen;

    public MainWindow()
    {
        InitializeComponent();
        UpdatePopup.CustomPopupPlacementCallback = AboveTargetLeftAligned;
        AddHandler(PreviewMouseDownEvent, new MouseButtonEventHandler(DismissCatPopupOnOutsideClick), handledEventsToo: true);
        AddHandler(PreviewMouseDownEvent, new MouseButtonEventHandler(CommitGridEditOnOutsideClick), handledEventsToo: true);
        Deactivated += (_, _) => { CloseCatPopup(); AccountsGrid?.CommitEdit(DataGridEditingUnit.Row, true); };

        Loaded += (_, _) =>
        {
            RestoreGridLayout(AccountsGrid, "AccountsGrid");
            var w = App.Services.Settings.Current.SidebarWidth;
            if (w > 0) SidebarColumn.Width = new GridLength(w);
        };
        Closing += (_, _) => SaveGridLayout(AccountsGrid, "AccountsGrid");
        AccountsGrid.ColumnReordered += (_, _) => SaveGridLayout(AccountsGrid, "AccountsGrid");
        AccountsGrid.Sorting += (_, _) => Dispatcher.BeginInvoke(new Action(() => SaveGridLayout(AccountsGrid, "AccountsGrid")));
        AccountsGrid.AddHandler(Thumb.DragCompletedEvent,
            new DragCompletedEventHandler((_, _) => SaveGridLayout(AccountsGrid, "AccountsGrid")));
    }

    // ---------- data-grid layout persistence (column width / order / sort) ----------

    private bool _restoringGrid;

    private void RestoreGridLayout(DataGrid grid, string id)
    {
        if (!App.Services.Settings.Current.GridLayouts.TryGetValue(id, out var saved) || saved is null || saved.Count == 0)
            return;

        _restoringGrid = true;
        try
        {
            foreach (var st in saved)
            {
                var col = grid.Columns.FirstOrDefault(c => (c.Header as string) == st.Header);
                if (col is null) continue;
                // Restore widths as *star weights* (using the saved pixel numbers as the ratio) so the
                // columns keep filling the grid; fixed-width columns (e.g. a button column) are left alone.
                if (st.Width > 0 && col.CanUserResize)
                    col.Width = new DataGridLength(st.Width, DataGridLengthUnitType.Star);
                if (st.DisplayIndex >= 0 && st.DisplayIndex < grid.Columns.Count) col.DisplayIndex = st.DisplayIndex;
                col.SortDirection = st.SortDirection switch
                {
                    "Ascending" => ListSortDirection.Ascending,
                    "Descending" => ListSortDirection.Descending,
                    _ => null,
                };
            }

            var sortCol = grid.Columns.FirstOrDefault(c => c.SortDirection is not null);
            var view = CollectionViewSource.GetDefaultView(grid.ItemsSource);
            if (sortCol?.SortMemberPath is { Length: > 0 } path && view is not null)
            {
                view.SortDescriptions.Clear();
                view.SortDescriptions.Add(new SortDescription(path, sortCol.SortDirection!.Value));
            }
        }
        finally { _restoringGrid = false; }
    }

    private void SidebarSplitter_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        App.Services.Settings.Current.SidebarWidth = SidebarColumn.ActualWidth;
        App.Services.Settings.Save();
    }

    private void SaveGridLayout(DataGrid grid, string id)
    {
        if (_restoringGrid) return;

        App.Services.Settings.Current.GridLayouts[id] = grid.Columns.Select(c => new GridColumnState
        {
            Header = c.Header as string ?? "",
            Width = c.CanUserResize ? c.ActualWidth : 0,
            DisplayIndex = c.DisplayIndex,
            SortDirection = c.SortDirection?.ToString(),
        }).ToList();
        App.Services.Settings.Save();
    }

    // ---------- sidebar category list: right-click to remove ----------

    private void CategoryItemMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu { PlacementTarget: ComboBoxItem { DataContext: string cat } } menu) return;
        if (menu.Items.Count > 0 && menu.Items[0] is MenuItem mi)
            mi.Header = $"Remove \"{cat}\" category";
    }

    private void RemoveCategoryMenuItem_Click(object sender, RoutedEventArgs e)
    {
        // sender (MenuItem) -> its ContextMenu -> PlacementTarget is the ComboBoxItem that was right-clicked
        var target = (((sender as MenuItem)?.Parent) as ContextMenu)?.PlacementTarget as ComboBoxItem;
        if (target?.DataContext is not string cat) return;
        if (cat == MainViewModel.AllCategories || cat == MainViewModel.NewCategoryItem) return;

        CategoryBox.IsDropDownOpen = false;

        int n = Vm.CountInCategory(cat);
        string msg = n > 0
            ? $"Remove the category “{cat}”?\n\nIt will be taken off {n} account{(n == 1 ? "" : "s")}. This can't be undone."
            : $"Remove the category “{cat}”?";
        if (MessageBox.Show(msg, "Remove category", MessageBoxButton.YesNo,
                MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes)
            Vm.RemoveCategoryEverywhere(cat);
    }

    private void AccountList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        Vm.SetSelectedAccounts(AccountList.SelectedItems.OfType<AccountItemViewModel>());

    private void Favorites_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        Vm.SetSelectedFavorites(((ListBox)sender).SelectedItems.OfType<FavoriteGame>());

    private void Recents_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        Vm.SetSelectedRecents(((ListBox)sender).SelectedItems.OfType<RecentGame>());

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

    private const double UpdatePopupGap = 5;

    /// <summary>Place the update dropdown above the button: popup bottom-left on the button's top-left,
    /// minus a fixed vertical gap.</summary>
    private static CustomPopupPlacement[] AboveTargetLeftAligned(Size popupSize, Size targetSize, Point offset)
        => new[]
        {
            new CustomPopupPlacement(
                new Point(0, -popupSize.Height - UpdatePopupGap),
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
        bool onButton = FindAncestor<Button>(e.OriginalSource as DependencyObject) is not null;
        _dragItem = (_menuOpen || onButton) ? null : ItemUnder(e.OriginalSource);

        if (DoubleClickTarget(AccountList, e) is AccountItemViewModel acct)
        {
            if (acct.NeedsReLogin) Vm.ReLogin(acct);
            else if (Vm.HasQuickJoin) _ = Vm.QuickJoinAccountAsync(acct);
        }
    }

    // ---------- custom double-click: tighter window than the OS default, ignores Ctrl/Shift
    //            (multi-select gestures), and only starts counting once the row is already selected —
    //            so a click that first selects the row never becomes a double-click. ----------
    private const double DoubleClickMs = 280;
    private object? _dcItem;
    private DateTime _dcTime;

    /// <summary>Call from a list's PreviewMouseLeftButtonDown; returns the row's item when this click
    /// completes a valid double-click, else null.</summary>
    private object? DoubleClickTarget(ListBox list, MouseButtonEventArgs e)
    {
        if (FindAncestor<Button>(e.OriginalSource as DependencyObject) is not null) { _dcItem = null; return null; }

        var item = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject)?.DataContext;
        if (item is null || (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) != 0)
        {
            _dcItem = null;
            return null;
        }

        // Preview fires before the ListBox updates selection, so this is the pre-click state.
        if (!list.SelectedItems.Contains(item))
        {
            _dcItem = null;   // this click only selects the row — don't begin double-click tracking
            return null;
        }

        var now = DateTime.UtcNow;
        bool isDouble = ReferenceEquals(item, _dcItem)
                        && (now - _dcTime).TotalMilliseconds <= DoubleClickMs;

        _dcItem = item;
        _dcTime = now;
        return isDouble ? item : null;
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

    private void AccountList_DragOver(object sender, DragEventArgs e)
    {
        if (_dragItem is null) { e.Effects = DragDropEffects.None; return; }
        e.Effects = DragDropEffects.Move;
        e.Handled = true;

        int from = Vm.Accounts.IndexOf(_dragItem);
        if (from < 0) return;

        // Only reorder once the cursor pushes into the outer 40% of an adjacent row — the middle 20%
        // is a dead zone, so a small wobble over a neighbour doesn't swap it. One step per event.
        const double edge = 0.40;
        double y = e.GetPosition(AccountList).Y;

        if (from + 1 < Vm.Accounts.Count
            && AccountList.ItemContainerGenerator.ContainerFromItem(Vm.Accounts[from + 1]) is FrameworkElement below)
        {
            double top = below.TranslatePoint(new Point(0, 0), AccountList).Y;
            if (y > top + below.ActualHeight * (1 - edge)) { Vm.Accounts.Move(from, from + 1); return; }
        }

        if (from - 1 >= 0
            && AccountList.ItemContainerGenerator.ContainerFromItem(Vm.Accounts[from - 1]) is FrameworkElement above)
        {
            double top = above.TranslatePoint(new Point(0, 0), AccountList).Y;
            if (y < top + above.ActualHeight * edge) Vm.Accounts.Move(from, from - 1);
        }
    }

    private void AccountList_Drop(object sender, DragEventArgs e)
    {
        if (_dragItem is not null) _dragItem.IsDragging = false;
        Vm.PersistOrder();
    }

    private void DescriptionIcon_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;   // don't let the click bubble into row drag / double-click handling
        if ((sender as FrameworkElement)?.DataContext is AccountItemViewModel acct)
            PromptDescription(acct);
    }

    private static void PromptDescription(AccountItemViewModel acct)
    {
        var v = Services.Dialogs.Prompt("Description", $"Description for {acct.Label}:", acct.Note);
        if (v is not null) acct.Note = v;
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
            menu.Items.Add(new MenuItem
            {
                Header = "Set alias…",
                Command = new SimpleCommand(() =>
                {
                    var v = Services.Dialogs.Prompt("Alias", $"Alias for {one.Label}:", one.Alias);
                    if (v is not null) one.Alias = v.Trim();
                }),
            });
            if (!string.IsNullOrWhiteSpace(one.Alias))
                menu.Items.Add(new MenuItem
                {
                    Header = "Clear alias",
                    Command = new SimpleCommand(() => one.Alias = ""),
                });
            menu.Items.Add(new MenuItem
            {
                Header = "Set description…",
                Command = new SimpleCommand(() => PromptDescription(one)),
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

    private void Favorite_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (DoubleClickTarget((ListBox)sender, e) is FavoriteGame fav
            && Vm.JoinFavoriteCommand.CanExecute(fav))
            Vm.JoinFavoriteCommand.Execute(fav);
    }

    private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }

    private void Recent_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (DoubleClickTarget((ListBox)sender, e) is RecentGame rec
            && Vm.JoinRecentCommand.CanExecute(rec))
            Vm.JoinRecentCommand.Execute(rec);
    }

    private sealed class SimpleCommand(Action run) : ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => run();
    }
}
