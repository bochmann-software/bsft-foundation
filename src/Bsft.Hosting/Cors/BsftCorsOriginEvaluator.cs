using Microsoft.AspNetCore.Cors.Infrastructure;

namespace Bsft.Hosting.Cors;

/// <summary>The origin decision of the BSFT CORS policy, shared by the policy and by other callers.</summary>
/// <remarks>
/// The configured origins are matched by a policy built with ASP.NET Core's own builder, so the
/// comparison is the one the middleware applies without a predicate: the configured origins are
/// normalised, the request origin is compared case-sensitively, and a <c>*</c> entry allows every
/// origin only when it is the only entry. It reads the options of the last <c>AddBsftCors</c> call, the
/// instance the policy is built from.
/// </remarks>
internal sealed class BsftCorsOriginEvaluator : IBsftCorsOriginEvaluator
{
    private readonly BsftCorsOptions _options;
    private readonly Lazy<CorsPolicy> _origins;

    /// <summary>Creates the evaluator over the configured options.</summary>
    /// <param name="registration">The registration carrying the options of the last call.</param>
    public BsftCorsOriginEvaluator(BsftCorsRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);

        _options = registration.Options;
        _origins = new Lazy<CorsPolicy>(() =>
        {
            var builder = new CorsPolicyBuilder();
            if (_options.Origins.Count > 0)
            {
                builder.WithOrigins([.. _options.Origins]);
            }

            return builder.Build();
        });
    }

    /// <inheritdoc />
    public bool IsOriginAllowed(string? origin)
    {
        if (string.IsNullOrWhiteSpace(origin))
        {
            return false;
        }

        return _options.IsAnyOriginAllowed
            || _origins.Value.AllowAnyOrigin
            || _origins.Value.IsOriginAllowed(origin)
            || _options.OriginPredicate?.Invoke(origin) == true;
    }
}
