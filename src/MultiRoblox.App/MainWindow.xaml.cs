using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MultiRoblox.App.ViewModels;

namespace MultiRoblox.App;

public partial class MainWindow : Window
{
    private Point _dragStart;
    private AccountItemViewModel? _dragItem;

    public MainWindow()
    {
        InitializeComponent();
    }

    private void AccountList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(null);
        _dragItem = (e.OriginalSource as FrameworkElement)?.DataContext as AccountItemViewModel;
    }

    private void AccountList_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragItem is null) return;
        var diff = _dragStart - e.GetPosition(null);
        if (Math.Abs(diff.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(diff.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        DragDrop.DoDragDrop(AccountList, _dragItem, DragDropEffects.Move);
    }

    private void AccountList_Drop(object sender, DragEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        if (e.Data.GetData(typeof(AccountItemViewModel)) is not AccountItemViewModel dragged) return;
        var target = (e.OriginalSource as FrameworkElement)?.DataContext as AccountItemViewModel;

        int from = vm.Accounts.IndexOf(dragged);
        int to = target is null ? vm.Accounts.Count - 1 : vm.Accounts.IndexOf(target);
        if (from < 0 || to < 0 || from == to) return;

        vm.Accounts.Move(from, to);
        vm.ReorderFromView(vm.Accounts.Select(a => a.Id).ToList());
        _dragItem = null;
    }
}
