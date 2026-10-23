namespace Bsft.Logging.Sinks;

/// <summary>
/// Writes the scope parts of a log line in the format of the BSFT file logger, so that another sink (the console
/// formatter of <c>Bsft.Hosting</c>) shows the same line as the file and the same reader could parse it.
/// </summary>
public interface IScopeLineEncoder
{
    /// <summary>Makes a correlation id safe for the bracketed slot of a line.</summary>
    /// <param name="correlationId">The id as the scope carried it.</param>
    /// <returns>The text to write between the brackets: line breaks replaced, a backslash and <c>]</c> escaped.</returns>
    string EscapeSlot(string correlationId);

    /// <summary>Renders the scope properties other than the correlation id as the <c>{Key=value;Key=value}</c> block.</summary>
    /// <param name="properties">The properties, already free of line breaks.</param>
    /// <returns>The block with a leading space, or an empty string when there are none.</returns>
    string ToBlock(IReadOnlyList<KeyValuePair<string, string>> properties);

    /// <summary>Finds what makes a list of persisted scope keys unusable, by the rules the file logger applies.</summary>
    /// <param name="keys">The configured keys.</param>
    /// <returns>A description of the problem, or null when the list is usable.</returns>
    string? DescribeKeyProblem(IEnumerable<string>? keys);
}
