using Bsft.Data.Search;
using Microsoft.Extensions.DependencyInjection;

namespace Bsft.Data.Postgres.DependencyInjection;

/// <summary>
/// Dependency injection extensions for the PostgreSQL full-text search provider.
/// </summary>
public static class DataPostgresServiceCollectionExtensions
{
    /// <summary>
    /// Registers the PostgreSQL search provider and declares which groups are backed by a
    /// <c>tsvector</c> column.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">A callback declaring the indexed groups.</param>
    /// <returns>The same service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    /// <exception cref="ArgumentException">Thrown when a declared name is not usable, naming the argument.</exception>
    public static IServiceCollection AddBsftDataPostgres(
        this IServiceCollection services,
        Action<BsftDataPostgresOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new BsftDataPostgresOptions();
        configure(options);

        return services.AddBsftDataPostgres(options);
    }

    /// <summary>
    /// Registers the PostgreSQL search provider with options the application built itself.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="options">The declaration to register.</param>
    /// <returns>The same service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    /// <remarks>
    /// The overload exists so that the declaration can be built once and handed both to the container
    /// and to <see cref="PostgresFullTextMigrationBuilderExtensions.AddFullTextVector"/>, which runs outside
    /// one. The index and the queries against it have to name the same text search configuration;
    /// keeping them on one instance is what makes that structural rather than a convention two files
    /// apart. The container gets a copy, so a later mutation of the caller's instance cannot make the
    /// two disagree.
    /// </remarks>
    public static IServiceCollection AddBsftDataPostgres(
        this IServiceCollection services,
        BsftDataPostgresOptions options)
    {
        SearchProviderRegistration.Add<BsftDataPostgresOptions, PostgresSearchProvider>(
            services, options, static (from, to) => from.CopyTo(to));

        return services;
    }
}
