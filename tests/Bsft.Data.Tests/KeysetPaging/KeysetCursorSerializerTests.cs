using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Bsft.Core;
using Bsft.Data.KeysetPaging;
using FluentAssertions;
using Xunit;

namespace Bsft.Data.Tests.KeysetPaging;

public sealed class KeysetCursorSerializerTests
{
    private const string Listing = "readings";

    private const byte StringCode = 1;

    private const byte LongCode = 4;

    private readonly KeysetCursorSerializer _serializer = new();

    [Fact]
    public void Serialize_KnownPosition_WritesTheVersionOneFormat()
    {
        // Pins the wire format: a cursor a client holds has to read back after an upgrade.
        _serializer.Serialize(Listing, new KeysetPosition<long, long>(42, 7))
            .Should().Be("AQQEAAI0MgABN59VY9cIctwLvxcCe3VqaMI");
    }

    [Fact]
    public void Serialize_AnyPosition_IsBase64UrlWithoutPadding()
    {
        var cursor = _serializer.Serialize(Listing, new KeysetPosition<string, Guid>("ä/+?", Guid.Parse("fbfffbff-ffff-4fff-bfff-ffffffffffff")));

        cursor.Should().MatchRegex("^[A-Za-z0-9_-]+$");
    }

    [Fact]
    public void Deserialize_EverySupportedType_ReadsBackTheSameValue()
    {
        RoundTrip("a:b|c ä");
        RoundTrip(string.Empty);
        RoundTrip(Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e"));
        RoundTrip(int.MinValue);
        RoundTrip(long.MaxValue);
        RoundTrip(-12.3400m);
        RoundTrip(new DateTime(2026, 11, 12, 10, 30, 15, DateTimeKind.Utc).AddTicks(1234567));
        RoundTrip(new DateTime(2026, 11, 12, 10, 30, 15, DateTimeKind.Unspecified));
        RoundTrip(new DateTimeOffset(2026, 11, 12, 10, 30, 15, TimeSpan.FromHours(2)).AddTicks(7));
        RoundTrip(new DateOnly(2026, 11, 12));
    }

    [Fact]
    public void Deserialize_DateTime_KeepsTheKind()
    {
        var utc = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);

        var read = _serializer.Deserialize<DateTime, long>(Listing, _serializer.Serialize(Listing, new KeysetPosition<DateTime, long>(utc, 1)));

        read.Key.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void Deserialize_CursorOfAnotherListing_IsRefused()
    {
        var cursor = _serializer.Serialize("other", new KeysetPosition<long, long>(42, 7));

        AssertRefused(() => _serializer.Deserialize<long, long>(Listing, cursor));
    }

    [Fact]
    public void Deserialize_AlteredCharacter_IsRefused()
    {
        var cursor = _serializer.Serialize(Listing, new KeysetPosition<long, long>(42, 7));
        var altered = (cursor[5] == 'A' ? 'B' : 'A') + string.Empty;

        AssertRefused(() => _serializer.Deserialize<long, long>(Listing, cursor[..5] + altered + cursor[6..]));
    }

    [Fact]
    public void Deserialize_OnlyUnusedTrailingBitsAltered_IsRefused()
    {
        // 35 characters leave the last one two bits the decoder ignores: 'I' and 'J' decode alike.
        _serializer.Deserialize<long, long>(Listing, "AQQEAAI0MgABN59VY9cIctwLvxcCe3VqaMI").Should().NotBeNull();

        AssertRefused(() => _serializer.Deserialize<long, long>(Listing, "AQQEAAI0MgABN59VY9cIctwLvxcCe3VqaMJ"));
    }

    [Fact]
    public void Deserialize_OtherKeyType_IsRefused()
    {
        var cursor = _serializer.Serialize(Listing, new KeysetPosition<long, long>(42, 7));

        AssertRefused(() => _serializer.Deserialize<DateTime, long>(Listing, cursor));
    }

    [Fact]
    public void Deserialize_OtherIdType_IsRefused()
    {
        var cursor = _serializer.Serialize(Listing, new KeysetPosition<long, long>(42, 7));

        AssertRefused(() => _serializer.Deserialize<long, Guid>(Listing, cursor));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not base64!")]
    [InlineData("abcde")]
    [InlineData("AAAA")]
    [InlineData("AQQE+AI0MgABN59VY9cIctwLvxcCe3VqaMI")]
    [InlineData("AQQEAAI0MgABN59VY9cIctwLvxcCe3VqaMI=")]
    public void Deserialize_NotACursor_IsRefused(string cursor) =>
        AssertRefused(() => _serializer.Deserialize<long, long>(Listing, cursor));

    [Fact]
    public void Deserialize_LongerThanTheLimit_IsRefusedWithoutDecoding()
    {
        var cursor = new string('A', KeysetCursorSerializer.MaxCursorLength + 1);

        AssertRefused(() => _serializer.Deserialize<long, long>(Listing, cursor));
    }

    [Fact]
    public void Deserialize_UnknownVersion_IsRefused()
    {
        var cursor = Forge(2, LongCode, LongCode, Value("42"), Value("7"));

        AssertRefused(() => _serializer.Deserialize<long, long>(Listing, cursor));
    }

    [Fact]
    public void Deserialize_ForgedTextThatIsNoValueOfTheType_IsRefused()
    {
        var cursor = Forge(KeysetCursorSerializer.FormatVersion, LongCode, LongCode, Value("forty-two"), Value("7"));

        AssertRefused(() => _serializer.Deserialize<long, long>(Listing, cursor));
    }

    [Fact]
    public void Deserialize_ForgedTextForEveryParsedType_IsRefused()
    {
        // Codes 2 to 8: every type whose text is parsed rather than taken as it is.
        AssertRefused(() => _serializer.Deserialize<Guid, long>(Listing, ForgeKey(2)));
        AssertRefused(() => _serializer.Deserialize<int, long>(Listing, ForgeKey(3)));
        AssertRefused(() => _serializer.Deserialize<long, long>(Listing, ForgeKey(4)));
        AssertRefused(() => _serializer.Deserialize<decimal, long>(Listing, ForgeKey(5)));
        AssertRefused(() => _serializer.Deserialize<DateTime, long>(Listing, ForgeKey(6)));
        AssertRefused(() => _serializer.Deserialize<DateTimeOffset, long>(Listing, ForgeKey(7)));
        AssertRefused(() => _serializer.Deserialize<DateOnly, long>(Listing, ForgeKey(8)));
    }

    [Fact]
    public void Deserialize_ForgedIdTextThatIsNoValueOfTheType_IsRefused()
    {
        var cursor = Forge(KeysetCursorSerializer.FormatVersion, LongCode, LongCode, Value("42"), Value("x"));

        AssertRefused(() => _serializer.Deserialize<long, long>(Listing, cursor));
    }

    [Fact]
    public void Deserialize_ForgedTrailingBytes_IsRefused()
    {
        var cursor = Forge(KeysetCursorSerializer.FormatVersion, LongCode, LongCode, Value("42"), Value("7"), [0]);

        AssertRefused(() => _serializer.Deserialize<long, long>(Listing, cursor));
    }

    [Fact]
    public void Deserialize_ForgedLengthBeyondThePayload_IsRefused()
    {
        var cursor = Forge(KeysetCursorSerializer.FormatVersion, LongCode, LongCode, Value("42"), [0, 9, 0x37]);

        AssertRefused(() => _serializer.Deserialize<long, long>(Listing, cursor));
    }

    [Fact]
    public void Deserialize_ForgedMissingSecondValue_IsRefused()
    {
        var cursor = Forge(KeysetCursorSerializer.FormatVersion, StringCode, StringCode, Value("abc"), [0]);

        AssertRefused(() => _serializer.Deserialize<string, string>(Listing, cursor));
    }

    [Fact]
    public void Serialize_KeyTooLongForACursor_Throws()
    {
        var act = () => _serializer.Serialize(Listing, new KeysetPosition<string, long>(new string('x', 1000), 1));

        act.Should().Throw<InvalidOperationException>().WithMessage("*too long*");
    }

    [Fact]
    public void Serialize_LongestKeyThatFits_ReadsBack()
    {
        // 768 payload bytes: header 3, two prefixes 4, id 1, checksum 16, leaving 744 for the key.
        var key = new string('x', 744);

        var cursor = _serializer.Serialize(Listing, new KeysetPosition<string, long>(key, 1));

        cursor.Length.Should().Be(KeysetCursorSerializer.MaxCursorLength);
        _serializer.Deserialize<string, long>(Listing, cursor).Key.Should().Be(key);
    }

    [Fact]
    public void Serialize_NullKey_Throws()
    {
        var act = () => _serializer.Serialize(Listing, new KeysetPosition<string, long>(null!, 1));

        act.Should().Throw<ArgumentException>().WithParameterName("position");
    }

    [Fact]
    public void Serialize_NullId_Throws()
    {
        var act = () => _serializer.Serialize(Listing, new KeysetPosition<long, string>(1, null!));

        act.Should().Throw<ArgumentException>().WithParameterName("position");
    }

    [Fact]
    public void Serialize_UnsupportedType_Throws()
    {
        var act = () => _serializer.Serialize(Listing, new KeysetPosition<double, long>(1.5, 1));

        act.Should().Throw<NotSupportedException>().WithMessage("Double*");
    }

    [Fact]
    public void Deserialize_UnsupportedType_Throws()
    {
        var act = () => _serializer.Deserialize<long, double>(Listing, "AAAA");

        act.Should().Throw<NotSupportedException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Serialize_BlankListing_Throws(string listing)
    {
        var act = () => _serializer.Serialize(listing, new KeysetPosition<long, long>(1, 1));

        act.Should().Throw<ArgumentException>().WithParameterName("listing");
    }

    [Fact]
    public void Deserialize_BlankListing_Throws()
    {
        var act = () => _serializer.Deserialize<long, long>(" ", "AAAA");

        act.Should().Throw<ArgumentException>().WithParameterName("listing");
    }

    [Fact]
    public void Serialize_NullPosition_Throws()
    {
        var act = () => _serializer.Serialize<long, long>(Listing, null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("position");
    }

    [Fact]
    public void Deserialize_NullCursor_Throws()
    {
        var act = () => _serializer.Deserialize<long, long>(Listing, null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("cursor");
    }

    [Fact]
    public void Supports_SupportedAndUnsupportedTypes_Answers()
    {
        _serializer.Supports(typeof(DateTime)).Should().BeTrue();
        _serializer.Supports(typeof(double)).Should().BeFalse();
        _serializer.Supports(typeof(long?)).Should().BeFalse();
    }

    [Fact]
    public void Supports_Null_Throws()
    {
        var act = () => _serializer.Supports(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("valueType");
    }

    private static void AssertRefused(Action read)
    {
        var refusal = read.Should().Throw<ValidationException>().Which;
        refusal.ErrorCode.Should().Be(KeysetPagingErrorCodes.InvalidCursor);
        refusal.Errors.Should().ContainKey("cursor");
        refusal.Message.Should().Be("The cursor is not valid for this listing.");
    }

    private void RoundTrip<TKey>(TKey key)
    {
        var position = new KeysetPosition<TKey, long>(key, 99);

        var read = _serializer.Deserialize<TKey, long>(Listing, _serializer.Serialize(Listing, position));

        read.Should().BeEquivalentTo(position);
    }

    /// <summary>A forged cursor whose key of the given type code is text no type parses.</summary>
    private static string ForgeKey(byte keyCode) =>
        Forge(KeysetCursorSerializer.FormatVersion, keyCode, LongCode, Value("not a value"), Value("7"));

    /// <summary>A length-prefixed UTF-8 value as the format writes one.</summary>
    private static byte[] Value(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var value = new byte[2 + bytes.Length];
        BinaryPrimitives.WriteUInt16BigEndian(value, (ushort)bytes.Length);
        bytes.CopyTo(value, 2);
        return value;
    }

    /// <summary>
    /// Builds a cursor with a correct checksum around a body the serializer would never write, as a client
    /// that recomputes the checksum could.
    /// </summary>
    private static string Forge(byte version, byte keyCode, byte idCode, params byte[][] parts)
    {
        var body = new[] { version, keyCode, idCode }.Concat(parts.SelectMany(part => part)).ToArray();
        var checksum = SHA256.HashData(Encoding.UTF8.GetBytes(Listing).Append((byte)0).Concat(body).ToArray())[..16];
        return Convert.ToBase64String(body.Concat(checksum).ToArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
