using Bsft.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Bsft.Data.MultiTenancy;

/// <summary>
/// Fails application startup when a <see cref="ITenantScopedDbContext"/> implementation is registered
/// through <c>AddDbContextPool</c>.
/// </summary>
/// <remarks>
/// <para>
/// A pooled context is constructed once and then rented out again and again, so the
/// <see cref="ITenantContext"/> its constructor received is never resolved a second time and
/// <see cref="ITenantScopedDbContext.CurrentTenantId"/> freezes on the first tenant that rented it.
/// The global query filter then scopes every later request to that tenant — a cross-tenant read with
/// nothing at runtime to signal it. It is the same defect the filter rewrite fixed, arriving through
/// pooling instead of through the model cache, which is why it is worth a startup failure rather than
/// a paragraph in the documentation.
/// </para>
/// <para>
/// The check reads the service collection the host built rather than the built provider, because
/// pooling is visible only as a registration: there is no service to resolve that would answer
/// "is this context pooled". The collection is captured at registration time and read when validation
/// runs, so the order of <c>AddBsftDataMultiTenancy</c> and <c>AddDbContextPool</c> does not matter.
/// </para>
/// <para>
/// Pooling is detected by the <c>IDbContextPool&lt;TContext&gt;</c> registration that
/// <c>AddDbContextPool</c> adds. That type is matched by name: it lives in Entity Framework Core's
/// <c>Internal</c> namespace, and referencing it in source is an error under this repository's
/// warnings-as-errors settings. If a future Entity Framework Core release renames it, this validator
/// stops reporting rather than reporting wrongly, and the documented rule in <c>docs/data.md</c>
/// remains the backstop.
/// </para>
/// </remarks>
public sealed class PooledTenantContextStartupValidator : IStartupValidator
{
    /// <summary>The full name of the open generic Entity Framework Core registers for a pooled context.</summary>
    private const string DbContextPoolTypeName = "Microsoft.EntityFrameworkCore.Internal.IDbContextPool`1";

    private readonly IServiceCollection _services;

    /// <summary>
    /// Initializes a new instance of the <see cref="PooledTenantContextStartupValidator"/> class.
    /// </summary>
    /// <param name="services">The service collection the host is registering into.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    public PooledTenantContextStartupValidator(IServiceCollection services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    /// <inheritdoc />
    public Task<StartupValidationResult> ValidateAsync(CancellationToken cancellationToken = default)
    {
        var pooled = PooledTenantScopedContexts().ToList();

        if (pooled.Count == 0)
            return Task.FromResult(StartupValidationResult.Success("No tenant-scoped DbContext is pooled."));

        var errors = pooled
            .Select(contextType =>
                $"{contextType.Name} implements {nameof(ITenantScopedDbContext)} but is registered with AddDbContextPool, "
                + "which freezes CurrentTenantId on the first tenant that rented the instance and scopes every later request to it. Register it with AddDbContext instead.")
            .ToArray();

        return Task.FromResult(StartupValidationResult.Failure(errors));
    }

    /// <summary>Every context type that is both tenant-scoped and pooled.</summary>
    /// <returns>The offending context types, each reported once.</returns>
    private IEnumerable<Type> PooledTenantScopedContexts() =>
        _services
            .Select(descriptor => descriptor.ServiceType)
            .Where(IsDbContextPool)
            .Select(serviceType => serviceType.GetGenericArguments()[0])
            .Where(contextType => contextType.IsAssignableTo(typeof(ITenantScopedDbContext)))
            .Distinct();

    /// <summary>Whether a service type is Entity Framework Core's pool for one context type.</summary>
    /// <param name="serviceType">The registered service type.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    private static bool IsDbContextPool(Type serviceType) =>
        serviceType.IsGenericType
        && string.Equals(
            serviceType.GetGenericTypeDefinition().FullName,
            DbContextPoolTypeName,
            StringComparison.Ordinal);
}
