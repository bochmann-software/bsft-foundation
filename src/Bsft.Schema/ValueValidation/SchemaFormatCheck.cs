using System.Globalization;
using System.Net.Mail;

namespace Bsft.Schema.ValueValidation;

/// <summary>What each checked <c>format</c> means, and what a value that misses it should have been.</summary>
/// <remarks>
/// <para>
/// One method rather than a check and a separate name, so a format cannot be checked one way and
/// described another. The words are what a refusal reads: "must be a date, written as yyyy-mm-dd" is
/// something a tenant admin can act on, "must match format date" is not.
/// </para>
/// <para>
/// The two presentational formats match everything. They say how a string is edited, not what it may
/// contain, and refusing a Markdown field for not looking like Markdown would enforce a rule nobody
/// wrote.
/// </para>
/// <para>
/// An email is checked by <see cref="System.Net.Mail.MailAddress"/> rather than by a pattern, because
/// every pattern short enough to read refuses addresses that work.
/// </para>
/// </remarks>
internal static class SchemaFormatCheck
{
    /// <summary>Checks one string against one format.</summary>
    /// <param name="format">The format, one of <see cref="SchemaFormats"/>.</param>
    /// <param name="text">The value.</param>
    /// <returns>
    /// What the value should have been, or <see langword="null"/> when it is what the format asks for.
    /// </returns>
    public static string? Mismatch(string format, string text) => format switch
    {
        SchemaFormats.Email => IsEmail(text) ? null : "an email address",
        SchemaFormats.Uri => IsUri(text) ? null : "a web address",
        SchemaFormats.Date => IsDate(text) ? null : "a date, written as yyyy-mm-dd",
        SchemaFormats.DateTime => IsDateTime(text) ? null : "a date and time",
        SchemaFormats.Uuid => Guid.TryParseExact(text, "D", out _)
            ? null
            : "an identifier in the form 00000000-0000-0000-0000-000000000000",

        // The presentational ones, which constrain nothing.
        _ => null,
    };

    /// <summary>Whether a string is an absolute URI that carries its own scheme.</summary>
    /// <param name="text">The value.</param>
    /// <returns>Whether it is one.</returns>
    /// <remarks>
    /// The scheme has to be in the text rather than only in the parsed result, and it has to be longer
    /// than one character. Both rules are there so that the same manifest gets the same answer on every
    /// host: on Unix <c>Uri.TryCreate</c> reads "/etc/passwd" as an absolute <c>file:</c> URI where
    /// Windows refuses it, and "C:\share" parses as a drive path on Windows and as a one-letter scheme
    /// on Unix. A one-character scheme is legal in RFC 3986 and is not something a configuration field
    /// ever means.
    /// </remarks>
    private static bool IsUri(string text)
        => Uri.TryCreate(text, UriKind.Absolute, out var uri)
            && uri.Scheme.Length > 1
            && text.StartsWith(uri.Scheme + ":", StringComparison.OrdinalIgnoreCase);

    private static bool IsDate(string text)
        => DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    private static bool IsDateTime(string text)
        => DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _);

    // Parsed rather than thrown at: a document may carry a thousand fields of this format, and a
    // submission where every one of them is malformed would otherwise cost a thousand exceptions.
    // The address has to be the whole string, so the display-name spelling "Ops <ops@example.com>"
    // is not an address this accepts.
    private static bool IsEmail(string text)
        => MailAddress.TryCreate(text, out var address)
            && string.Equals(address.Address, text, StringComparison.Ordinal);
}
