namespace MsixCore.Deployment;

/// <summary>
/// Progress and result surface for an asynchronous deployment operation.
/// The C# analogue of the native <c>IMsixResponse</c>, using events and cancellation instead of
/// a single callback pointer.
/// </summary>
public interface IMsixResponse
{
    /// <summary>Completion percentage in the range [0, 100].</summary>
    float Percentage { get; }

    /// <summary>The current coarse installation stage.</summary>
    InstallationStep Status { get; }

    /// <summary>Human-readable status/error text.</summary>
    string StatusText { get; }

    /// <summary>The failure, if <see cref="Status"/> is <see cref="InstallationStep.Error"/>; otherwise <see langword="null"/>.</summary>
    Exception? Failure { get; }

    /// <summary>
    /// A task that completes when the operation finishes (successfully, faulted, or cancelled).
    /// The response handle is returned to the caller immediately, so progress can be observed via
    /// <see cref="ProgressChanged"/> and the operation cancelled via <see cref="Cancel"/> while it runs.
    /// </summary>
    Task Completion { get; }

    /// <summary>Raised whenever <see cref="Percentage"/> or <see cref="Status"/> changes.</summary>
    event EventHandler<IMsixResponse>? ProgressChanged;

    /// <summary>Requests cancellation of the in-flight operation.</summary>
    void Cancel();
}
