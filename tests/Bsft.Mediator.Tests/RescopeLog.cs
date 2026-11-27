namespace Bsft.Mediator.Tests;

/// <summary>What the handlers and the probe behavior of one rescoping test saw.</summary>
public sealed class RescopeLog
{
    public List<string> Handled { get; } = [];

    public List<ScopeMarker> AttemptMarkers { get; } = [];
}
