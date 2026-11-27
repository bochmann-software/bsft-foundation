using Bsft.Data.DependencyInjection;
using Bsft.Data.Search;
using Bsft.Data.Transactions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace Bsft.Data.Postgres.DependencyInjection;

/// <summary>
/// Dependency injection extensions for the PostgreSQL full-text search provider and the PostgreSQL
/// shared transaction.
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

    /// <summary>
    /// Registers the shared transaction of <c>Bsft.Data</c> on a PostgreSQL connection per scope,
    /// classifies PostgreSQL's serialization failure and deadlock as transaction conflicts so
    /// <c>[Retryable]</c> re-runs the whole unit on them, and asks the server before every commit whether
    /// it already aborted the transaction.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="connectionString">The connection string of the database every enlisted context shares.</param>
    /// <returns>The same service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="connectionString"/> is blank.</exception>
    /// <remarks>
    /// Enlist each context with <see cref="DataPostgresDbContextOptionsExtensions.UseBsftSharedNpgsql"/>.
    /// A host that creates its connections from an <c>NpgsqlDataSource</c> uses the overload taking a factory.
    /// </remarks>
    public static IServiceCollection AddBsftDataPostgresTransaction(
        this IServiceCollection services,
        string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        return services.AddBsftDataPostgresTransaction(_ => new NpgsqlConnection(connectionString));
    }

    /// <summary>
    /// Registers the shared transaction of <c>Bsft.Data</c> on a PostgreSQL connection the factory creates
    /// per scope, classifies PostgreSQL's serialization failure and deadlock as transaction conflicts,
    /// and asks the server before every commit whether it already aborted the transaction.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="connectionFactory">
    /// Creates the scope's connection, unopened, for example <c>sp =&gt; dataSource.CreateConnection()</c>.
    /// </param>
    /// <returns>The same service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    public static IServiceCollection AddBsftDataPostgresTransaction(
        this IServiceCollection services,
        Func<IServiceProvider, NpgsqlConnection> connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(connectionFactory);

        services.AddBsftDataSharedTransaction(connectionFactory);
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<ITransactionConflictClassifier, PostgresTransactionConflictClassifier>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<ITransactionAbortProbe, PostgresTransactionAbortProbe>());

        return services;
    }
}
