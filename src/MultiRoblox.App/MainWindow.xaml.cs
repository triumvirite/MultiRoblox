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

    /// <summary>Centre the update dropdown horizontally under the button.</summary>
    private static CustomPopupPlacement[] CenterUnderTarget(Size popupSize, Size targetSize, Point offset)
        => new[]
        {
            new CustomPopupPlacement(
                new Point((targetSize.Width - popupSize.Width) / 2, targetSize.Height + offset.Y),
                PopupPrimaryAxis.Horizontal),
        };

    private MainViewModel Vm => (MainViewModel)DataContext;

    // ---------- account drag-to-reorder with live preview ----------

    private void AccountList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(null);
        _dragItem = ItemUnder(e.OriginalSource);
    }

    private void AccountList_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragItem is null) return;
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
        Vm.SelectedAccount = item;

        var menu = new ContextMenu();
        var addTo = new MenuItem { Header = "Add to category" };
        addTo.Items.Add(new MenuItem
        {
            Header = "Default",
            Command = new SimpleCommand(() => Vm.AssignCategory(item, MainViewModel.AllCategories)),
        });
        foreach (var cat in Vm.Categories)
        {
            if (cat == MainViewModel.AllCategories) continue;
            var c = cat;
            addTo.Items.Add(new MenuItem { Header = c, Command = new SimpleCommand(() => Vm.AssignCategory(item, c)) });
        }
        menu.Items.Add(addTo);
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
