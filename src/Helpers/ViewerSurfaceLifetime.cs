namespace Illustra.Helpers;

/// <summary>Separates a temporary WPF rehost unload from the surface's final disposal.</summary>
public sealed class ViewerSurfaceLifetime
{
    private bool _transferPending;
    private bool _disposed;

    public bool IsTransferPending => _transferPending;
    public bool IsDisposed => _disposed;

    public void BeginHostTransfer()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _transferPending = true;
    }

    public void OnUnloaded(Action finalCleanup)
    {
        if (_disposed || _transferPending) return;
        Dispose(finalCleanup);
    }

    public void OnLoaded(Action restoreAfterTransfer)
    {
        if (_disposed || !_transferPending) return;
        _transferPending = false;
        restoreAfterTransfer();
    }

    public void Dispose(Action finalCleanup)
    {
        if (_disposed) return;
        _disposed = true;
        _transferPending = false;
        finalCleanup();
    }
}
