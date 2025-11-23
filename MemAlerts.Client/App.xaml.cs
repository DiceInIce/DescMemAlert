using System.IO;
using System.Text.Json;
using System.Windows;
using global::MemAlerts.Shared.Models;
using MemAlerts.Client.Alerts;
using MemAlerts.Client.Networking;
using MemAlerts.Client.Services;
using MemAlerts.Client.ViewModels;
using MemAlerts.Client.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MemAlerts.Client;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Prevent shutdown when LoginWindow closes
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var builder = Host.CreateApplicationBuilder();

        // Configuration
        builder.Services.AddSingleton<AppConfig>(provider => 
        {
            var config = new AppConfig();
            try
            {
                if (File.Exists("config.json"))
                {
                    var json = File.ReadAllText("config.json");
                    var loadedConfig = JsonSerializer.Deserialize<AppConfig>(json);
                    if (loadedConfig != null) config = loadedConfig;
                }
            }
            catch { /* Use defaults */ }
            return config;
        });

        // Services
        builder.Services.AddSingleton<IMemAlertService, MockMemAlertService>();
        builder.Services.AddSingleton<AlertOverlayManager>();
        builder.Services.AddSingleton<PeerMessenger>();

        // ViewModels
        builder.Services.AddSingleton<MainViewModel>();
        builder.Services.AddTransient<LoginViewModel>();

        // Windows
        builder.Services.AddSingleton<MainWindow>();
        builder.Services.AddTransient<LoginWindow>();

        _host = builder.Build();
        await _host.StartAsync();

        var messenger = _host.Services.GetRequiredService<PeerMessenger>();
        var config = _host.Services.GetRequiredService<AppConfig>();
        var mainViewModel = _host.Services.GetRequiredService<MainViewModel>();

        // Initialize ViewModel with config
        mainViewModel.ServerAddress = config.ServerIp;
        mainViewModel.ServerPort = config.ServerPort;

        // Connect
        try
        {
            await messenger.ConnectAsync(config.ServerIp, config.ServerPort);
        }
        catch
        {
            MessageBox.Show("Не удалось подключиться к серверу. Убедитесь, что сервер запущен.", 
                "Ошибка подключения", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }

        // Login Flow
        var loginWindow = _host.Services.GetRequiredService<LoginWindow>();
        var dialogResult = loginWindow.ShowDialog();

        if (dialogResult != true || !messenger.IsAuthenticated)
        {
            messenger.Dispose();
            Shutdown();
            return;
        }

        // Main Window
        try
        {
            var mainWindow = _host.Services.GetRequiredService<MainWindow>();
            MainWindow = mainWindow;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            
            mainWindow.Visibility = Visibility.Visible;
            mainWindow.Show();
            mainWindow.Activate();
            
            try
            {
                _ = mainViewModel.InitializeAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при инициализации: {ex.Message}", 
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка при показе главного окна: {ex.Message}", 
                "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            messenger.Dispose();
            Shutdown();
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host != null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }
        base.OnExit(e);
    }

    private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"Необработанное исключение: {e.Exception}");
        MessageBox.Show($"Произошла ошибка: {e.Exception.Message}\n\nДетали: {e.Exception}", 
            "Ошибка приложения", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}

