using System.Globalization;
using System.Text;

namespace Bsft.Messaging.RabbitMQ;

/// <summary>
/// A delivery's AMQP headers, read as the types this adapter needs them as.
/// </summary>
/// <remarks>
/// <para>
/// It exists because reading one is never a lookup. AMQP's field table has no string type: the client
/// writes a string as a byte array and reads it back as one, so a header this adapter set as a string
/// arrives as bytes. A foreign client may write the same value as a string, a 32-bit integer or a 64-bit
/// one, and a broker writes its own headers in whichever type the protocol names for them. Every reader
/// therefore has to accept several shapes and decide what an unusable one means, and that decision
/// belongs in one place rather than being repeated at each header the mapper reads.
/// </para>
/// <para>
/// Nothing here throws. A header that is absent, or present in a shape that cannot be read, is reported
/// as the absence it amounts to; what a missing value means is the caller's to decide, because it
/// differs per header.
/// </para>
/// <para>
/// It is a struct because it is one reference with readers attached and no identity of its own, and a
/// consumed delivery builds more than one of them. The default instance, which carries no dictionary,
/// reads as a delivery with no headers, which is exactly what it is.
/// </para>
/// </remarks>
internal readonly struct RabbitMQHeaderTable
{
    private readonly IDictionary<string, object?>? _headers;

    /// <summary>
    /// Initializes a new instance of the <see cref="RabbitMQHeaderTable"/> class.
    /// </summary>
    /// <param name="headers">The delivery's headers, which AMQP allows to be absent entirely.</param>
    public RabbitMQHeaderTable(IDictionary<string, object?>? headers)
    {
        _headers = headers;
    }

    /// <summary>Reads a header as text, or <see langword="null"/> when it carries none.</summary>
    /// <param name="key">The header to read.</param>
    /// <returns>The header's text, or <see langword="null"/>.</returns>
    public string? Text(string key) =>
        Value(key) switch
        {
            byte[] bytes => Encoding.UTF8.GetString(bytes),
            string text => text,
            _ => null,
        };

    /// <summary>Reads a header as a whole number, falling back when it carries none.</summary>
    /// <param name="key">The header to read.</param>
    /// <param name="fallback">What an absent or unreadable header counts as.</param>
    /// <returns>The header's value, or <paramref name="fallback"/>.</returns>
    public int Int32(string key, int fallback) =>
        Value(key) switch
        {
            int number => number,
            byte[] bytes when TryParseInt32(Encoding.UTF8.GetString(bytes), out var parsed) => parsed,
            string text when TryParseInt32(text, out var parsed) => parsed,
            _ => fallback,
        };

    /// <summary>
    /// Reads a header as a count, which is zero when it carries none.
    /// </summary>
    /// <remarks>
    /// AMQP's own type for a count is a signed 64-bit integer, so a negative value is representable. It
    /// is read as no count rather than as a negative one: nothing this adapter counts can be negative,
    /// and passing one on would turn a broker's oddity into an attempt number below the first.
    /// </remarks>
    /// <param name="key">The header to read.</param>
    /// <returns>The count, or zero.</returns>
    public long Count(string key)
    {
        var value = Value(key) switch
        {
            long count => count,
            int count => count,
            byte[] bytes when TryParseInt64(Encoding.UTF8.GetString(bytes), out var parsed) => parsed,
            string text when TryParseInt64(text, out var parsed) => parsed,
            _ => 0L,
        };

        return value < 0 ? 0L : value;
    }

    private static bool TryParseInt32(string text, out int value) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    private static bool TryParseInt64(string text, out long value) =>
        long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    private object? Value(string key) =>
        _headers is not null && _headers.TryGetValue(key, out var value) ? value : null;
}
