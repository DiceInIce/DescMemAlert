using System;
using System.Threading.Tasks;
using System.Windows;
using MemAlerts.Client.Networking;
using global::MemAlerts.Shared.Models;

namespace MemAlerts.Client.ViewModels;

public sealed class LoginViewModel : ObservableObject
{
    private readonly PeerMessenger _messenger;
    private string _login = string.Empty;
    private string _email = string.Empty;
    private string _password = string.Empty;
    private string _errorMessage = string.Empty;
    private bool _hasError;
    private bool _isBusy;
    private string _statusMessage = string.Empty;

    public event EventHandler? LoginSuccessful;

    public LoginViewModel(PeerMessenger messenger)
    {
        _messenger = messenger;
        _messenger.AuthResponseReceived += OnAuthResponseReceived;

        LoginCommand = new AsyncRelayCommand(LoginAsync, () => !IsBusy && CanLogin, () => IsBusy, v => IsBusy = v);
        RegisterCommand = new AsyncRelayCommand(RegisterAsync, () => !IsBusy && CanRegister, () => IsBusy, v => IsBusy = v);
    }

    public void Unsubscribe()
    {
        if (_messenger != null)
        {
            _messenger.AuthResponseReceived -= OnAuthResponseReceived;
        }
    }

    public AsyncRelayCommand LoginCommand { get; }
    public AsyncRelayCommand RegisterCommand { get; }

    public string Login
    {
        get => _login;
        set
        {
            if (SetProperty(ref _login, value))
            {
                LoginCommand.RaiseCanExecuteChanged();
                RegisterCommand.RaiseCanExecuteChanged();
                ClearError();
            }
        }
    }

    public string Email
    {
        get => _email;
        set
        {
            if (SetProperty(ref _email, value))
            {
                LoginCommand.RaiseCanExecuteChanged();
                RegisterCommand.RaiseCanExecuteChanged();
                ClearError();
            }
        }
    }

    public string Password
    {
        get => _password;
        set
        {
            if (SetProperty(ref _password, value))
            {
                LoginCommand.RaiseCanExecuteChanged();
                RegisterCommand.RaiseCanExecuteChanged();
                ClearError();
            }
        }
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            SetProperty(ref _errorMessage, value);
            HasError = !string.IsNullOrWhiteSpace(value);
        }
    }

    public bool HasError
    {
        get => _hasError;
        private set => SetProperty(ref _hasError, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                LoginCommand.RaiseCanExecuteChanged();
                RegisterCommand.RaiseCanExecuteChanged();
                RaisePropertyChanged(nameof(IsNotBusy));
            }
        }
    }

    public bool IsNotBusy => !IsBusy;

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    private bool CanLogin => !string.IsNullOrWhiteSpace(Login) && !string.IsNullOrWhiteSpace(Password) && Password.Length >= 6;
    
    private bool CanRegister => !string.IsNullOrWhiteSpace(Login) && !string.IsNullOrWhiteSpace(Email) && !string.IsNullOrWhiteSpace(Password) && Password.Length >= 6;

    private async Task LoginAsync()
    {
        if (!_messenger.IsConnected)
        {
            ErrorMessage = "Нет подключения к серверу";
            return;
        }

        IsBusy = true;
        StatusMessage = "Вход...";
        ClearError();

        try
        {
            // Login может быть либо логином, либо email
            await _messenger.LoginAsync(Login, Password);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Ошибка входа: {ex.Message}";
            StatusMessage = string.Empty;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RegisterAsync()
    {
        if (!_messenger.IsConnected)
        {
            ErrorMessage = "Нет подключения к серверу";
            return;
        }

        IsBusy = true;
        StatusMessage = "Регистрация...";
        ClearError();

        try
        {
            await _messenger.RegisterAsync(Login, Email, Password);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Ошибка регистрации: {ex.Message}";
            StatusMessage = string.Empty;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void OnAuthResponseReceived(object? sender, AuthResponse response)
    {
        try
        {
            if (Application.Current?.Dispatcher == null)
            {
                return;
            }

            Application.Current.Dispatcher.Invoke(() =>
            {
                try
                {
                    if (response.Success)
                    {
                        StatusMessage = $"Успешно! Добро пожаловать, {response.UserLogin ?? response.UserEmail}";
                        LoginSuccessful?.Invoke(this, EventArgs.Empty);
                    }
                    else
                    {
                        ErrorMessage = response.ErrorMessage ?? "Ошибка авторизации";
                        StatusMessage = string.Empty;
                        IsBusy = false;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Ошибка в обработке ответа авторизации: {ex.Message}");
                    ErrorMessage = "Ошибка при обработке ответа сервера";
                    IsBusy = false;
                }
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Ошибка в OnAuthResponseReceived: {ex.Message}");
        }
    }

    private void ClearError()
    {
        if (HasError)
        {
            ErrorMessage = string.Empty;
        }
    }
}

