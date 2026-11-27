using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Bsft.Core;

namespace Bsft.Data.KeysetPaging;

/// <summary>
/// The version 1 cursor format (see <see cref="IKeysetCursorSerializer"/>): base64url without padding of
/// <c>version | key type | id type | key length | key | id length | id | checksum</c>, lengths as
/// big-endian 16-bit counts of UTF-8 bytes, and the checksum the first 16 bytes of SHA-256 over the UTF-8
/// listing name, a zero byte and everything before the checksum.
/// </summary>
internal sealed class KeysetCursorSerializer : IKeysetCursorSerializer
{
    /// <summary>The longest cursor written or read, in characters.</summary>
    internal const int MaxCursorLength = 1024;

    /// <summary>The format version this serializer writes and the only one it reads.</summary>
    internal const byte FormatVersion = 1;

    /// <summary>The bytes of the checksum at the end of the payload.</summary>
    internal const int ChecksumLength = 16;

    /// <summary>The characters of one base64 quantum.</summary>
    private const int Base64QuantumLength = 4;

    /// <summary>The bytes one base64 quantum carries.</summary>
    private const int Base64QuantumBytes = 3;

    /// <summary>The most payload bytes whose base64url text fits <see cref="MaxCursorLength"/>.</summary>
    private const int MaxPayloadLength = MaxCursorLength / Base64QuantumLength * Base64QuantumBytes;

    private const int HeaderLength = 3;

    private const int LengthPrefix = sizeof(ushort);

    /// <summary>The format of a <see cref="Guid"/>: 32 digits, no separators.</summary>
    private const string GuidFormat = "N";

    /// <summary>The round-trip format of the date and time types, written and read alike.</summary>
    private const string RoundTripFormat = "O";

    private const string InvalidCursorMessage = "The cursor is not valid for this listing.";

