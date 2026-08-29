using System.Windows;
using MultiRoblox.App.ViewModels;
using MultiRoblox.Core.Models;

namespace MultiRoblox.App.Views;

public partial class UtilitiesWindow : Window
{
    public UtilitiesWindow(AppServices svc, IReadOnlyList<Account> accounts, MainViewModel main)
    {
        DataContext = new UtilitiesViewModel(svc, accounts, main);
        InitializeComponent();
        Loaded += (_, _) => ((UtilitiesViewModel)DataContext).LoadCommand.Execute(null);
    }
}
