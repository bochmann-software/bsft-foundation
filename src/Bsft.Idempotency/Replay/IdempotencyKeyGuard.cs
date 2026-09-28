using Bsft.Core;

namespace Bsft.Idempotency.Replay;

/// <summary>
/// Checks a client-supplied key before it is used as a store key or written to a log.
/// </summary>
/// <remarks>
/// The key is attacker-reachable input that ends up in a database column and in an audit record, so
/// it is bounded and restricted to a character set rather than trusted. The set is the one every
/// realistic key already lives in — a GUID, a ULID, a request id — so the restriction costs a
/// well-behaved client nothing.
/// </remarks>
internal static class IdempotencyKeyGuard
{
    /// <summary>
    /// Throws when the key cannot be used.
    /// </summary>
    /// <param name="key">The value of the idempotency header.</param>
    /// <param name="maxLength">The longest key accepted.</param>
    /// <exception cref="ValidationException">
    /// Thrown when the key is too long or carries a character outside the accepted set. A 400 keyed
    /// on the header rather than a 500, because a malformed key is the client's mistake and the
    /// client can see the field name.
    /// </exception>
    internal static void Ensure(string key, int maxLength)
    {
        var failure = Describe(key, maxLength);

        if (failure is null)
            return;

        throw new ValidationException(
            new Dictionary<string, string[]> { [IdempotencyDefaults.KeyValidationField] = [failure] });
    }

    /// <summary>Reports what is wrong with the key, or nothing.</summary>
    /// <param name="key">The value of the idempotency header.</param>
    /// <param name="maxLength">The longest key accepted.</param>
    /// <returns>The message, or <see langword="null"/> when the key is usable.</returns>
    private static string? Describe(string key, int maxLength)
    {
        if (key.Length > maxLength)
            return $"The idempotency key must be at most {maxLength} characters.";

        return key.All(IsAccepted)
            ? null
            : "The idempotency key must consist of letters, digits, '-', '_', '.' and ':' only.";
    }

    /// <summary>Reports whether one character may appear in a key.</summary>
    /// <param name="character">The character.</param>
    /// <returns><see langword="true"/> when it is accepted.</returns>
    private static bool IsAccepted(char character) =>
        char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.' or ':';
}
