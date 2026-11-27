namespace Bsft.Mediator.Tests;

/// <summary>A scoped value a seed carries into a fresh scope, which says when its scope ended.</summary>
public sealed class ScopeMarker : IDisposable
{
    public string Name { get; set; } = "fresh";

    public bool IsDisposed { get; private set; }

    public void Dispose() => IsDisposed = true;
}
