using Microsoft.EntityFrameworkCore;

namespace Bsft.Localization.EFCore.Tests.Support;

/// <summary>The application context these tests stand in for.</summary>
internal sealed class TestTranslationDbContext : DbContext, ITranslationDbContext
{
    public TestTranslationDbContext(DbContextOptions<TestTranslationDbContext> options)
        : base(options)
    {
    }

    public DbSet<TranslationEntity> Translations => Set<TranslationEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        new TranslationEntityConfiguration().Configure(modelBuilder.Entity<TranslationEntity>());
    }
}
