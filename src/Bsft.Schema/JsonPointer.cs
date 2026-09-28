namespace Bsft.Schema;

/// <summary>Builds the RFC 6901 pointers a refusal names.</summary>
/// <remarks>
/// A pointer rather than a dotted path because a property name may itself contain a dot, and a path
/// that cannot be parsed back is a path a form cannot highlight the right field from. Written once so
/// that the reader and the validator escape a name the same way.
/// </remarks>
internal static class JsonPointer
{
    /// <summary>Appends one segment.</summary>
    /// <param name="pointer">The pointer so far, empty for the document itself.</param>
    /// <param name="segment">The property name or array index to append.</param>
    /// <returns>The pointer.</returns>
    public static string Append(string pointer, string segment)
        => $"{pointer}/{segment.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal)}";
}
