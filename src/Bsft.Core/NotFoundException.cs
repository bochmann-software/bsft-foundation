namespace Bsft.Core;

/// <summary>
/// Exception thrown when a requested entity cannot be found.
/// </summary>
public sealed class NotFoundException : BsftException
{
    /// <summary>
    /// Gets the type name of the entity that was not found.
    /// </summary>
    public string EntityType { get; }

    /// <summary>
    /// Gets the identifier of the entity that was not found.
    /// </summary>
    public object EntityId { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="NotFoundException"/> class.
    /// </summary>
    /// <param name="entityType">The type name of the entity that was not found.</param>
    /// <param name="entityId">The identifier of the entity that was not found.</param>
    public NotFoundException(string entityType, object entityId)
        : base(BuildMessage(entityType, entityId), "NOT_FOUND")
    {
        EntityType = entityType;
        EntityId = entityId;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="NotFoundException"/> class, wrapping the exception
    /// that revealed the entity to be missing.
    /// </summary>
    /// <param name="entityType">The type name of the entity that was not found.</param>
    /// <param name="entityId">The identifier of the entity that was not found.</param>
    /// <param name="innerException">
    /// The exception the absence was detected from, such as a provider's reference failure.
    /// </param>
    public NotFoundException(string entityType, object entityId, Exception innerException)
        : base(BuildMessage(entityType, entityId), innerException, "NOT_FOUND")
    {
        EntityType = entityType;
        EntityId = entityId;
    }

    // Validates the arguments before they are interpolated into the base message, so a null
    // argument throws ArgumentNullException instead of producing a malformed exception message.
    private static string BuildMessage(string entityType, object entityId)
    {
        ArgumentNullException.ThrowIfNull(entityType);
        ArgumentNullException.ThrowIfNull(entityId);

        return $"Entity '{entityType}' with identifier '{entityId}' was not found.";
    }
}
