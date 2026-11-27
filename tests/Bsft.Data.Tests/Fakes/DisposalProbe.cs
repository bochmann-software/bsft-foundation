namespace Bsft.Data.Tests.Fakes;

/// <summary>Tracks whether it was disposed.</summary>
internal sealed class DisposalProbe : IDisposable
{
    public bool IsDisposed { get; private set; }

    public void Dispose()
    {
        IsDisposed = true;
    }
}
