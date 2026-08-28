using System.Windows;
using MultiRoblox.App.ViewModels;
using MultiRoblox.Core.Models;

namespace MultiRoblox.App.Views;

public partial class UtilitiesWindow : Window
{
    public UtilitiesWindow(AppServices svc, Account account, MainViewModel main)
    {
        DataContext = new UtilitiesViewModel(svc, account, main);
        InitializeComponent();
        Loaded += (_, _) => ((UtilitiesViewModel)DataContext).LoadCommand.Execute(null);
    }
}
