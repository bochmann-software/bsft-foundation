using System.Text;

namespace Bsft.Logging;

/// <summary>
/// The text form of the correlation slot and of the scope property block, <c>{Key=value;Key=value}</c>, in one
/// place for the file logger and the reader. Other sinks reach the write half through
/// <see cref="Sinks.IScopeLineEncoder"/>.
/// </summary>
/// <remarks>
/// A backslash escapes the next character. The writer escapes the backslash, <c>;</c>, <c>=</c> and <c>}</c> in the
/// keys and values of the block, so a block ends at the first unescaped <c>}</c>, a pair at the first unescaped
/// <c>;</c> and a key at the first unescaped <c>=</c>. The correlation slot, which ends at the first unescaped
/// <c>]</c>, escapes the backslash and <c>]</c> the same way, so a correlation id cannot end the slot early and
/// pass the rest of itself off as a block.
/// </remarks>
internal static class ScopePropertyEncoding
{
    /// <summary>
    /// The reserved key of the marker property a writer adds to a line whose scope properties did not fit the budget,
    /// so a reader knows that a property it does not find may have been dropped. Its value is <see cref="TruncatedValue"/>.
    /// </summary>
    internal const string TruncatedKey = "~truncated";

    /// <summary>The only value of the marker property that counts as a truncation.</summary>
    internal const string TruncatedValue = "1";

    private const char Escape = (char)92;

    private const int SlotEscapeHeadroom = 4;

    /// <summary>Makes a correlation id safe for the slot: line breaks become a space, and the backslash and <c>]</c> are escaped.</summary>
    /// <param name="correlationId">The id as the scope carried it.</param>
    /// <returns>The text to write between the brackets; the id itself when it needs no change.</returns>
    internal static string EscapeSlot(string correlationId)
    {
        ArgumentNullException.ThrowIfNull(correlationId);

        var flat = correlationId.ReplaceLineEndings(" ");
        if (flat.AsSpan().IndexOfAny(Escape, ']') < 0)
            return flat;

        var builder = new StringBuilder(flat.Length + SlotEscapeHeadroom);
        foreach (var character in flat)
        {
            if (character is Escape or ']')
                builder.Append(Escape);

            builder.Append(character);
        }

        return builder.ToString();
    }

    /// <summary>Renders properties as the block with a leading space, or as nothing when there are none.</summary>
    /// <param name="properties">The properties, already free of line breaks.</param>
    /// <returns>The block, or an empty string.</returns>
    internal static string ToBlock(IReadOnlyList<KeyValuePair<string, string>> properties)
    {
        ArgumentNullException.ThrowIfNull(properties);

        if (properties.Count == 0)
            return string.Empty;

        var builder = new StringBuilder(" {");
        for (var index = 0; index < properties.Count; index++)
        {
            if (index > 0)
                builder.Append(';');

            AppendEscaped(builder, properties[index].Key);
            builder.Append('=');
            AppendEscaped(builder, properties[index].Value);
        }

        return builder.Append('}').ToString();
    }

    /// <summary>Finds what makes a list of persisted scope keys unusable.</summary>
    /// <param name="keys">The configured keys.</param>
    /// <param name="problem">What is wrong with them, phrased to follow the option name.</param>
    /// <param name="value">The offending value, for the message.</param>
    /// <returns><see langword="true"/> when the list is unusable.</returns>
    internal static bool TryFindKeyProblem(IEnumerable<string>? keys, out string problem, out string value)
    {
        problem = string.Empty;
        value = string.Empty;
        if (keys is null)
        {
            problem = "must be a list";
            value = "<null>";
            return true;
        }

        var all = keys.ToList();
        if (all.Any(string.IsNullOrWhiteSpace))
        {
            problem = "must not contain a blank key";
            value = string.Join(", ", all);
            return true;
        }

        if (all.Contains(TruncatedKey, StringComparer.Ordinal))
        {
            problem = "must not contain the reserved truncation marker key";
            value = TruncatedKey;
            return true;
        }

        return false;
    }

    /// <summary>Reads the escapes of a slot or block text back out.</summary>
    /// <param name="text">The text between the delimiters, as the reader's pattern accepted it.</param>
    /// <returns>The text with every escape resolved.</returns>
    internal static string Unescape(string text)
    {
        if (text.IndexOf(Escape, StringComparison.Ordinal) < 0)
            return text;

        var builder = new StringBuilder(text.Length);
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == Escape && index + 1 < text.Length)
                index++;

            builder.Append(text[index]);
        }

        return builder.ToString();
    }

    /// <summary>Reads the content between the braces of a block the pattern of the reader accepted.</summary>
    /// <param name="content">The text between the braces: well-formed pairs separated by <c>;</c>.</param>
    /// <returns>The properties in the order written.</returns>
    internal static List<KeyValuePair<string, string>> Parse(string content)
    {
        var properties = new List<KeyValuePair<string, string>>();
        var key = new StringBuilder();
        var value = new StringBuilder();
        var inValue = false;
        for (var index = 0; index <= content.Length; index++)
        {
            if (index == content.Length || content[index] == ';')
            {
                properties.Add(new KeyValuePair<string, string>(key.ToString(), value.ToString()));
                key.Clear();
                value.Clear();
                inValue = false;
                continue;
            }

            var character = content[index];
            if (character == Escape)
                character = content[++index];
            else if (character == '=' && !inValue)
            {
                inValue = true;
                continue;
            }

            (inValue ? value : key).Append(character);
        }

        return properties;
    }

    private static void AppendEscaped(StringBuilder builder, string text)
    {
        foreach (var character in text)
        {
            if (character is Escape or ';' or '=' or '}')
                builder.Append(Escape);

            builder.Append(character);
        }
    }
}
