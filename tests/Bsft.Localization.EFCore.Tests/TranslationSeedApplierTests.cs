using Bsft.Localization.EFCore.Tests.Fakes;
using Bsft.Localization.EFCore.Tests.Support;
using Bsft.Localization.Seeding;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Bsft.Localization.EFCore.Tests;

/// <summary>
/// Writing the compiled-in seed to the store: insert what is missing, never overwrite what is there.
/// </summary>
public class TranslationSeedApplierTests : IDisposable
{
    private readonly TestTranslationDbContext _dbContext = TestDbContextFactory.Create();

    [Fact]
    public async Task ApplyAsync_InsertsNewSeeds()
    {
        var seed = SeedWith(("en", "greeting", "Hello!"), ("de", "greeting", "Hallo!"));

        await _dbContext.ApplyAsync(seed);

        _dbContext.Translations.Should().HaveCount(2);
        _dbContext.Translations.Should().Contain(t => t.Key == "greeting" && t.Locale == "en" && t.Value == "Hello!");
        _dbContext.Translations.Should().Contain(t => t.Key == "greeting" && t.Locale == "de" && t.Value == "Hallo!");
    }

    [Fact]
    public async Task ApplyAsync_DoesNotOverwriteExistingEntries()
    {
        _dbContext.Translations.Add(new TranslationEntity
        {
            Key = "greeting", Locale = "en", Value = "Hi there!", TenantId = null
        });
        await _dbContext.SaveChangesAsync();

        await _dbContext.ApplyAsync(SeedWith(("en", "greeting", "Hello!")));

        _dbContext.Translations.Should().HaveCount(1);
        _dbContext.Translations.First().Value.Should().Be("Hi there!");
    }

    [Fact]
    public async Task ApplyAsync_InsertsOnlyMissingSeeds()
    {
        _dbContext.Translations.Add(new TranslationEntity
        {
            Key = "greeting", Locale = "en", Value = "Existing!", TenantId = null
        });
        await _dbContext.SaveChangesAsync();

        await _dbContext.ApplyAsync(SeedWith(("en", "greeting", "Hello!"), ("en", "farewell", "Goodbye!")));

        _dbContext.Translations.Should().HaveCount(2);
        _dbContext.Translations.Should().Contain(t => t.Key == "greeting" && t.Value == "Existing!");
        _dbContext.Translations.Should().Contain(t => t.Key == "farewell" && t.Value == "Goodbye!");
    }

    [Fact]
    public async Task ApplyAsync_AnEmptySeed_WritesNothing()
    {
        await _dbContext.ApplyAsync(new TranslationSeed());

        _dbContext.Translations.Should().BeEmpty();
    }

    [Fact]
    public async Task ApplyAsync_SeedsGlobally_NeverForATenant()
    {
        await _dbContext.ApplyAsync(SeedWith(("en", "greeting", "Hello!")));

        _dbContext.Translations.Single().TenantId.Should().BeNull();
    }

    [Fact]
    public async Task ApplyAsync_TheInsertedRow_GetsItsSurrogateKey()
    {
        await _dbContext.ApplyAsync(SeedWith(("en", "id-test-key", "ID Test Value")));

        _dbContext.Translations.First(t => t.Key == "id-test-key").Id.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ApplyAsync_NullDbContext_ThrowsArgumentNullException()
    {
        var act = async () => await TranslationSeedApplier.ApplyAsync(null!, new TranslationSeed());

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("dbContext");
    }

    [Fact]
    public async Task ApplyAsync_NullSeed_ThrowsArgumentNullException()
    {
        var act = async () => await _dbContext.ApplyAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("seed");
    }

    [Fact]
    public async Task ApplyAsync_ARowStoredUnderAnotherSpelling_IsNotSeededASecondTime()
    {
        // Under a case-insensitive collation the second row would not be an extra row but a start
        // that fails on the unique index.
        _dbContext.Translations.Add(new TranslationEntity
        {
            Key = "Greeting", Locale = "EN", Value = "Existing!", TenantId = null
        });
        await _dbContext.SaveChangesAsync();

        await _dbContext.ApplyAsync(SeedWith(("en", "greeting", "Hello!")));

        _dbContext.Translations.Should().ContainSingle().Which.Value.Should().Be("Existing!");
    }

    [Fact]
    public async Task ApplyAsync_WhenAnotherInstanceWonTheRace_InsertsWhatIsStillMissing()
    {
        // Two instances starting together read a store without the row and both insert it. The whole
        // batch rolls back, so the rows that did not clash are still missing and the second pass is
        // what puts them there.
        var database = Guid.NewGuid().ToString();
        using var store = TestDbContextFactory.Create(database);

        var racing = new RefusingTranslationDbContext(store, refusals: 1, onRefusal: () =>
        {
            using var other = TestDbContextFactory.Create(database);

            other.Translations.Add(new TranslationEntity
            {
                Key = "greeting", Locale = "en", Value = "Hello from the other one", TenantId = null
            });

            other.SaveChanges();
        });

        await racing.ApplyAsync(SeedWith(("en", "greeting", "Hello!"), ("en", "farewell", "Goodbye!")));

        var stored = store.Translations.AsNoTracking().ToList();

        stored.Should().HaveCount(2);
        stored.Single(t => t.Key == "greeting").Value.Should().Be("Hello from the other one");
        stored.Should().Contain(t => t.Key == "farewell" && t.Value == "Goodbye!");
    }

    [Fact]
    public async Task ApplyAsync_WhenTheSecondPassIsRefusedToo_ThrowsRatherThanRetryingForEver()
    {
        var racing = new RefusingTranslationDbContext(_dbContext, refusals: 2);

        var act = async () => await racing.ApplyAsync(SeedWith(("en", "greeting", "Hello!")));

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    private static TranslationSeed SeedWith(params (string Locale, string Key, string Value)[] entries)
    {
        var builder = new TranslationSeedBuilder();

        foreach (var group in entries.GroupBy(entry => entry.Locale))
        {
            builder.ForLocale(group.Key, locale =>
            {
                foreach (var entry in group)
                    locale.Add(entry.Key, entry.Value);
            });
        }

        return builder.Build();
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        GC.SuppressFinalize(this);
    }
}
