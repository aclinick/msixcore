namespace MsixCore.PackageStore;

/// <summary>
/// An <see cref="IProgress{T}"/> that invokes its handler synchronously on the calling thread.
/// Unlike <see cref="Progress{T}"/>, it does not marshal to a captured <see cref="SynchronizationContext"/>
/// or post to the thread pool, so progress callbacks are delivered in order and complete before the
/// next reporting call returns. The deployment engine already runs on a background task, so ordered,
/// inline delivery is exactly what is wanted.
/// </summary>
internal sealed class SynchronousProgress<T> : IProgress<T>
{
    private readonly Action<T> _handler;

    public SynchronousProgress(Action<T> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _handler = handler;
    }

    public void Report(T value) => _handler(value);
}
