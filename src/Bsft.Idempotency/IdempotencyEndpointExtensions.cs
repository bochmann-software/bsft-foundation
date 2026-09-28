using Microsoft.AspNetCore.Builder;

namespace Bsft.Idempotency;

/// <summary>
/// Extension methods that opt a minimal-API route, or a whole route group, into idempotent handling.
/// </summary>
/// <remarks>
/// The attribute is the controller half of the same opt-in and this is the minimal-API half; both
/// end up as <see cref="IIdempotencyMetadata"/> on the endpoint, which is the only thing the
/// middleware reads. A group-level call is what lets a family that maps several unsafe routes —
/// <c>MapBsftWebhooks</c> and its kind — opt them in wholesale rather than one at a time, which is
/// the case the plain <c>WithMetadata</c> spelling handles badly.
/// </remarks>
public static class IdempotencyEndpointExtensions
{
    /// <summary>
    /// Marks every endpoint the builder covers as one the idempotency middleware protects.
    /// </summary>
    /// <typeparam name="TBuilder">The route or group builder.</typeparam>
    /// <param name="builder">The builder to add the metadata to.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="builder"/> is null.</exception>
    public static TBuilder RequireIdempotency<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.WithMetadata(new IdempotentAttribute());

        return builder;
    }

    /// <summary>
    /// Marks every endpoint the builder covers as protected <em>and</em> refuses a request that
    /// carries no key, whatever <see cref="BsftIdempotencyOptions.ShouldRequireKey"/> says.
    /// </summary>
    /// <typeparam name="TBuilder">The route or group builder.</typeparam>
    /// <param name="builder">The builder to add the metadata to.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    /// <remarks>
    /// A second method rather than a flag on the first, because the two are different intentions and
    /// a call site reading <c>RequireIdempotency(true)</c> says nothing about which of them it meant.
    /// This is the one to reach for on a route where an unprotected call is a business error — a
    /// payment, a provisioning call — rather than a nuisance.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="builder"/> is null.</exception>
    public static TBuilder RequireIdempotencyKey<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.WithMetadata(new IdempotentAttribute(requireKey: true));

        return builder;
    }
}
