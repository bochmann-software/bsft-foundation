using Bsft.Localization.EFCore.Tests.Support;
using FluentAssertions;
using Xunit;

namespace Bsft.Localization.EFCore.Tests;

/// <summary>
/// The four reads the resolution chain asks for, and what each of them does and does not return.
/// </summary>
public class EFCoreTranslationReaderTests : IDisposable
{
    private readonly TestTranslationDbContext _dbContext = TestDbContextFactory.Create();

    [Fact]
    public void Constructor_NullDbContext_ThrowsArgumentNullException()
    {
        var act = () => new EFCoreTranslationReader(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("dbContext");
    }

    [Fact]
    public void GetForLocale_ReturnsTheTenantsRowsAndTheGlobalOnes()
    {
        Given("greeting", "en", "Hello tenant", "t1");
        Given("greeting", "en", "Hello world", null);
        Given("greeting", "en", "Hello other", "t2");
        Given("greeting", "de", "Hallo", null);

        var rows = new EFCoreTranslationReader(_dbContext).ListForLocale("en", "t1");

        rows.Should().HaveCount(2);
        rows.Should().Contain(t => t.TenantId == "t1");
        rows.Should().Contain(t => t.TenantId == null);
        rows.Should().NotContain(t => t.TenantId == "t2");
        rows.Should().NotContain(t => t.Locale == "de");
    }

    [Fact]
    public void GetForLocale_WithoutATenant_ReturnsOnlyTheGlobalRows()
    {
        Given("greeting", "en", "Hello tenant", "t1");
        Given("greeting", "en", "Hello world", null);

        var rows = new EFCoreTranslationReader(_dbContext).ListForLocale("en", tenantId: null);

        rows.Should().ContainSingle();
        rows[0].TenantId.Should().BeNull();
    }

    [Fact]
    public void GetForKey_ReturnsBothLocalesAndBothTenantScopes_ForThatKeyOnly()
    {
        Given("greeting", "de", "Hallo", "t1");
        Given("greeting", "en", "Hello", null);
        Given("greeting", "fr", "Bonjour", null);
        Given("farewell", "de", "Tschüss", null);

        var rows = new EFCoreTranslationReader(_dbContext).ListForKey("greeting", "de", "en", "t1");

        rows.Should().HaveCount(2);
        rows.Should().OnlyContain(t => t.Key == "greeting");
        rows.Should().Contain(t => t.Locale == "de").And.Contain(t => t.Locale == "en");
        rows.Should().NotContain(t => t.Locale == "fr");
    }

    [Fact]
    public void GetForLocaleOrFallback_ReturnsEveryKeyOfBothLocalesTheTenantCanSee()
    {
        Given("greeting", "de", "Hallo", null);
        Given("farewell", "en", "Goodbye", null);
        Given("greeting", "de", "Servus", "t1");
        Given("greeting", "de", "Moin", "t2");
        Given("greeting", "fr", "Bonjour", null);

        var rows = new EFCoreTranslationReader(_dbContext).ListForLocaleOrFallback("de", "en", "t1");

        rows.Should().HaveCount(3);
        rows.Should().NotContain(t => t.TenantId == "t2");
        rows.Should().NotContain(t => t.Locale == "fr");
    }

    [Fact]
    public void GetLocales_IsDistinctAndNotNarrowedByTenant()
    {
        Given("greeting", "en", "Hello", null);
        Given("farewell", "en", "Goodbye", null);
        Given("greeting", "de", "Hallo", "t1");

        var locales = new EFCoreTranslationReader(_dbContext).ListLocales();

        locales.Should().BeEquivalentTo(["en", "de"]);
    }

    [Fact]
    public void GetLocales_OnAnEmptyTable_IsEmpty()
    {
        new EFCoreTranslationReader(_dbContext).ListLocales().Should().BeEmpty();
    }

    [Fact]
    public void EveryRead_ProjectsTheStoredRowFaithfully()
    {
        Given("greeting", "en", "Hello", "t1");

        var row = new EFCoreTranslationReader(_dbContext).ListForLocale("en", "t1").Single();

        row.Key.Should().Be("greeting");
        row.Locale.Should().Be("en");
        row.Value.Should().Be("Hello");
        row.TenantId.Should().Be("t1");
    }

    private void Given(string key, string locale, string value, string? tenantId)
    {
        _dbContext.Translations.Add(new TranslationEntity
        {
            Key = key,
            Locale = locale,
            Value = value,
            TenantId = tenantId
        });

        _dbContext.SaveChanges();
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        GC.SuppressFinalize(this);
    }
}
