using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace Bsft.Data.Tests.Modeling;

/// <summary>
/// A SQLite context whose model is cached under a key of its own, so every test starts from a model
/// whose relational model nobody has built yet.
/// </summary>
/// <remarks>
/// No connection is opened: building the relational model needs the provider's services, never the
/// database.
/// </remarks>
public sealed class FreshModelContext : DbContext
{
    private readonly bool _relational;

    /// <summary>Initializes a new instance of the <see cref="FreshModelContext"/> class.</summary>
    /// <param name="modelKey">The key the model is cached under; a new key is a new model.</param>
    /// <param name="relational"><see langword="false"/> for the in-memory provider.</param>
    public FreshModelContext(Guid modelKey, bool relational = true)
    {
        ModelKey = modelKey;
        _relational = relational;
    }

    /// <summary>Gets the key the model is cached under.</summary>
    public Guid ModelKey { get; }

    /// <summary>Gets the one entity set.</summary>
    public DbSet<WarmupEntity> Entities => Set<WarmupEntity>();

    /// <summary>
    /// Builds a runtime-initialized relational model outside any context, so no context reports it
    /// and only the code under test can build its relational model.
    /// </summary>
    /// <returns>A model whose relational model is not built yet.</returns>
    public static IModel UnbuiltModel()
    {
        var builder = SqliteConventionSetBuilder.CreateModelBuilder();
        builder.Entity<WarmupEntity>();

        using var context = new FreshModelContext(Guid.NewGuid());

        return context.GetService<IModelRuntimeInitializer>().Initialize(builder.FinalizeModel(), designTime: false);
    }

    /// <inheritdoc />
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (_relational)
            optionsBuilder.UseSqlite("Data Source=:memory:");
        else
            optionsBuilder.UseInMemoryDatabase(ModelKey.ToString());

        optionsBuilder.ReplaceService<IModelCacheKeyFactory, KeyFactory>();
    }

    /// <summary>Caches each model under the context's own key.</summary>
    private sealed class KeyFactory : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime)
            => (context.GetType(), ((FreshModelContext)context).ModelKey, designTime);
    }
}
