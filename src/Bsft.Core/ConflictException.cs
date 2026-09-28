namespace Bsft.Core;

/// <summary>
/// Exception thrown when a resource conflict occurs, such as a duplicate or concurrent modification.
/// </summary>
public sealed class ConflictException : BsftException
{
    /// <summary>The code every instance carries unless a caller names its own.</summary>
    private const string DefaultErrorCode = "CONFLICT";

    /// <summary>
    /// Gets the kind of resource the caller collided with, or <see langword="null"/> when the thrower
    /// named none.
    /// </summary>
    /// <remarks>
    /// A conflict that only says "conflict" leaves a client parsing the detail prose to find out
    /// <em>what</em> it collided with. This pair carries that identity in machine-readable form, the
    /// same shape <see cref="NotFoundException.EntityType"/> and <see cref="NotFoundException.EntityId"/>
    /// carry it in: a name for the kind, a value for the one. The human-readable why stays in the
    /// detail and anything an operator wrote for other operators stays in the log.
    /// </remarks>
    public string? ResourceType { get; }

    /// <summary>
    /// Gets the identifier of the resource the caller collided with, or <see langword="null"/> when the
    /// thrower named none.
    /// </summary>
    public object? ResourceId { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ConflictException"/> class.
    /// </summary>
    /// <param name="message">The error message describing the conflict.</param>
    public ConflictException(string message)
        : base(message, DefaultErrorCode)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ConflictException"/> class, wrapping the exception
    /// that revealed the conflict.
    /// </summary>
    /// <param name="message">The error message describing the conflict.</param>
    /// <param name="innerException">
    /// The exception the conflict was detected from, such as a provider-specific concurrency failure.
    /// </param>
    public ConflictException(string message, Exception innerException)
        : base(message, innerException, DefaultErrorCode)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ConflictException"/> class with a caller-supplied
    /// error code.
    /// </summary>
    /// <param name="message">The error message describing the conflict.</param>
    /// <param name="errorCode">
    /// The machine-readable discriminator the client branches on. Two 409s that a human tells apart by
    /// their message are told apart in code by this, which is why a family with more than one conflict
    /// of its own — idempotency key reuse against a key still in flight, for instance — states it
    /// rather than sharing the generic <c>CONFLICT</c>.
    /// </param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="errorCode"/> is blank.</exception>
    public ConflictException(string message, string errorCode)
        : this(message, errorCode, null, null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ConflictException"/> class with a caller-supplied
    /// error code, wrapping the exception that revealed the conflict.
    /// </summary>
    /// <param name="message">The error message describing the conflict.</param>
    /// <param name="errorCode">The machine-readable discriminator the client branches on.</param>
    /// <param name="innerException">
    /// The exception the conflict was detected from, such as a provider-specific concurrency failure.
    /// </param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="errorCode"/> is blank.</exception>
    public ConflictException(string message, string errorCode, Exception innerException)
        : base(message, innerException, errorCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ConflictException"/> class with a caller-supplied
    /// error code and the resource that was collided with.
    /// </summary>
    /// <param name="message">The error message describing the conflict.</param>
    /// <param name="errorCode">The machine-readable discriminator the client branches on.</param>
    /// <param name="resourceType">
    /// The kind of resource that was collided with, or <see langword="null"/> for none. Only what a
    /// client may see belongs here; the problem document goes over the wire unfiltered.
    /// </param>
    /// <param name="resourceId">
    /// The identifier of that resource, or <see langword="null"/> for none.
    /// </param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="errorCode"/> is blank.</exception>
    public ConflictException(string message, string errorCode, string? resourceType, object? resourceId)
        : base(message, errorCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        ResourceType = resourceType;
        ResourceId = resourceId;
    }
}
