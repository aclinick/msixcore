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
        Status = step;
        Percentage = Math.Clamp(percentage, 0f, 100f);
        StatusText = statusText;
        ProgressChanged?.Invoke(this, this);
    }

    /// <summary>Marks the operation as successfully completed.</summary>
    public void Complete()
    {
        Report(InstallationStep.Completed, 100f, "Completed.");
        _completion.TrySetResult();
    }

    /// <summary>Marks the operation as failed (or cancelled) and completes the task accordingly.</summary>
    public void Fail(Exception failure)
    {
        Failure = failure;
        Status = InstallationStep.Error;
        StatusText = failure.Message;
        ProgressChanged?.Invoke(this, this);

        if (failure is OperationCanceledException)
        {
            _completion.TrySetCanceled(_cts.Token);
        }
        else
        {
            _completion.TrySetException(failure);
        }
    }

    public void Dispose() => _cts.Dispose();
}
