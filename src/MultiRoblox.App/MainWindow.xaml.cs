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

    public MainWindow()
    {
        InitializeComponent();
        UpdatePopup.CustomPopupPlacementCallback = CenterUnderTarget;
    }

    private void AccountList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        Vm.SetSelectedAccounts(AccountList.SelectedItems.OfType<AccountItemViewModel>());

    /// <summary>Centre the update dropdown horizontally under the button.</summary>
    private static CustomPopupPlacement[] CenterUnderTarget(Size popupSize, Size targetSize, Point offset)
        => new[]
        {
            new CustomPopupPlacement(
                new Point((targetSize.Width - popupSize.Width) / 2, targetSize.Height + offset.Y),
                PopupPrimaryAxis.Horizontal),
        };

    private MainViewModel Vm => (MainViewModel)DataContext;

    // ---------- Running-instances columns flex with the window ----------

    private ScrollViewer? _instancesScroll;
    private bool _scrollHooked;

    private void InstancesList_SizeChanged(object sender, SizeChangedEventArgs e) => LayoutInstanceColumns();

    private void LayoutInstanceColumns()
    {
        // GridView has no star sizing. Fill the exact viewport width (which already excludes the
        // scrollbar when one is shown) so the header row has no empty gap on either side.
        if (_instancesScroll is null)
        {
            _instancesScroll = FindDescendant<ScrollViewer>(InstancesList);
            if (_instancesScroll is not null && !_scrollHooked)
            {
                _instancesScroll.ScrollChanged += (_, _) => LayoutInstanceColumns();
                _scrollHooked = true;
            }
        }

        double content = _instancesScroll?.ViewportWidth ?? (InstancesList.ActualWidth - 2);
        double avail = content - ColState.ActualWidth - ColLeave.ActualWidth;
        if (avail < 160) return;

        double account = Math.Round(avail * 0.42);
        ColAccount.Width = account;
        ColWhere.Width = avail - account;   // absorbs the remainder exactly
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
        _dragItem = ItemUnder(e.OriginalSource);
    }

    private void AccountList_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragItem is null) return;
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

    private static T? FindAncestor<T>(DependencyObject d) where T : DependencyObject
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
        string label = targets.Count > 1 ? $"{targets.Count} accounts" : "account";

        var menu = new ContextMenu();
        var realCategories = Vm.Categories.Where(c => c != MainViewModel.AllCategories).ToList();

        if (realCategories.Count > 0)
        {
            var moveTo = new MenuItem { Header = $"Move {label} to" };
            foreach (var cat in realCategories)
            {
                var c = cat;
                moveTo.Items.Add(new MenuItem
                {
                    Header = c,
                    Command = new SimpleCommand(() => { foreach (var t in targets) Vm.AssignCategory(t, c); }),
                });
            }
            menu.Items.Add(moveTo);
        }

        // "Remove from category" only when at least one target is currently in one
        if (targets.Any(t => !string.IsNullOrEmpty(t.Model.Group)))
            menu.Items.Add(new MenuItem
            {
                Header = "Remove from category",
                Command = new SimpleCommand(() => { foreach (var t in targets) Vm.AssignCategory(t, MainViewModel.AllCategories); }),
            });

        if (menu.Items.Count == 0)
            menu.Items.Add(new MenuItem { Header = "No categories — use + to create one", IsEnabled = false });

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
