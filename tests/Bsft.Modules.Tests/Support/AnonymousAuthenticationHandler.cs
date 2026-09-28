using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bsft.Modules.Tests.Support;

/// <summary>
/// Authenticates nobody, so a request reaches the endpoint exactly as an unauthenticated caller's
/// would. It is registered under the same scheme name as <see cref="TestAuthenticationHandler"/> so
/// the two differ in nothing but whether the caller is known.
/// </summary>
internal sealed class AnonymousAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    /// <summary>Initializes a new instance of the <see cref="AnonymousAuthenticationHandler"/> class.</summary>
    /// <param name="options">The scheme options monitor.</param>
    /// <param name="logger">The logger factory.</param>
    /// <param name="encoder">The URL encoder.</param>
    public AnonymousAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    /// <inheritdoc />
    protected override Task<AuthenticateResult> HandleAuthenticateAsync() =>
        Task.FromResult(AuthenticateResult.NoResult());
}
