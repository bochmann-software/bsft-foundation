using System.Collections.Concurrent;

namespace Bsft.Mediator;

/// <summary>
/// Default <see cref="IRequestMetadataProvider"/>: reflects over a request type once and hands the
/// resulting descriptor to every later caller.
/// </summary>
/// <remarks>
/// Registered as a singleton by <c>AddBsftMediator</c>, so the cache spans the application rather
/// than the pipeline instance — a behavior is transient and closed over the declared contract, so a
/// cache held per behavior instance would be rebuilt on every dispatch, and one held per closed
/// generic would be keyed by the wrong type.
/// <para>
/// The set of dispatched request types is fixed once the application's assemblies are loaded, so
/// the cache settles and is not evicted. A concurrent cold start may build a descriptor twice; both
/// describe the same type and every caller is handed the one that won, so the instance a caller
/// sees is stable from the first completed lookup onwards.
/// </para>
/// <para>
/// Public and constructible, like the other <c>TryAdd</c>-registered defaults in the framework, so a
/// host can decorate it — wrap it to observe or extend the lookup — rather than only replace it and
/// reimplement the reflection and the cache from scratch.
/// </para>
/// </remarks>
public sealed class RequestMetadataProvider : IRequestMetadataProvider
{
    private readonly ConcurrentDictionary<Type, IRequestMetadata> _metadataByRequestType = new();

    /// <inheritdoc />
    public IRequestMetadata GetMetadata(object request)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        return GetMetadata(request.GetType());
    }

    /// <inheritdoc />
    public IRequestMetadata GetMetadata(Type requestType)
    {
        if (requestType is null)
            throw new ArgumentNullException(nameof(requestType));

        return _metadataByRequestType.GetOrAdd(requestType, static type => new RequestMetadata(type));
    }
}
