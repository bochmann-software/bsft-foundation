namespace Bsft.Caching;

/// <summary>
/// Thrown when a stored cache payload cannot be read back into the expected type, typically
/// because the response type changed since the entry was written.
/// </summary>
public sealed class CacheSerializationException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CacheSerializationException"/> class.
    /// </summary>
    /// <param name="message">The message describing the failure.</param>
    /// <param name="innerException">The underlying serializer exception.</param>
    public CacheSerializationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
