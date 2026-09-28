namespace Bsft.Http;

/// <summary>
/// The permissions a route declares, so a console can say what a caller is missing rather than
/// showing them a button that answers 403.
/// </summary>
/// <remarks>
/// <para>
/// It is metadata and not a check. The check is the authorization policy the host configured; this
/// only names what that policy is expected to stand for.
/// </para>
/// <para>
/// Every package that maps routes declares its permissions with this one type, so a reader such as
/// <c>GET /api/auth/permissions</c> or a document generator finds them with a single
/// <c>GetMetadata&lt;RequiredPermissionsMetadata&gt;()</c> whatever package mapped the route.
/// </para>
/// </remarks>
/// <param name="Permissions">The permissions the route declares.</param>
public sealed record RequiredPermissionsMetadata(IReadOnlyList<string> Permissions);
