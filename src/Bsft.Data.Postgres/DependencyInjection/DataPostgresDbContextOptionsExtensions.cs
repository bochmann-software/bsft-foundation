using Bsft.Data.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace Bsft.Data.Postgres.DependencyInjection;

/// <summary>
/// The PostgreSQL shorthand for enlisting a context in the shared transaction of <c>Bsft.Data</c>.
/// </summary>
public static class DataPostgresDbContextOptionsExtensions
{
    /// <summary>
    /// Configures the context for PostgreSQL on the scope's shared connection and enlists it in the
    /// scope's shared transaction; the same as
    /// <c>UseBsftSharedTransaction(serviceProvider, (o, connection) =&gt; o.UseNpgsql(connection, npgsqlOptionsAction))</c>.
    /// </summary>
    /// <param name="optionsBuilder">The options builder of the context being registered.</param>
    /// <param name="serviceProvider">The scoped provider the context is being resolved from.</param>
    /// <param name="npgsqlOptionsAction">
    /// The context's own Npgsql options, for example its migrations history table and schema. Must not
    /// call <c>EnableRetryOnFailure</c>.
    /// </param>
    /// <returns>The same <paramref name="optionsBuilder"/> for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="optionsBuilder"/> or <paramref name="serviceProvider"/> is null.</exception>
    public static DbContextOptionsBuilder UseBsftSharedNpgsql(
        this DbContextOptionsBuilder optionsBuilder,
        IServiceProvider serviceProvider,
        Action<NpgsqlDbContextOptionsBuilder>? npgsqlOptionsAction = null) =>
        optionsBuilder.UseBsftSharedTransaction(
            serviceProvider,
            (options, connection) => options.UseNpgsql(connection, npgsqlOptionsAction));
}
