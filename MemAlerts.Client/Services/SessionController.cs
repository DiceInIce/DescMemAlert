using System;
using System.Threading.Tasks;
using System.Windows;
using MemAlerts.Client.Networking;
using MemAlerts.Client.ViewModels;
using MemAlerts.Client.Views;
using Microsoft.Extensions.DependencyInjection;

namespace MemAlerts.Client.Services;

public sealed record ReloginResult(bool Success, string? ErrorMessage, bool IsBusy)
{
    public static ReloginResult Busy => new(false, "Операция уже выполняется", true);
}

/// <summary>
/// Отвечает за переавторизацию пользователя без выхода из приложения.
/// </summary>
public sealed class SessionController
{
    private readonly IServiceProvider _serviceProvider;
    private readonly PeerMessenger _messenger;
    private bool _isInProgress;

    public SessionController(IServiceProvider serviceProvider, PeerMessenger messenger)
    {
        _serviceProvider = serviceProvider;
        _messenger = messenger;
    }

    public async Task<ReloginResult> ReloginAsync(Window owner, MainViewModel viewModel)
    {
        if (_isInProgress)
        {
            return ReloginResult.Busy;
        }

        _isInProgress = true;

        try
        {
            try
            {
                _messenger.Disconnect();
                await _messenger.ConnectAsync(viewModel.ServerAddress, viewModel.ServerPort);
            }
            catch (Exception ex)
            {
                return new ReloginResult(false, $"Не удалось подключиться к серверу: {ex.Message}", false);
            }

            owner.Hide();

            var loginWindow = _serviceProvider.GetRequiredService<LoginWindow>();
            loginWindow.Owner = owner;
            var dialogResult = loginWindow.ShowDialog();
            var loginSucceeded = dialogResult == true && _messenger.IsAuthenticated;

            owner.Show();

            if (loginSucceeded)
            {
                owner.Activate();
                await viewModel.InitializeAsync();
                return new ReloginResult(true, null, false);
            }

            return new ReloginResult(false, null, false);
        }
        finally
        {
            _isInProgress = false;
        }
    }
}
