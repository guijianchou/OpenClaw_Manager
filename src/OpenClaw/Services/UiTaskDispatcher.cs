// Copyright (c) Lanstack @openclaw. All rights reserved.

namespace OpenClaw.Services;

internal sealed class UiTaskDispatcher
{
    private readonly Func<Action, bool> _dispatch;

    public UiTaskDispatcher(Func<Action, bool> dispatch)
    {
        _dispatch = dispatch ?? throw new ArgumentNullException(nameof(dispatch));
    }

    public Task RunAsync(Action action)
    {
        var completion = CreateCompletion();
        Dispatch(() =>
        {
            try
            {
                action();
                completion.TrySetResult();
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        }, completion);

        return completion.Task;
    }

    public Task RunAsync(Action action, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled(cancellationToken);
        }

        var completion = CreateCompletion();
        var registration = cancellationToken.Register(
            () => completion.TrySetCanceled(cancellationToken));

        Dispatch(() =>
        {
            if (cancellationToken.IsCancellationRequested)
            {
                completion.TrySetCanceled(cancellationToken);
                return;
            }

            try
            {
                action();
                completion.TrySetResult();
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        }, completion);

        return DisposeCancellationRegistrationAsync(completion.Task, registration);
    }

    public Task<T> RunAsync<T>(Func<T> action)
    {
        var completion = CreateCompletion<T>();
        Dispatch(() =>
        {
            try
            {
                completion.TrySetResult(action());
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        }, completion);

        return completion.Task;
    }

    public Task<T> RunAsync<T>(Func<T> action, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<T>(cancellationToken);
        }

        var completion = CreateCompletion<T>();
        var registration = cancellationToken.Register(
            () => completion.TrySetCanceled(cancellationToken));

        Dispatch(() =>
        {
            if (cancellationToken.IsCancellationRequested)
            {
                completion.TrySetCanceled(cancellationToken);
                return;
            }

            try
            {
                completion.TrySetResult(action());
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        }, completion);

        return DisposeCancellationRegistrationAsync(completion.Task, registration);
    }

    public Task RunAsync(Func<Task> action)
    {
        var completion = CreateCompletion();
        Dispatch(() => _ = CompleteAsync(action, completion), completion);

        return completion.Task;
    }

    public Task<T> RunAsync<T>(Func<Task<T>> action)
    {
        var completion = CreateCompletion<T>();
        Dispatch(() => _ = CompleteAsync(action, completion), completion);

        return completion.Task;
    }

    public Task<T> RunAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<T>(cancellationToken);
        }

        var completion = CreateCompletion<T>();
        var registration = cancellationToken.Register(
            () => completion.TrySetCanceled(cancellationToken));

        Dispatch(() =>
        {
            if (cancellationToken.IsCancellationRequested)
            {
                completion.TrySetCanceled(cancellationToken);
                return;
            }

            _ = CompleteAsync(action, completion);
        }, completion);

        return DisposeCancellationRegistrationAsync(completion.Task, registration);
    }

    private static TaskCompletionSource CreateCompletion()
    {
        return new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private static TaskCompletionSource<T> CreateCompletion<T>()
    {
        return new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private void Dispatch(Action action, TaskCompletionSource completion)
    {
        try
        {
            if (!_dispatch(action))
            {
                completion.TrySetException(CreateUnavailableDispatcherException());
            }
        }
        catch (Exception ex)
        {
            completion.TrySetException(ex);
        }
    }

    private void Dispatch<T>(Action action, TaskCompletionSource<T> completion)
    {
        try
        {
            if (!_dispatch(action))
            {
                completion.TrySetException(CreateUnavailableDispatcherException());
            }
        }
        catch (Exception ex)
        {
            completion.TrySetException(ex);
        }
    }

    private static InvalidOperationException CreateUnavailableDispatcherException()
    {
        return new InvalidOperationException("UI dispatcher is unavailable.");
    }

    private static async Task CompleteAsync(Func<Task> action, TaskCompletionSource completion)
    {
        try
        {
            await action();
            completion.TrySetResult();
        }
        catch (OperationCanceledException ex)
        {
            completion.TrySetCanceled(ex.CancellationToken);
        }
        catch (Exception ex)
        {
            completion.TrySetException(ex);
        }
    }

    private static async Task CompleteAsync<T>(Func<Task<T>> action, TaskCompletionSource<T> completion)
    {
        try
        {
            completion.TrySetResult(await action());
        }
        catch (OperationCanceledException ex)
        {
            completion.TrySetCanceled(ex.CancellationToken);
        }
        catch (Exception ex)
        {
            completion.TrySetException(ex);
        }
    }

    private static async Task<T> DisposeCancellationRegistrationAsync<T>(
        Task<T> task,
        CancellationTokenRegistration registration)
    {
        try
        {
            return await task.ConfigureAwait(false);
        }
        finally
        {
            registration.Dispose();
        }
    }

    private static async Task DisposeCancellationRegistrationAsync(
        Task task,
        CancellationTokenRegistration registration)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        finally
        {
            registration.Dispose();
        }
    }
}
