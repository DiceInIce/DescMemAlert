using System;
using System.Threading.Tasks;
using System.Windows.Input;

namespace MemAlerts.Client.ViewModels;

public abstract class AsyncCommandBase : ICommand
{
    private bool _isExecuting;
    private readonly Func<bool>? _isBusyGetter;
    private readonly Action<bool>? _isBusySetter;

    public event EventHandler? CanExecuteChanged;

    protected AsyncCommandBase(Func<bool>? isBusyGetter = null, Action<bool>? isBusySetter = null)
    {
        _isBusyGetter = isBusyGetter;
        _isBusySetter = isBusySetter;
    }

    public virtual bool CanExecute(object? parameter) =>
        !_isExecuting &&
        (_isBusyGetter?.Invoke() != true);

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
        {
            return;
        }

        SetBusyState(true);
        RaiseCanExecuteChanged();

        try
        {
            await ExecuteAsync(parameter);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"{GetType().Name} error: {ex}");
        }
        finally
        {
            SetBusyState(false);
            RaiseCanExecuteChanged();
        }
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);

    protected abstract Task ExecuteAsync(object? parameter);

    private void SetBusyState(bool value)
    {
        _isExecuting = value;
        _isBusySetter?.Invoke(value);
    }
}

public sealed class AsyncRelayCommand : AsyncCommandBase
{
    private readonly Func<Task> _execute;
    private readonly Func<bool>? _canExecute;

    public AsyncRelayCommand(
        Func<Task> execute,
        Func<bool>? canExecute = null,
        Func<bool>? isBusyGetter = null,
        Action<bool>? isBusySetter = null)
        : base(isBusyGetter, isBusySetter)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public override bool CanExecute(object? parameter)
    {
        return base.CanExecute(parameter) && (_canExecute?.Invoke() ?? true);
    }

    protected override Task ExecuteAsync(object? parameter)
    {
        return _execute();
    }
}

public sealed class AsyncRelayCommand<T> : AsyncCommandBase
{
    private readonly Func<T, Task> _execute;
    private readonly Predicate<T>? _canExecute;

    public AsyncRelayCommand(
        Func<T, Task> execute,
        Predicate<T>? canExecute = null,
        Func<bool>? isBusyGetter = null,
        Action<bool>? isBusySetter = null)
        : base(isBusyGetter, isBusySetter)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public override bool CanExecute(object? parameter)
    {
        if (!base.CanExecute(parameter))
        {
            return false;
        }

        if (parameter is T t)
        {
            return _canExecute?.Invoke(t) ?? true;
        }

        return parameter == null && typeof(T).IsClass && (_canExecute == null || _canExecute(default!));
    }

    protected override Task ExecuteAsync(object? parameter)
    {
        if (parameter is T t)
        {
            return _execute(t);
        }
        
        if (parameter == null && typeof(T).IsClass)
        {
            return _execute(default!);
        }

        return Task.CompletedTask;
    }
}