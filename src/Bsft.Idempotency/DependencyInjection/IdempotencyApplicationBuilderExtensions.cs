using Bsft.Idempotency.Replay;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Bsft.Idempotency.DependencyInjection;

/// <summary>
/// Extension methods for <see cref="IApplicationBuilder"/> to put the idempotency middleware in the
/// pipeline.
/// </summary>
public static class IdempotencyApplicationBuilderExtensions
{
    /// <summary>
    /// Adds the middleware that replays the first response of a repeated unsafe request.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <returns>The application builder for chaining.</returns>
    /// <remarks>
    /// <para>
    /// Order matters, and getting it wrong is silent, so it is stated rather than implied. The call
    /// belongs <strong>after</strong> <c>UseRouting()</c>, because the opt-in is endpoint metadata
    /// and there is no endpoint before routing has run; <strong>after</strong> authentication,
    /// because the authenticated subject is part of the fingerprint and of the store key, and one
    /// resolved after the middleware would put every caller in the same scope; and
    /// <strong>after</strong> <c>UseBsftDefaults()</c>, so <c>BsftExceptionMiddleware</c> is outside
    /// it and the 428 and 409 this middleware raises reach the client as problem documents rather
    /// than as a 500.
    /// </para>
    /// <para>
    /// The ordering is asserted by a test in <c>Bsft.Idempotency.Tests</c> rather than left to this
    /// paragraph.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="app"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <c>AddBsftIdempotency</c> has not been called, because the middleware would
    /// otherwise be in the pipeline with no store, no options that were ever validated, and nothing
    /// to fail until the first protected request.
    /// </exception>
    public static IApplicationBuilder UseBsftIdempotency(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        if (app.ApplicationServices.GetService<IdempotencyRegistrationMarker>() is null)
        {
            throw new InvalidOperationException(
                $"{nameof(UseBsftIdempotency)} requires "
                + $"{nameof(IdempotencyServiceCollectionExtensions.AddBsftIdempotency)} to have been called first.");
        }

        app.UseMiddleware<IdempotencyMiddleware>();

        return app;
    }
}
