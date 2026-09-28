using Bsft.Localization.EFCore;
using Microsoft.EntityFrameworkCore;

namespace Bsft.Localization.Tests.Support;

/// <summary>
/// The application context these tests stand in for, so that the localizer is exercised against the
/// reader the framework ships rather than against a fake of it.
/// </summary>
public sealed class TestTranslationDbContext : DbContext, ITranslationDbContext
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
