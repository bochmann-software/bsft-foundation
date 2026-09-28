using Bsft.Data.Search;
using Microsoft.Extensions.DependencyInjection;

namespace Bsft.Data.Sqlite.DependencyInjection;

/// <summary>
/// Dependency injection extensions for the SQLite FTS5 search provider.
/// </summary>
public static class DataSqliteServiceCollectionExtensions
{
    /// <summary>
    /// Registers the SQLite search provider and declares which groups are backed by an FTS5 index.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">A callback declaring the indexed groups.</param>
    /// <returns>The same service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    /// <exception cref="ArgumentException">Thrown when a declared name is not usable, naming the argument.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the tokenizer or the folding level is not a declared value.</exception>
    /// <remarks>
    /// The provider is a singleton and holds no <c>DbContext</c>: it reaches the FTS5 table through
    /// the model of the queryable it is handed. Startup validation resolves the registered providers
    /// from the root, so a scoped provider could not be validated at all.
    /// </remarks>
    public static IServiceCollection AddBsftDataSqlite(
        this IServiceCollection services,
        Action<BsftDataSqliteOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new BsftDataSqliteOptions();
        configure(options);

        return services.AddBsftDataSqlite(options);
    }

    /// <summary>
    /// Registers the SQLite search provider with options the application built itself.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="options">The declaration to register.</param>
    /// <returns>The same service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    /// <remarks>
    /// The overload exists so that the declaration can be built once and handed both to the container
    /// and to <see cref="SqliteFullTextMigrationBuilderExtensions.CreateFullTextIndex"/>, which runs outside
    /// one. The index is built with the tokenizer specification these options describe, and the
    /// provider answers for the same declaration at startup; keeping them on one instance is what makes
    /// that structural rather than a convention two files apart. The container gets a copy, so a later
    /// mutation of the caller's instance cannot make the two disagree.
    /// </remarks>
    public static IServiceCollection AddBsftDataSqlite(
        this IServiceCollection services,
        BsftDataSqliteOptions options)
    {
        SearchProviderRegistration.Add<BsftDataSqliteOptions, SqliteSearchProvider>(
            services, options, static (from, to) => from.CopyTo(to));

        return services;
    }
}
