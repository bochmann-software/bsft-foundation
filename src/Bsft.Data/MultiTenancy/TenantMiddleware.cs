using Microsoft.AspNetCore.Http;

namespace Bsft.Data.MultiTenancy;

/// <summary>
/// Middleware that resolves the current tenant using <see cref="ITenantResolver"/>
/// and populates the <see cref="ITenantContext"/>.
/// </summary>
public sealed class TenantMiddleware
{
    private readonly RequestDelegate _next;

    /// <summary>
    /// Initializes a new instance of the <see cref="TenantMiddleware"/> class.
    /// </summary>
    /// <param name="next">The next middleware in the pipeline.</param>
    public TenantMiddleware(RequestDelegate next)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
    }

    /// <summary>
    /// Invokes the middleware, resolving the tenant and setting the context.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <param name="tenantResolver">The tenant resolution strategy.</param>
    /// <param name="tenantContext">The tenant context to populate.</param>
    public async Task InvokeAsync(HttpContext context, ITenantResolver tenantResolver, TenantContext tenantContext)
    {
        if (context is null)
            throw new ArgumentNullException(nameof(context));
        if (tenantResolver is null)
            throw new ArgumentNullException(nameof(tenantResolver));
        if (tenantContext is null)
            throw new ArgumentNullException(nameof(tenantContext));

        var tenantId = await tenantResolver.ResolveAsync(context, context.RequestAborted).ConfigureAwait(false);
        tenantContext.SetTenantId(tenantId);

        await _next(context).ConfigureAwait(false);
    }
}
