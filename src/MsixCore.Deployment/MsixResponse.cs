namespace MsixCore.Deployment;

/// <summary>
/// Mutable <see cref="IMsixResponse"/> returned immediately from an add/remove operation. The
/// deployment engine drives it forward via <see cref="Report"/> / <see cref="Complete"/> /
/// <see cref="Fail"/>; callers observe progress through <see cref="IMsixResponse.ProgressChanged"/>
/// and await <see cref="IMsixResponse.Completion"/>.
/// </summary>
internal sealed class MsixResponse : IMsixResponse, IDisposable
{
    private readonly TaskCompletionSource _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly CancellationTokenSource _cts;
    private readonly object _gate = new();
    private bool _terminal;

    public MsixResponse(CancellationToken externalCancellation)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(externalCancellation);
    }

    public float Percentage { get; private set; }

    public InstallationStep Status { get; private set; } = InstallationStep.Unknown;

    public string StatusText { get; private set; } = string.Empty;

    public Exception? Failure { get; private set; }

    public Task Completion => _completion.Task;

    /// <summary>The token the engine honors for cooperative cancellation.</summary>
    public CancellationToken Token => _cts.Token;

    public event EventHandler<IMsixResponse>? ProgressChanged;

    public void Cancel() => _cts.Cancel();

    /// <summary>Updates the coarse stage, percentage, and status text and raises a progress event.</summary>
    public void Report(InstallationStep step, float percentage, string statusText)
    {
        lock (_gate)
        {
            // Ignore progress after a terminal transition so a late (e.g. asynchronously-posted)
            // update can never move a completed/failed response back to an in-progress state.
            if (_terminal)
            {
                return;
            }

            Status = step;
            Percentage = Math.Clamp(percentage, 0f, 100f);
            StatusText = statusText;
        }

        RaiseProgress();
    }

    /// <summary>Marks the operation as successfully completed.</summary>
    public void Complete()
    {
        lock (_gate)
        {
            if (_terminal)
            {
                return;
            }

            _terminal = true;
            Status = InstallationStep.Completed;
            Percentage = 100f;
            StatusText = "Completed.";
        }

        // Settle the completion source before notifying so a throwing subscriber can never leave
        // Completion permanently pending.
        _completion.TrySetResult();
        RaiseProgress();
    }

    /// <summary>Marks the operation as failed (or cancelled) and completes the task accordingly.</summary>
    public void Fail(Exception failure)
    {
        lock (_gate)
        {
            if (_terminal)
            {
                return;
            }

            _terminal = true;
            Failure = failure;
            Status = InstallationStep.Error;
            StatusText = failure.Message;
        }

        if (failure is OperationCanceledException)
        {
            _completion.TrySetCanceled(_cts.Token);
        }
        else
        {
            _completion.TrySetException(failure);
        }

        RaiseProgress();
    }

    private void RaiseProgress()
    {
        try
        {
            ProgressChanged?.Invoke(this, this);
        }
        catch
        {
            // Observer faults must not disrupt the deployment engine or strand the completion task.
        }
    }

    public void Dispose() => _cts.Dispose();
}
