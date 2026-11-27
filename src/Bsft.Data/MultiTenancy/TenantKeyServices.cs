using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Bsft.Data.MultiTenancy;

/// <summary>Reaches the application's services from inside EF Core.</summary>
internal static class TenantKeyServices
{
    /// <summary>
    /// Returns the provider the context was created from: the request's or the pass's scope for a
    /// context registered with <c>AddDbContext</c>.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <returns>The provider, or <see langword="null"/> for a context created without one.</returns>
    public static IServiceProvider? FindApplicationServices(DbContext context) =>
        context.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>()?.ApplicationServiceProvider;
}
