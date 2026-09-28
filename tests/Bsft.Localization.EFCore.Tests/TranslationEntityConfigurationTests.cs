using Bsft.Localization.EFCore.Tests.Support;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace Bsft.Localization.EFCore.Tests;

/// <summary>
/// The mapping a host applies in its own <c>OnModelCreating</c>.
/// </summary>
public class TranslationEntityConfigurationTests : IDisposable
{
    private readonly TestTranslationDbContext _dbContext = TestDbContextFactory.Create();

    [Fact]
    public void Configure_NullBuilder_ThrowsArgumentNullException()
    {
        var configuration = new TranslationEntityConfiguration();

        var act = () => configuration.Configure(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("builder");
    }

    [Fact]
    public void TheEntity_IsKeyedOnItsSurrogate()
    {
        var entityType = _dbContext.Model.FindEntityType(typeof(TranslationEntity))!;

        entityType.FindPrimaryKey()!.Properties.Should().ContainSingle(p => p.Name == nameof(TranslationEntity.Id));
    }

    [Fact]
    public void TheLookupIndex_IsUniqueOverKeyLocaleAndTenant()
    {
        var index = IndexLedBy(nameof(TranslationEntity.Key));

        index.IsUnique.Should().BeTrue("one tenant holds at most one override per key and locale");
        index.Properties.Select(p => p.Name).Should().Equal(
            nameof(TranslationEntity.Key),
            nameof(TranslationEntity.Locale),
            nameof(TranslationEntity.TenantId));
    }

    [Fact]
    public void TheBundleRead_HasAnIndexOfItsOwnLedByTheLocale()
    {
        // The read behind a bundle names no key, so the unique index cannot answer it.
        var index = IndexLedBy(nameof(TranslationEntity.Locale));

        index.IsUnique.Should().BeFalse("two tenants translate the same locale");
        index.Properties.Select(p => p.Name).Should().Equal(
            nameof(TranslationEntity.Locale),
            nameof(TranslationEntity.TenantId));
    }

    [Fact]
    public void TheEntity_CarriesNoIndexBeyondTheTwoTheReadsNeed()
    {
        var entityType = _dbContext.Model.FindEntityType(typeof(TranslationEntity))!;

        entityType.GetIndexes().Should().HaveCount(2);
    }

    private IIndex IndexLedBy(string property)
    {
        var entityType = _dbContext.Model.FindEntityType(typeof(TranslationEntity))!;

        return entityType.GetIndexes().Should().ContainSingle(i => i.Properties[0].Name == property).Subject;
    }

    [Theory]
    [InlineData(nameof(TranslationEntity.Key), 512, false)]
    [InlineData(nameof(TranslationEntity.Locale), 16, false)]
    [InlineData(nameof(TranslationEntity.TenantId), 128, true)]
    public void TheIndexedColumns_AreBounded(string property, int maxLength, bool nullable)
    {
        var entityType = _dbContext.Model.FindEntityType(typeof(TranslationEntity))!;

        var mapped = entityType.FindProperty(property)!;

        mapped.GetMaxLength().Should().Be(maxLength);
        mapped.IsNullable.Should().Be(nullable);
    }

    [Fact]
    public void TheValue_IsRequiredAndUnbounded()
    {
        var entityType = _dbContext.Model.FindEntityType(typeof(TranslationEntity))!;

        var value = entityType.FindProperty(nameof(TranslationEntity.Value))!;

        value.IsNullable.Should().BeFalse();
        value.GetMaxLength().Should().BeNull("a translation is prose, not an identifier");
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        GC.SuppressFinalize(this);
    }
}
