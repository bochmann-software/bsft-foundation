namespace Bsft.Tests.Shared;

/// <summary>The pass counts <see cref="PollIntervalProbe"/> measured.</summary>
/// <param name="First">Passes once the loop has armed its first wait.</param>
/// <param name="BeforeInterval">Passes one tick before the interval has passed on the clock.</param>
/// <param name="AfterInterval">Passes once the interval has passed and the next wait is armed.</param>
internal sealed record PollIntervalCounts(int First, int BeforeInterval, int AfterInterval);
