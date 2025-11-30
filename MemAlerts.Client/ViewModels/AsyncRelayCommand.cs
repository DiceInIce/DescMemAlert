using System;
using System.Threading.Tasks;
using System.Windows.Input;

namespace MemAlerts.Client.ViewModels;

public abstract class AsyncCommandBase : ICommand
{
    private bool _isExecuting;

    public event EventHandler? CanExecuteChanged;

    public virtual bool CanExecute(object? parameter) => !_isExecuting;

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
        {
            return;
        }

        _isExecuting = true;
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
            _isExecuting = false;
            RaiseCanExecuteChanged();
        }
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);

    protected abstract Task ExecuteAsync(object? parameter);
}

public sealed class AsyncRelayCommand : AsyncCommandBase
{
    private readonly Func<Task> _execute;
    private readonly Func<bool>? _canExecute;

    public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
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

    public AsyncRelayCommand(Func<T, Task> execute, Predicate<T>? canExecute = null)
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