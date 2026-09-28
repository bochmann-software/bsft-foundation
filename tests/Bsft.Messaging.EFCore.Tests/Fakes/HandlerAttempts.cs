namespace Bsft.Messaging.EFCore.Tests.Fakes;

/// <summary>Counts how often a handler was reached, across the deliveries of one test.</summary>
public sealed class HandlerAttempts
{
    public int Count { get; private set; }

    public int Next() => ++Count;
}