    private static readonly Dictionary<Type, KeysetValueDescriptor> Descriptors = new()
    {
        [typeof(string)] = new(1, value => (string)value, text => text),
        [typeof(Guid)] = new(2, value => ((Guid)value).ToString(GuidFormat), text => Guid.TryParseExact(text, GuidFormat, out var parsed) ? parsed : null),
        [typeof(int)] = new(3, FormatInvariant, text => int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var parsed) ? parsed : null),
        [typeof(long)] = new(4, FormatInvariant, text => long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var parsed) ? parsed : null),
        [typeof(decimal)] = new(5, FormatInvariant, text => decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed) ? parsed : null),
        [typeof(DateTime)] = new(6, FormatRoundTrip, text => DateTime.TryParseExact(text, RoundTripFormat, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed) ? parsed : null),
        [typeof(DateTimeOffset)] = new(7, FormatRoundTrip, text => DateTimeOffset.TryParseExact(text, RoundTripFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) ? parsed : null),
        [typeof(DateOnly)] = new(8, FormatRoundTrip, text => DateOnly.TryParseExact(text, RoundTripFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) ? parsed : null),
    };

    /// <inheritdoc />
    public bool Supports(Type valueType)
    {
        ArgumentNullException.ThrowIfNull(valueType);
        return Descriptors.ContainsKey(valueType);
    }

    /// <inheritdoc />
    public string Serialize<TKey, TId>(string listing, KeysetPosition<TKey, TId> position)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(listing);
        ArgumentNullException.ThrowIfNull(position);
        var keyDescriptor = GetDescriptor(typeof(TKey));
        var idDescriptor = GetDescriptor(typeof(TId));
        if (position.Key is null || position.Id is null)
            throw new ArgumentException("A keyset position holds a sort key and an id; neither may be null.", nameof(position));

        var key = Encoding.UTF8.GetBytes(keyDescriptor.Format(position.Key));
        var id = Encoding.UTF8.GetBytes(idDescriptor.Format(position.Id));
        var length = HeaderLength + LengthPrefix + key.Length + LengthPrefix + id.Length + ChecksumLength;

        // Checked before writing, so a length prefix is never truncated: the longest cursor holds far
        // fewer bytes than a 16-bit prefix can count.
        if (length > MaxPayloadLength)
            throw new InvalidOperationException(
                $"The sort key and the id together are too long to carry in a keyset cursor of at most {MaxCursorLength} characters.");

        var payload = new byte[length];
        payload[0] = FormatVersion;
        payload[1] = keyDescriptor.Code;
        payload[2] = idDescriptor.Code;
        var offset = HeaderLength;
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(offset), (ushort)key.Length);
        key.CopyTo(payload, offset + LengthPrefix);
        offset += LengthPrefix + key.Length;
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(offset), (ushort)id.Length);
        id.CopyTo(payload, offset + LengthPrefix);
        var body = payload.Length - ChecksumLength;
        ComputeChecksum(listing, payload.AsSpan(0, body)).CopyTo(payload.AsSpan(body));

        return EncodeBase64Url(payload);
    }

    /// <inheritdoc />
    public KeysetPosition<TKey, TId> Deserialize<TKey, TId>(string listing, string cursor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(listing);
        ArgumentNullException.ThrowIfNull(cursor);
        var keyDescriptor = GetDescriptor(typeof(TKey));
        var idDescriptor = GetDescriptor(typeof(TId));

        var payload = DecodeBase64Url(cursor);
        if (payload is null || payload.Length < HeaderLength + LengthPrefix + LengthPrefix + ChecksumLength)
            throw CreateInvalidCursorException();
        var body = payload.Length - ChecksumLength;
        if (!CryptographicOperations.FixedTimeEquals(ComputeChecksum(listing, payload.AsSpan(0, body)), payload.AsSpan(body)))
            throw CreateInvalidCursorException();
        if (payload[0] != FormatVersion || payload[1] != keyDescriptor.Code || payload[2] != idDescriptor.Code)
            throw CreateInvalidCursorException();

        var texts = ReadTexts(payload.AsSpan(HeaderLength, body - HeaderLength));
        if (texts is null || keyDescriptor.Parse(texts[0]) is not TKey key || idDescriptor.Parse(texts[1]) is not TId id)
            throw CreateInvalidCursorException();
        return new KeysetPosition<TKey, TId>(key, id);
    }

    private static KeysetValueDescriptor GetDescriptor(Type valueType) =>
        Descriptors.TryGetValue(valueType, out var descriptor)
            ? descriptor
            : throw new NotSupportedException($"{valueType.Name} cannot be the sort key or the id of a keyset cursor.");

    private static string FormatInvariant(object value) => ((IFormattable)value).ToString(null, CultureInfo.InvariantCulture);

    private static string FormatRoundTrip(object value) => ((IFormattable)value).ToString(RoundTripFormat, CultureInfo.InvariantCulture);

    private static byte[] ComputeChecksum(string listing, ReadOnlySpan<byte> body)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(listing));
        hash.AppendData([0]);
        hash.AppendData(body);
        return hash.GetHashAndReset()[..ChecksumLength];
    }

    /// <summary>Reads the two length-prefixed texts of the payload, or null when they do not fill it exactly.</summary>
    private static string[]? ReadTexts(ReadOnlySpan<byte> values)
    {
        var texts = new string[2];
        for (var i = 0; i < texts.Length; i++)
        {
            if (values.Length < LengthPrefix)
                return null;
            int length = BinaryPrimitives.ReadUInt16BigEndian(values);
            values = values[LengthPrefix..];
            if (values.Length < length)
                return null;
            texts[i] = Encoding.UTF8.GetString(values[..length]);
            values = values[length..];
        }

        return values.IsEmpty ? texts : null;
    }

    private static string EncodeBase64Url(byte[] payload) =>
        Convert.ToBase64String(payload).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Decodes base64url without padding, or returns null when the text is not that.</summary>
    /// <remarks>
    /// Only the canonical text is accepted: the unused low bits of a last character that does not fill a
    /// byte are ignored by the decoder, so without the comparison two different cursors would name one payload.
    /// </remarks>
    private static byte[]? DecodeBase64Url(string cursor)
    {
        if (cursor.Length == 0 || cursor.Length > MaxCursorLength || cursor.Length % Base64QuantumLength == 1 || !cursor.All(IsBase64UrlCharacter))
            return null;
        var padded = cursor.Replace('-', '+').Replace('_', '/') + new string('=', (Base64QuantumLength - (cursor.Length % Base64QuantumLength)) % Base64QuantumLength);
        var payload = Convert.FromBase64String(padded);
        return string.Equals(EncodeBase64Url(payload), cursor, StringComparison.Ordinal) ? payload : null;
    }

    private static bool IsBase64UrlCharacter(char character) =>
        character is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '_';

    private static ValidationException CreateInvalidCursorException() =>
        new(
            InvalidCursorMessage,
            new Dictionary<string, string[]> { ["cursor"] = [InvalidCursorMessage] },
            KeysetPagingErrorCodes.InvalidCursor);
}
