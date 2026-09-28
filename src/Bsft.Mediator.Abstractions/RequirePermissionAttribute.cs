namespace Bsft.Mediator;

/// <summary>
/// Declares the permission a dispatched request requires, without the declaring package taking a
/// dependency on the web stack. <c>Bsft.Auth.Mediator</c>'s authorization behavior enforces it
/// exactly as it enforces <c>[TenantAuthorize]</c>.
/// </summary>
/// <remarks>
/// <c>[TenantAuthorize]</c> derives from ASP.NET Core's <c>AuthorizeAttribute</c>, so a package that
/// puts it on a command acquires the <c>Microsoft.AspNetCore.App</c> framework reference and with it
/// a web host. That is right for a family that maps endpoints and wrong for one that does not —
/// <c>Bsft.Auth.Credentials</c> holds passwords for worker hosts and console applications and has a
/// test that says so. This attribute is the same declaration in a package every family already
/// references, so a command can carry its permission either way.
/// <para>
/// It declares a requirement and enforces nothing on its own: without the authorization behavior in
/// the pipeline a marked request runs, exactly as an unenforced <c>[TenantAuthorize]</c> does. An
/// endpoint policy stays the outer gate where there is one; this is the gate a background job, a saga
/// or another module passes through.
/// </para>
/// <para>
/// It belongs on a type dispatched as a command or a query, because a pipeline behavior is the only
/// thing that enforces it and the pipeline sees nothing else. On a notification it can never be
/// enforced at all: publishing goes straight to the handlers. The attribute cannot say so in its
/// targets, since a request is a request by the interface it implements rather than by its kind, so
/// the mediator's authorization coverage check reports such a declaration at startup instead.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = true, AllowMultiple = true)]
public sealed class RequirePermissionAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RequirePermissionAttribute"/> class.
    /// </summary>
    /// <param name="permission">The permission the caller must hold.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="permission"/> is blank.</exception>
    public RequirePermissionAttribute(string permission)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(permission);

        Permission = permission;
    }

    /// <summary>Gets the permission the caller must hold for the request to reach its handler.</summary>
    public string Permission { get; }
}
