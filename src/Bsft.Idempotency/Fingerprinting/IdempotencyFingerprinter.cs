using Bsft.Data.MultiTenancy;
using Bsft.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Security.Claims;

namespace Bsft.Idempotency.Fingerprinting;

/// <summary>
/// Builds the scope and the fingerprint from an explicit, ordered segment sequence, hashed with
/// <c>Bsft.Http</c>'s <c>CanonicalHash</c>.
/// </summary>
/// <remarks>
/// The segments are listed here rather than derived from a serializer for the reason
/// <c>CanonicalHash</c> exists at all: a digest over a serializer's output moves when property
/// order or number formatting moves, and two replicas would then disagree about whether a request
/// is the same request.
/// </remarks>
internal sealed class IdempotencyFingerprinter : IIdempotencyFingerprinter
{
    /// <summary>Marks the tenant segment, so an id can never be mistaken for a subject.</summary>
    private const string TenantPrefix = "tenant:";

    /// <summary>Marks the subject segment.</summary>
    private const string SubjectPrefix = "subject:";

    /// <summary>Marks the client segment.</summary>
    private const string ClientPrefix = "client:";

    /// <summary>Marks the client segment of a caller that is an API key rather than an OAuth client.</summary>
    /// <remarks>
    /// A prefix of its own, so a key whose id happens to read like a client id cannot land in that
    /// client's scope.
    /// </remarks>
    private const string ApiKeyPrefix = "api-key:";

    /// <summary>Marks the segment that stands in for a caller with no identity of its own.</summary>
    private const string RemotePrefix = "remote:";

    /// <summary>What a request with no query at all fingerprints as: a count of none, framed.</summary>
    private const string EmptyQuery = "0&";

    /// <summary>The claim an OAuth client id arrives under.</summary>
    private const string ClientIdClaim = "client_id";

    /// <summary>The claim a subject arrives under on a token this framework issues.</summary>
    private const string SubjectClaim = "sub";

    /// <summary>The claim the id of the API key a request was authenticated with arrives under.</summary>
    /// <remarks>
    /// The source of truth is <c>Bsft.Auth</c>'s <c>BsftClaimsMappingOptions.CanonicalApiKeyClaimType</c>;
    /// it is mirrored as a literal here because idempotency is a middleware concern that must not
    /// pull the whole authentication stack into every host that only wants replay protection. The
    /// two have to be changed together.
    /// </remarks>
    private const string ApiKeyClaim = "bsft:api-key";

    private readonly BsftIdempotencyOptions _options;
    private readonly IReadOnlyList<IIdempotencyFingerprintContributor> _contributors;

