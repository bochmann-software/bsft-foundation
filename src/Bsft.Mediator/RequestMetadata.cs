namespace Bsft.Mediator;

/// <summary>
/// The attributes of one request type, read once and held for the life of the application.
/// </summary>
/// <remarks>
/// Every attribute declared on the type is captured in a single reflection pass, rather than one
/// pass per attribute type a behavior happens to ask for. The lookups below are a scan of that
/// array: a request carries a handful of attributes, so the scan is cheaper than the dictionary it
/// would take to index them, and it keeps the descriptor immutable and therefore safe to share
/// across threads without a lock.
/// </remarks>
internal sealed class RequestMetadata : IRequestMetadata
{
    private readonly Attribute[] _attributes;

    /// <summary>
    /// Initializes a new instance of the <see cref="RequestMetadata"/> class for a request type.
    /// </summary>
    /// <param name="requestType">The concrete request type to describe.</param>
    public RequestMetadata(Type requestType)
    {
        RequestType = requestType ?? throw new ArgumentNullException(nameof(requestType));

        // inherit: true, matching what the shipped attribute-driven behaviors read: a request type
        // deriving from a base that carries the marker declares it just as much as one that spells
        // it out, and a descriptor that disagreed with the behaviors would be a second answer to
        // the same question.
        _attributes = Attribute.GetCustomAttributes(requestType, inherit: true);
    }

    /// <inheritdoc />
    public Type RequestType { get; }

    /// <inheritdoc />
    public TAttribute? FindAttribute<TAttribute>() where TAttribute : Attribute
    {
        foreach (var attribute in _attributes)
        {
            if (attribute is TAttribute match)
                return match;
        }

        return null;
    }

    /// <inheritdoc />
    public IReadOnlyList<TAttribute> ListAttributes<TAttribute>() where TAttribute : Attribute
    {
        List<TAttribute>? matches = null;

        foreach (var attribute in _attributes)
        {
            if (attribute is TAttribute match)
                (matches ??= []).Add(match);
        }

        // The common case is a request that carries none of the asked-for attribute, so the empty
        // result allocates nothing.
        return matches ?? (IReadOnlyList<TAttribute>)[];
    }

    /// <inheritdoc />
    public bool HasAttribute<TAttribute>() where TAttribute : Attribute =>
        FindAttribute<TAttribute>() is not null;
}
