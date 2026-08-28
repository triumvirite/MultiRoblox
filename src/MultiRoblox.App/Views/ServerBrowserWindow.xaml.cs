using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MultiRoblox.App.ViewModels;
using MultiRoblox.Core.Models;
using MultiRoblox.Core.Services;

namespace MultiRoblox.App.Views;

public partial class ServerBrowserWindow : Window
{
    private readonly ServerBrowserViewModel _vm;

    public ServerBrowserWindow(AppServices svc, Account account, long placeId, MainViewModel main)
    {
        _vm = new ServerBrowserViewModel(svc, account, placeId, main);
        DataContext = _vm;
        InitializeComponent();
    }

    private void Game_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: GameSummary g })
            _vm.UseGameCommand.Execute(g);
    }
}
