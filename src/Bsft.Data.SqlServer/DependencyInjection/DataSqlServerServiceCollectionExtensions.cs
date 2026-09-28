using Bsft.Data.Search;
using Microsoft.Extensions.DependencyInjection;

namespace Bsft.Data.SqlServer.DependencyInjection;

/// <summary>
/// Dependency injection extensions for the SQL Server full-text search provider.
/// </summary>
public static class DataSqlServerServiceCollectionExtensions
{
    /// <summary>
    /// Registers the SQL Server search provider and declares which groups are backed by an index.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">A callback declaring the indexed groups.</param>
    /// <returns>The same service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    /// <exception cref="ArgumentException">Thrown when a declared group name is not usable.</exception>
    public static IServiceCollection AddBsftDataSqlServer(
        this IServiceCollection services,
        Action<BsftDataSqlServerOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new BsftDataSqlServerOptions();
        configure(options);

        return services.AddBsftDataSqlServer(options);
    }

    /// <summary>
    /// Registers the SQL Server search provider with options the application built itself.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="options">The declaration to register.</param>
    /// <returns>The same service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    /// <remarks>
    /// The container gets a copy, so a mutation of the caller's instance after the call cannot change
    /// what the provider answers for.
    /// </remarks>
    public static IServiceCollection AddBsftDataSqlServer(
        this IServiceCollection services,
        BsftDataSqlServerOptions options)
    {
        SearchProviderRegistration.Add<BsftDataSqlServerOptions, SqlServerSearchProvider>(
            services, options, static (from, to) => from.CopyTo(to));

        return services;
    }
}
