using System.Text.RegularExpressions;

namespace Bsft.Mediator;

/// <summary>
/// Decides whether a correlation ID is well-formed enough to be recorded.
/// </summary>
/// <remarks>
/// <c>ICorrelationIdAccessor</c> is a public seam, so the value need not have passed through
/// the middleware that sanitizes an incoming header. Anything recorded from it is therefore checked
/// here first: a value carrying a line break would otherwise let a caller write its own lines into
/// the log, and one of unbounded length would let it flood a span tag. The rule mirrors the one in
/// CorrelationIdMiddleware. The end anchor is <c>\z</c> rather than <c>$</c> for the same reason the
/// check is there at all: <c>$</c> also matches just before a single trailing newline, which is
/// precisely the character a line-based sink must not be handed.
/// </remarks>
internal static partial class CorrelationIds
{
    /// <summary>The placeholder recorded in place of an absent or malformed correlation ID.</summary>
    internal const string Unknown = "-";

    /// <summary>Returns the correlation ID when it is well-formed, and the placeholder otherwise.</summary>
    /// <param name="correlationId">The correlation ID as the accessor reported it.</param>
    /// <returns>The value to record.</returns>
    internal static string Safe(string? correlationId)
        => IsWellFormed(correlationId) ? correlationId! : Unknown;

    /// <summary>Reports whether a correlation ID is well-formed.</summary>
    /// <param name="correlationId">The correlation ID as the accessor reported it.</param>
    /// <returns><see langword="true"/> when the value may be recorded as it stands.</returns>
    internal static bool IsWellFormed(string? correlationId)
        => !string.IsNullOrEmpty(correlationId) && WellFormed().IsMatch(correlationId);

    [GeneratedRegex(@"^[A-Za-z0-9._-]{1,128}\z", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex WellFormed();
}
