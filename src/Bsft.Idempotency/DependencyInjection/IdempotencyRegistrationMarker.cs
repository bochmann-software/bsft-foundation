namespace Bsft.Idempotency.DependencyInjection;

/// <summary>
/// Registered by <c>AddBsftIdempotency</c> so <c>UseBsftIdempotency</c> can tell a wired
/// application from one that only called the pipeline half.
/// </summary>
/// <remarks>
/// The marker rather than the options: the options infrastructure answers a request for
/// <c>IOptions&lt;T&gt;</c> with a default-constructed instance whether or not anybody configured
/// one, so probing for the options would put the middleware in the pipeline of a host that never
/// registered a store and only fail on the first protected request.
/// </remarks>
internal sealed class IdempotencyRegistrationMarker;
