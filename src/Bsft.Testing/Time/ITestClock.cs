namespace Bsft.Testing.Time;

/// <summary>
/// A clock a test moves by hand, so code that reads the current time can be driven deterministically
/// instead of waiting for wall-clock time to pass.
/// </summary>
public interface ITestClock
{
    /// <summary>Gets the instant the clock currently reports.</summary>
    DateTimeOffset UtcNow { get; }

    /// <summary>Moves the clock forward.</summary>
    /// <param name="by">How far to move forward. Must not be negative.</param>
    void Advance(TimeSpan by);

    /// <summary>Sets the clock to an absolute instant, forward or backward.</summary>
    /// <param name="utcNow">The instant the clock is set to.</param>
    void SetUtcNow(DateTimeOffset utcNow);
}
