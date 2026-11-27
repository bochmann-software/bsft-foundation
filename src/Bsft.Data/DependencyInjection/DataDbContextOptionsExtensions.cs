using System.Data.Common;
using Bsft.Data.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bsft.Data.DependencyInjection;

/// <summary>
/// Extension methods that enlist an application's <see cref="DbContext"/> in the shared transaction
/// <see cref="DataServiceCollectionExtensions.AddBsftDataSharedTransaction"/> registers.
/// </summary>
public static class DataDbContextOptionsExtensions
{
    /// <summary>
    /// Points the context's provider at the scope's shared connection and enlists the context in the
    /// scope's shared transaction, including when it is resolved after the transaction began.
    /// </summary>
    /// <param name="optionsBuilder">The options builder of the context being registered.</param>
    /// <param name="serviceProvider">The scoped provider the context is being resolved from.</param>
    /// <param name="useProvider">
    /// The provider call, given the shared connection, for example
    /// <c>(options, connection) =&gt; options.UseNpgsql(connection, npgsql =&gt; npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "catalog"))</c>.
    /// Everything else about the provider — the migrations history table, the schema — stays per context.
    /// </param>
    /// <returns>The same <paramref name="optionsBuilder"/> for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the shared transaction is not registered: call
    /// <see cref="DataServiceCollectionExtensions.AddBsftDataSharedTransaction"/>, or a provider's
    /// registration such as <c>AddBsftDataPostgresTransaction</c>, first. Thrown as well when the options
    /// are built from the root provider, which a pooled context or a context factory does.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Call it from the <c>AddDbContext</c> overload that hands the scoped provider to the options
    /// callback:
    /// <code>
    /// services.AddDbContext&lt;CatalogDbContext&gt;((sp, options) =&gt; options
    ///     .UseBsftSharedTransaction(sp, (o, connection) =&gt; o.UseNpgsql(connection)));
    /// </code>
    /// The context must be registered scoped and not pooled: a pooled context or a context factory builds
    /// its options once, from the root provider, for every scope, which would hand one connection to all
    /// of them, so building the options from the root provider throws. The
    /// context must not configure <c>EnableRetryOnFailure</c>; the shared transaction is retried as a whole
    /// by <c>[Retryable]</c>, and enlisting a retrying context throws.
    /// </para>
    /// </remarks>
    public static DbContextOptionsBuilder UseBsftSharedTransaction(
        this DbContextOptionsBuilder optionsBuilder,
        IServiceProvider serviceProvider,
        Action<DbContextOptionsBuilder, DbConnection> useProvider)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(useProvider);

        var contextType = optionsBuilder.Options.ContextType;
        var root = serviceProvider.GetService<SharedTransactionRoot>()
            ?? throw new InvalidOperationException(
                $"UseBsftSharedTransaction enlists the context '{contextType.Name}' in the scope's shared transaction, but none is registered. Call services.AddBsftDataSharedTransaction(...), or a provider's registration such as services.AddBsftDataPostgresTransaction(...), when registering the services.");

        root.ThrowIfRoot(serviceProvider, contextType);

        // Resolved for its subscription: from here on, every enlisted context takes the open
        // transaction as it initializes.
        _ = serviceProvider.GetRequiredService<SharedContextLifetimeListener>();

        serviceProvider.GetRequiredService<SharedTransaction>().Configure(optionsBuilder, useProvider);

        optionsBuilder.AddInterceptors(serviceProvider.GetRequiredService<SharedTransactionInterceptor>());

        return optionsBuilder;
    }
}
