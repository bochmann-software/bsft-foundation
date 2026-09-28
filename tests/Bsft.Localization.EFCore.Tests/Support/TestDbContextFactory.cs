using Microsoft.EntityFrameworkCore;

namespace Bsft.Localization.EFCore.Tests.Support;

internal static class TestDbContextFactory
{
    public static TestTranslationDbContext Create(string? dbName = null)
    {
        var options = new DbContextOptionsBuilder<TestTranslationDbContext>()
            .UseInMemoryDatabase(dbName ?? Guid.NewGuid().ToString())
            .Options;

        var context = new TestTranslationDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }
}
