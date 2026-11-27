namespace Bsft.Hosting.Cors;

/// <summary>
/// Answers whether an origin is allowed by the configured CORS policy, for code outside the CORS
/// middleware that must reach the same decision, such as a WebSocket handshake check.
/// </summary>
/// <remarks>
/// The built-in evaluator and <c>UseBsftCors</c> read the options of the last <c>AddBsftCors</c> call, so the
/// evaluator matches the policy <c>UseBsftCors</c> applies; a policy an earlier call registered under another
/// name decides by its own call's options. A later <c>Configure</c> of <see cref="BsftCorsOptions"/> through
/// the options pipeline changes none of them. Configured origins are compared the way ASP.NET Core compares
/// them: normalised, and case-sensitive against the request origin.
/// The CORS policy captures the registered evaluator when <c>CorsOptions</c> is first resolved, so a host
/// that registers its own evaluator registers it as a singleton: a scoped one fails with "Cannot resolve
/// scoped service from root provider" under scope validation. A host evaluator replaces
/// <see cref="BsftCorsOptions.OriginPredicate"/> in the policy, and is not consulted when
/// <see cref="BsftCorsOptions.IsAnyOriginAllowed"/> or a <c>*</c> entry that is the only entry of
/// <see cref="BsftCorsOptions.Origins"/> allows every origin.
/// </remarks>
public interface IBsftCorsOriginEvaluator
{
    /// <summary>Decides whether an origin is allowed.</summary>
    /// <param name="origin">
    /// The origin, such as <c>https://app.example.com</c>. A null, empty or whitespace origin is no CORS request
    /// and is never allowed.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when any origin is allowed, the origin matches the configured origins, or
    /// <see cref="BsftCorsOptions.OriginPredicate"/> accepts it.
    /// </returns>
    bool IsOriginAllowed(string? origin);
}
