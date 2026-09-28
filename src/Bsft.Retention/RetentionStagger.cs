namespace Bsft.Retention;

/// <summary>
/// Works out the fixed startup offset of a retention pass.
/// </summary>
/// <remarks>
/// The offset is a hash of the pass name rather than a random draw, because the property that matters
/// is that two passes differ, not that either is unpredictable. A hash also gives the same answer on
/// every instance and across restarts, so a deployment's sweeps stay where an operator last saw them
/// instead of reshuffling on every deploy, and a test can assert the offset rather than tolerate it.
/// </remarks>
internal static class RetentionStagger
{
    /// <summary>The FNV-1a 32-bit offset basis.</summary>
    private const uint OffsetBasis = 2166136261;

    /// <summary>The FNV-1a 32-bit prime.</summary>
    private const uint Prime = 16777619;

    /// <summary>
    /// The offset this pass waits before its first sweep.
    /// </summary>
    /// <param name="passName">The name the offset is derived from.</param>
    /// <param name="maxStagger">
    /// The window the offset falls in. Zero — or anything under a millisecond — means no offset.
    /// </param>
    /// <returns>An offset in <c>[0, maxStagger)</c>, the same one for the same name every time.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="passName"/> is null.</exception>
    /// <remarks>
    /// The offset is a whole number of milliseconds because that is the resolution a delay actually
    /// has: a timer created from a <see cref="TimeProvider"/> rounds to milliseconds, so an offset
    /// carrying ticks below that would be a precision the wait cannot honour — and one a test could
    /// not assert against what the clock was asked for.
    /// </remarks>
    public static TimeSpan OffsetFor(string passName, TimeSpan maxStagger)
    {
        if (passName is null)
            throw new ArgumentNullException(nameof(passName));

        // A non-positive window is "start every pass at once", not an error: it is the documented way
        // to turn the stagger off, and a negative one is already rejected by the validator.
        var windowMilliseconds = (long)maxStagger.TotalMilliseconds;

        if (windowMilliseconds <= 0)
            return TimeSpan.Zero;

        var hash = OffsetBasis;

        foreach (var character in passName)
        {
            hash ^= character;
            hash *= Prime;
        }

        return TimeSpan.FromMilliseconds(hash % (ulong)windowMilliseconds);
    }
}
