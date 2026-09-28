using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text.Encodings.Web;

namespace Bsft.Idempotency.Tests.Fakes;

/// <summary>
/// Authenticates whoever the <c>X-Test-Subject</c> header says, so a walkthrough can put two real
/// authenticated callers through one pipeline without standing up an identity provider.
/// </summary>
/// <remarks>
/// A real authentication handler rather than a middleware that assigns <c>HttpContext.User</c>: the
/// ordering rule under test is about <c>UseAuthentication()</c>, and a stand-in that ran somewhere
/// else would assert the arrangement instead of the rule.
/// </remarks>
internal sealed class HeaderSubjectAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    /// <summary>The scheme the test host registers this handler under.</summary>
    public const string SchemeName = "Test";

    /// <summary>The header a caller names itself in.</summary>
    public const string HeaderName = "X-Test-Subject";

    /// <summary>Initializes a new instance of the <see cref="HeaderSubjectAuthenticationHandler"/> class.</summary>
    /// <param name="options">The scheme options.</param>
    /// <param name="logger">The logger factory.</param>
    /// <param name="encoder">The URL encoder.</param>
    public HeaderSubjectAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    /// <inheritdoc />
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var subject = Request.Headers[HeaderName].ToString();

        if (string.IsNullOrEmpty(subject))
            return Task.FromResult(AuthenticateResult.NoResult());

        var identity = new ClaimsIdentity([new Claim("sub", subject)], SchemeName);

        return Task.FromResult(
            AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}
