using System.Text.Json;

namespace Bsft.Caching;

/// <summary>
/// Default <see cref="ICacheSerializer"/> using <see cref="JsonSerializer"/>. Works directly on
/// UTF-8 payloads, so a value does not make the detour through an intermediate UTF-16 string on
/// its way to and from the cache.
/// </summary>
public sealed class JsonCacheSerializer : ICacheSerializer
{
    // Shared so every instance uses the same warmed converter cache.
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <inheritdoc />
    public byte[] Serialize<T>(T value)
    {
        // A value the serializer cannot write — a cycle, an unsupported member, a converter that
        // throws — is a caching problem, not a problem with the answer the handler produced. Naming
        // it as one lets the caching layer tell it apart from a genuine failure of the request and
        // degrade instead of failing a query whose work is already done.
        try
        {
            return JsonSerializer.SerializeToUtf8Bytes(value, SerializerOptions);
        }
        catch (JsonException ex)
        {
            throw Unwritable<T>(ex);
        }
        catch (NotSupportedException ex)
        {
            throw Unwritable<T>(ex);
        }
    }

    private static CacheSerializationException Unwritable<T>(Exception cause) =>
        new($"A value of type {typeof(T).Name} could not be written as a cache payload.", cause);

    private static CacheSerializationException Unreadable<T>(Exception cause) =>
        new($"A cached payload could not be read as {typeof(T).Name}.", cause);

    /// <inheritdoc />
    public T? Deserialize<T>(ReadOnlySpan<byte> payload)
    {
        // A malformed or mistyped payload and a response type the serializer cannot read back at
        // all (an interface or an abstract type) mean the same thing to the caller: this entry is
        // unusable.
        try
        {
            return JsonSerializer.Deserialize<T>(payload, SerializerOptions);
        }
        catch (JsonException ex)
        {
            throw Unreadable<T>(ex);
        }
        catch (NotSupportedException ex)
        {
            throw Unreadable<T>(ex);
        }
    }
}