    /// <summary>
    /// Initializes a new instance of the <see cref="IdempotencyFingerprinter"/> class.
    /// </summary>
    /// <param name="options">The options carrying the body bound.</param>
    /// <param name="contributors">The host's adjustments to the segment sequence, in order.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    public IdempotencyFingerprinter(
        IOptions<BsftIdempotencyOptions> options, IEnumerable<IIdempotencyFingerprintContributor> contributors)
    {
        ArgumentNullException.ThrowIfNull(contributors);

        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _contributors = contributors.ToList();
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context"/> is null.</exception>
    public async ValueTask<IdempotencyFingerprintResult> ComputeAsync(
        HttpContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var caller = ReadCaller(context);
        var bodyDigest = await RequestBodyDigest
            .ComputeAsync(context.Request, _options.MaxBodyBytes, cancellationToken)
            .ConfigureAwait(false);

        var segments = new List<string?>
        {
            context.Request.Method,
            ReadRoutePattern(context),
            ReadQuery(context.Request),
            caller.Tenant,
            caller.Subject,
            caller.Client,
            context.Request.ContentType,
            bodyDigest,
        };

        IReadOnlyList<string?> sequence = segments;

        foreach (var contributor in _contributors)
            sequence = contributor.Contribute(context.Request, sequence) ?? sequence;

        // The caller digest is hashed from the caller segments alone, so it is the same value for
        // every request that caller makes and can lead the store's index.
        var scope = new[] { caller.Tenant, caller.Subject, caller.Client }.ToCanonicalHash();

        return new IdempotencyFingerprintResult(scope, sequence.ToCanonicalHash());
    }

    /// <summary>Reads the three caller segments off the request.</summary>
    /// <param name="context">The request being fingerprinted.</param>
    /// <returns>The tenant, subject and client segments, any of which may be null.</returns>
    /// <remarks>
    /// <para>
    /// A null segment means "there was none", and <c>CanonicalHash</c> encodes null differently from
    /// an empty string, so "no tenant was resolved" cannot collapse into "the tenant whose id is
    /// blank". That distinction is the whole of the cross-tenant guarantee: they are different
    /// facts, and one of them must not be allowed to turn into the other.
    /// </para>
    /// <para>
    /// A machine caller that authenticated with an API key is its own principal: it carries neither a
    /// subject nor an OAuth client id, only the id of the key under <see cref="ApiKeyClaim"/>. That id
    /// is read as the client segment, because without it every such request of one tenant leaving
    /// through one egress address would share the remote scope, and the second key's report would be
    /// answered with the first key's stored response while its own work never ran.
    /// </para>
    /// <para>
    /// A request with no caller at all is the case that distinction cannot cover on its own: three
    /// nulls are the same three nulls for every anonymous caller, so two of them presenting one key
    /// for one request would share a scope and the second would be served the first one's stored
    /// response. The connection's remote address stands in as the discriminator there — it is the
    /// only identity a request that names no caller of its own has. It is read from the connection
    /// rather than from a header, so a caller cannot choose whose scope it lands in.
    /// </para>
    /// </remarks>
    private static (string? Tenant, string? Subject, string? Client) ReadCaller(HttpContext context)
    {
        var tenantContext = context.RequestServices?.GetService<ITenantContext>();
        var tenant = tenantContext is { IsResolved: true, TenantId: { } tenantId }
            ? $"{TenantPrefix}{tenantId}"
            : null;

        var user = context.User;
        var subject = user?.FindFirst(SubjectClaim)?.Value
            ?? user?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var client = ReadClient(user);

        if (subject is null && client is null)
            return (tenant, ReadRemoteCaller(context), null);

        return (tenant, subject is null ? null : $"{SubjectPrefix}{subject}", client);
    }

    /// <summary>Names the credential a caller acted with, an OAuth client id or an API key id.</summary>
    /// <param name="principal">The principal on the request, which may be null or anonymous.</param>
    /// <returns>The client segment, or <see langword="null"/> when the caller names no credential.</returns>
    /// <remarks>
    /// The client id is read first: a token issued to an OAuth client is the more specific fact when
    /// a principal somehow carries both.
    /// </remarks>
    private static string? ReadClient(ClaimsPrincipal? principal)
    {
        if (principal?.FindFirst(ClientIdClaim)?.Value is { } clientId)
            return $"{ClientPrefix}{clientId}";

        return principal?.FindFirst(ApiKeyClaim)?.Value is { } apiKeyId ? $"{ApiKeyPrefix}{apiKeyId}" : null;
    }

    /// <summary>Names an anonymous caller by the connection it arrived on.</summary>
    /// <param name="context">The request being fingerprinted.</param>
    /// <returns>The remote segment, or <see langword="null"/> when there is no remote address.</returns>
    /// <remarks>
    /// A connection with no remote address at all — an in-process test server, a unix socket — is
    /// left null rather than given a shared literal, which keeps such a host exactly where it was:
    /// one scope, and no second caller to leak to.
    /// </remarks>
    private static string? ReadRemoteCaller(HttpContext context) =>
        context.Connection.RemoteIpAddress is { } address ? $"{RemotePrefix}{address}" : null;

    /// <summary>Reads the matched route pattern, falling back to the path when nothing matched.</summary>
    /// <param name="context">The request being fingerprinted.</param>
    /// <returns>The pattern, or the raw path.</returns>
    /// <remarks>
    /// The pattern rather than the path, so <c>/api/orders/{id}</c> is one route however many ids
    /// travel through it — and so an id that moves does not look like a different request when the
    /// rest of it is identical.
    /// </remarks>
    private static string? ReadRoutePattern(HttpContext context) =>
        (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? context.Request.Path.Value;

    /// <summary>Reduces the query string to one segment whose order is a promise.</summary>
    /// <param name="request">The request being fingerprinted.</param>
    /// <returns>The sorted query, framed by its own count.</returns>
    /// <remarks>
    /// Sorted because a query's order is the client's, not a fact about the request, and framed by
    /// its count because a list hashed without one merges with whatever follows it.
    /// </remarks>
    private static string ReadQuery(HttpRequest request)
    {
        // The common case for a request with a body, and the pipeline below would allocate a list, a
        // comparer wrapper and two enumerators to arrive at the same three characters.
        if (request.Query.Count == 0)
            return EmptyQuery;

        var pairs = request.Query
            .SelectMany(entry => entry.Value.Select(value => $"{entry.Key}={value}"))
            .OrderBy(pair => pair, StringComparer.Ordinal)
            .ToList();

        return string.Format(CultureInfo.InvariantCulture, "{0}&{1}", pairs.Count, string.Join('&', pairs));
    }
}
