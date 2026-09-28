using System.Text.Json;
using System.Text.RegularExpressions;

namespace Bsft.Schema.ValueValidation;

/// <summary>Checks one leaf against what its node admits.</summary>
/// <remarks>
/// Separate from the pass that walks the document, because these answer a different question: the pass
/// is about structure - which value sits where - and this is about whether one value is admissible at
/// all. Every message says what the value should have been rather than which keyword failed, because
/// it is read by whoever is filling the form in.
/// </remarks>
internal sealed class SchemaValueCheck
{
    private readonly SchemaViolationLog _log;

    /// <summary>Initializes a new instance of the <see cref="SchemaValueCheck"/> class.</summary>
    /// <param name="log">Where a refusal goes.</param>
    public SchemaValueCheck(SchemaViolationLog log)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <summary>Checks a value against the node's declared choices, if it declared any.</summary>
    /// <param name="node">What the schema says the value is.</param>
    /// <param name="value">The value.</param>
    /// <param name="path">Where it sits.</param>
    public void CheckChoices(SchemaNode node, JsonElement value, string path)
    {
        if (node.AllowedValues is null)
            return;

        // Compared on the raw text, which is what makes 1 and 1.0 the same choice and "A" and "a"
        // different ones - the same answer a JSON reader downstream would give.
        var written = value.GetRawText();

        if (node.AllowedValues.Any(choice => string.Equals(choice.GetRawText(), written, StringComparison.Ordinal)))
            return;

        var choices = string.Join(", ", node.AllowedValues.Select(choice => choice.GetRawText()));

        _log.Refuse(path, SchemaCodes.ValueNotAllowed, $"{Describe(path)} must be one of: {choices}.");
    }

    /// <summary>Checks a string against its length, pattern and format.</summary>
    /// <param name="node">What the schema says the value is.</param>
    /// <param name="value">The value.</param>
    /// <param name="path">Where it sits.</param>
    public void CheckString(SchemaNode node, JsonElement value, string path)
    {
        var text = value.GetString()!;

        if (node.MinLength is { } min && text.Length < min)
            _log.Refuse(path, SchemaCodes.ValueLengthOutOfRange, $"{Describe(path)} must be at least {min} characters.");

        if (node.MaxLength is { } max && text.Length > max)
            _log.Refuse(path, SchemaCodes.ValueLengthOutOfRange, $"{Describe(path)} must be at most {max} characters.");

        if (node.Pattern is not null && !IsMatch(node.Pattern, text))
            _log.Refuse(path, SchemaCodes.ValuePatternMismatch, $"{Describe(path)} is not in the required format.");

        if (node.Format is not null && SchemaFormatCheck.Mismatch(node.Format, text) is { } expected)
            _log.Refuse(path, SchemaCodes.ValueFormatMismatch, $"{Describe(path)} must be {expected}.");
    }

    /// <summary>Checks a number against its range.</summary>
    /// <param name="node">What the schema says the value is.</param>
    /// <param name="value">The value.</param>
    /// <param name="path">Where it sits.</param>
    public void CheckNumber(SchemaNode node, JsonElement value, string path)
    {
        if (!value.TryGetDecimal(out var number))
        {
            _log.Refuse(path, SchemaCodes.ValueOutOfRange, $"{Describe(path)} is larger than this platform can check.");
            return;
        }

        if (node.Minimum is { } min && number < min)
            _log.Refuse(path, SchemaCodes.ValueOutOfRange, $"{Describe(path)} must be {min} or more.");

        if (node.Maximum is { } max && number > max)
            _log.Refuse(path, SchemaCodes.ValueOutOfRange, $"{Describe(path)} must be {max} or less.");
    }

    /// <summary>Runs one pattern, reading a match that ran out of time as no match.</summary>
    /// <param name="pattern">The compiled pattern.</param>
    /// <param name="text">The value to match.</param>
    /// <returns><c>true</c> where the value matches within the configured budget; otherwise <c>false</c>.</returns>
    /// <remarks>
    /// The timeout is the second line of defence behind backtracking being off, and a defence that
    /// throws out of <c>Validate</c> is not one: the caller documented to receive a list of refusals
    /// would instead get a <see cref="RegexMatchTimeoutException"/> off the request thread, from a
    /// value a tenant submitted. A string a pattern cannot be run against within the budget is a string
    /// nothing has shown to match, which is the same answer as not matching, and the refusal names the
    /// path so whoever is filling the form in can shorten it.
    /// </remarks>
    /// <remarks>
    /// The expression runs as it was written, which includes .NET reading a trailing <c>$</c> as
    /// matching before one final newline where ECMA-262 would not. Rewriting the anchor to close that
    /// gap would mean refusing a value no other JSON Schema implementation refuses, so the difference
    /// is documented in <c>docs/schema.md</c> instead of being edited into the publisher's pattern.
    /// </remarks>
    private static bool IsMatch(Regex pattern, string text)
    {
        try
        {
            return pattern.IsMatch(text);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    /// <summary>Names a place in a document the way a refusal says it.</summary>
    /// <param name="path">The JSON Pointer.</param>
    /// <returns>The words a person reads.</returns>
    public static string Describe(string path) => path.Length == 0 ? "This document" : $"\"{path}\"";
}
