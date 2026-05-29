// Copyright (c) Lanstack @openclaw. All rights reserved.

using System.Windows.Input;

namespace OpenClaw.Helpers;

/// <summary>
/// A simple <see cref="ICommand"/> implementation for binding.
/// </summary>
public class SimpleCommand : ICommand
{
    private readonly Action _action;

    public SimpleCommand(Action action)
    {
        _action = action ?? throw new ArgumentNullException(nameof(action));
    }

#pragma warning disable CS0067
    public event EventHandler? CanExecuteChanged;
#pragma warning restore CS0067

    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter) => _action();
}

/// <summary>
/// A simple async <see cref="ICommand"/> implementation that safely observes failures.
/// </summary>
public sealed class AsyncCommand : ICommand
{
    private readonly Func<Task> _action;
    private readonly Action<Exception>? _errorHandler;
    private int _isExecuting;

    public AsyncCommand(Func<Task> action, Action<Exception>? errorHandler = null)
    {
        _action = action ?? throw new ArgumentNullException(nameof(action));
        _errorHandler = errorHandler;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => Volatile.Read(ref _isExecuting) == 0;

    public void Execute(object? parameter)
    {
        if (Interlocked.CompareExchange(ref _isExecuting, 1, 0) != 0)
        {
            return;
        }

        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        Task task;
        try
        {
            task = _action() ?? Task.CompletedTask;
        }
        catch (Exception ex)
        {
            ResetExecuting();
            _errorHandler?.Invoke(ex);
            return;
        }

        Observe(task);
    }

    private async void Observe(Task task)
    {
        try
        {
            await task;
        }
        catch (Exception ex)
        {
            _errorHandler?.Invoke(ex);
        }
        finally
        {
            ResetExecuting();
        }
    }

    private void ResetExecuting()
    {
        Interlocked.Exchange(ref _isExecuting, 0);
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
