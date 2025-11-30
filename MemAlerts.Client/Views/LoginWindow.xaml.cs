using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MemAlerts.Client.ViewModels;

namespace MemAlerts.Client.Views;

public partial class LoginWindow : Window
{
    private readonly LoginViewModel _viewModel;

    public LoginWindow(LoginViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        _viewModel = viewModel;

        viewModel.LoginSuccessful += OnLoginSuccessful;
        Closing += LoginWindow_Closing;
    }

    private void OnLoginSuccessful(object? sender, EventArgs e)
    {
        try
        {
            // Небольшая задержка перед закрытием, чтобы пользователь увидел сообщение об успехе
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(800)
            };
            timer.Tick += (s, args) =>
            {
                try
                {
                    timer.Stop();
                    if (IsLoaded && !IsClosing)
                    {
                        // Закрываем окно, но событие LoginSuccessful уже обработано в App.xaml.cs
                        DialogResult = true;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Ошибка при закрытии окна логина: {ex.Message}");
                }
            };
            timer.Start();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Ошибка в OnLoginSuccessful: {ex.Message}");
        }
    }

    private bool IsClosing { get; set; }

    private void LoginWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        IsClosing = true;
        // Отписываемся от события при закрытии
        try
        {
            if (_viewModel != null)
            {
                _viewModel.LoginSuccessful -= OnLoginSuccessful;
                _viewModel.Unsubscribe();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Ошибка при отписке от событий: {ex.Message}");
        }
    }

    private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is LoginViewModel vm && sender is PasswordBox passwordBox)
        {
            vm.Password = passwordBox.Password;
        }
    }

    private void Button_Click(object sender, RoutedEventArgs e)
    {

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
