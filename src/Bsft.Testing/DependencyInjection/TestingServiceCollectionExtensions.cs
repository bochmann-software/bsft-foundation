using Bsft.Testing.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Bsft.Testing.DependencyInjection;

/// <summary>
/// Registers the Bsft test doubles into an existing container, replacing the production
/// registrations they stand in for.
/// </summary>
/// <remarks>
/// Every method replaces rather than adds, so it is safe to call on a container an application has
/// already fully configured — which is exactly what happens inside a
/// <see cref="Web.BsftWebApplicationFactory{TEntryPoint}"/>.
/// </remarks>
public static class TestingServiceCollectionExtensions
{
    /// <summary>Replaces the tenant context with a double the test controls.</summary>
    /// <param name="services">The service collection to modify.</param>
    /// <param name="tenantContext">The double to resolve the tenant context to.</param>
    /// <returns>The same service collection, for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    public static IServiceCollection AddBsftTestTenant(this IServiceCollection services, TestTenantContext tenantContext)
    {
        if (services is null)
            throw new ArgumentNullException(nameof(services));
        if (tenantContext is null)
            throw new ArgumentNullException(nameof(tenantContext));

        TestContextRegistrations.ReplaceTenant(services, tenantContext);

        return services;
    }

    /// <summary>Replaces the correlation context with one carrying a fixed id.</summary>
    /// <param name="services">The service collection to modify.</param>
    /// <param name="correlationContext">The double, registered for the concrete type as well.</param>
    /// <returns>The same service collection, for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    /// <remarks>
    /// The correlation middleware writes the id it resolved for the request into the concrete
    /// context, so in an HTTP test the fixed id survives only when the request carries no
    /// <c>X-Correlation-Id</c> header of its own — send that header to pin the id end to end.
    /// </remarks>
    public static IServiceCollection AddBsftTestCorrelation(
        this IServiceCollection services,
        TestCorrelationContext correlationContext)
    {
        if (services is null)
            throw new ArgumentNullException(nameof(services));
        if (correlationContext is null)
            throw new ArgumentNullException(nameof(correlationContext));

        TestContextRegistrations.ReplaceCorrelation(services, correlationContext);

        return services;
    }

    /// <summary>
    /// Replaces the tenant and correlation contexts in one call, each with its default double:
    /// resolved to <see cref="TestTenantContext.DefaultTenantId"/>, correlated as
    /// <see cref="TestCorrelationContext.DefaultCorrelationId"/>.
    /// </summary>
    /// <param name="services">The service collection to modify.</param>
    /// <returns>The same service collection, for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
    public static IServiceCollection AddBsftTestContexts(this IServiceCollection services)
    {
        if (services is null)
            throw new ArgumentNullException(nameof(services));

        TestContextRegistrations.ReplaceAllWithDefaults(services);

        return services;
    }

    /// <summary>
    /// Repoints a <see cref="DbContext"/> at an in-memory store, dropping whichever provider the
    /// application configured for it.
    /// </summary>
    /// <typeparam name="TContext">The context to repoint.</typeparam>
    /// <param name="services">The service collection to modify.</param>
    /// <param name="databaseName">
    /// The in-memory database name. Two contexts sharing a name share their data, so give each test
    /// that must not see another test's rows its own name.
    /// </param>
    /// <returns>The same service collection, for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="databaseName"/> is null, empty, or white space.
    /// </exception>
    /// <remarks>
    /// The in-memory provider is not a relational database: it ignores raw SQL, relational
    /// constraints, and transactions. Reach for SQLite in-memory instead when the behaviour under
    /// test depends on any of those.
    /// </remarks>
    public static IServiceCollection AddBsftTestDatabase<TContext>(
        this IServiceCollection services,
        string databaseName)
        where TContext : DbContext
    {
        if (services is null)
            throw new ArgumentNullException(nameof(services));
        if (string.IsNullOrWhiteSpace(databaseName))
            throw new ArgumentException("The database name must not be empty.", nameof(databaseName));

        services.RemoveAll<DbContextOptions<TContext>>();
        // EF Core 9 additionally registers the AddDbContext options delegate as
        // IDbContextOptionsConfiguration<TContext> so a second AddDbContext<TContext> call can layer
        // configuration onto the first. Without removing it here, the production provider's
        // configuration survives this swap and both providers try to register for TContext, which
        // AddDbContext refuses with "Services for database providers ... have been registered".
        services.RemoveAll<IDbContextOptionsConfiguration<TContext>>();
        services.RemoveAll<TContext>();
        services.AddDbContext<TContext>(options => options.UseInMemoryDatabase(databaseName));

        return services;
    }
}
