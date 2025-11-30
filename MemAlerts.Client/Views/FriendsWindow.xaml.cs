using System.Windows;
using System.Windows.Input;
using MemAlerts.Client.ViewModels;

namespace MemAlerts.Client.Views;

public partial class FriendsWindow : Window
{
    public FriendViewModel ViewModel => (FriendViewModel)DataContext;

    public FriendsWindow(FriendViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += async (_, _) => await ViewModel.InitializeAsync();
        Closed += (_, _) => ViewModel.Dispose();
    }

    private void ColorZone_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}

