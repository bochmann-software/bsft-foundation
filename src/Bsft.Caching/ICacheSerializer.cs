namespace Bsft.Caching;

/// <summary>
/// Converts cached values to and from the payload a distributed cache stores.
/// </summary>
/// <remarks>
/// Values are serialized against their statically known response type, so a cached response type
/// must be concrete: for a base class only the base members survive the round trip, and with the
/// default JSON implementation an interface or abstract type cannot be read back at all.
/// A decorator around this seam is the place to add encryption when cached results carry personal
/// or confidential data.
/// </remarks>
public interface ICacheSerializer
{
    /// <summary>
    /// Serializes a value into its cache payload.
    /// </summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <param name="value">The value to serialize.</param>
    /// <returns>The payload to store.</returns>
    /// <exception cref="CacheSerializationException">
    /// Thrown when <paramref name="value"/> cannot be written as a payload.
    /// </exception>
    byte[] Serialize<T>(T value);

    /// <summary>
    /// Deserializes a cache payload back into a value.
    /// </summary>
    /// <typeparam name="T">The expected type of the value.</typeparam>
    /// <param name="payload">The stored payload.</param>
    /// <returns>The deserialized value, or <see langword="null"/> when the payload carries no value.</returns>
    /// <exception cref="CacheSerializationException">
    /// Thrown when the payload cannot be read as <typeparamref name="T"/>.
    /// </exception>
    T? Deserialize<T>(ReadOnlySpan<byte> payload);
}
