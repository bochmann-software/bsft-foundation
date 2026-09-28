using Bsft.Data.MultiTenancy;
using Bsft.Data.Search;
using Bsft.Data.Contracts.Search;
using Bsft.Data.Tests.Support;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.Data.Tests.Search;

public class SearchStartupValidatorTests
{
    [Fact]
    public async Task WithoutAnyDeclaredGroupTheValidatorPasses()
    {
        var result = await Validator([], []).ValidateAsync();

        result.IsValid.Should().BeTrue();
        result.Message.Should().Contain("No searchable fields");
    }

    [Fact]
    public async Task ADeclaredGroupWithoutAProviderFailsStartup()
    {
        var result = await Validator([Source(Group())], []).ValidateAsync();

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("no ISearchProvider is registered"));
    }

    [Fact]
    public async Task AmbiguousProviderRegistrationFailsStartup()
    {
        var result = await Validator(
            [Source(Group())],
            [ReportingProvider(), ReportingProvider()],
            withScopeProvider: true).ValidateAsync();

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Should().Contain("Register exactly one");
    }

    [Fact]
    public async Task AMissingIndexFailsStartupInsteadOfFallingBackToLike()
    {
        var provider = new FakeSearchProvider();
        provider.ConfigurationErrors.Add("Search group 'profile' has no full-text index.");

        var result = await Validator([Source(Group())], [provider], withScopeProvider: true).ValidateAsync();

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("profile"));
    }

    [Fact]
    public async Task AScopedGroupWithoutAScopeProviderFailsStartup()
    {
        var result = await Validator([Source(Group())], [new FakeSearchProvider()]).ValidateAsync();

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("ISearchScopeProvider"));
    }

    [Fact]
    public async Task AnEntityThatCannotBeScopedFailsStartupUnlessDeclaredUnscoped()
    {
        var result = await Validator(
            [Source(Group(entityType: typeof(UnscopableEntity)))],
            [new FakeSearchProvider()],
            withScopeProvider: true).ValidateAsync();

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Unscoped()"));
    }

    [Theory]
    [InlineData(typeof(TenantOnlyEntity))]
    [InlineData(typeof(OrganizationOnlyEntity))]
    [InlineData(typeof(Candidate))]
    public async Task AnEntityCarryingEitherMarkerCanBeScoped(Type entityType)
    {
        var result = await Validator(
            [Source(Group(entityType: entityType))],
            [new FakeSearchProvider()],
            withScopeProvider: true).ValidateAsync();

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task AnExplicitlyUnscopedGroupNeedsNeitherMarkerNorScopeProvider()
    {
        var result = await Validator(
            [Source(Group(entityType: typeof(UnscopableEntity), isUnscoped: true))],
            [new FakeSearchProvider()]).ValidateAsync();

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task AnUnscopedDeclarationOnAScopedEntityFailsStartup()
    {
        // Candidate carries both markers, so the declaration claims a boundary-free search over data
        // that has a boundary. The runtime applies the boundary regardless, which makes the claim a
        // lie either way — silently truncated results if it were honoured, a false promise if not.
        var result = await Validator(
            [Source(Group(isUnscoped: true))],
            [new FakeSearchProvider()],
            withScopeProvider: true).ValidateAsync();

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Unscoped()") && e.Contains(nameof(Candidate)));
    }

    [Fact]
    public async Task AnIndexedAndScopedGroupPasses()
    {
        var result = await Validator([Source(Group())], [new FakeSearchProvider()], withScopeProvider: true).ValidateAsync();

        result.IsValid.Should().BeTrue();
        result.Message.Should().Contain("1 search group");
    }

    [Fact]
    public async Task TheSameGroupNameOnTwoEntitiesFailsStartup()
    {
        // A provider declares its indexes by name, so HasFullTextIndex("profile") would certify both
        // entities and the unindexed one would only fail on the first real search.
        var result = await Validator(
            [Source(Group(entityType: typeof(UnscopableEntity), isUnscoped: true)), Source(Group())],
            [new FakeSearchProvider()],
            withScopeProvider: true).ValidateAsync();

        result.IsValid.Should().BeFalse();
        // The entities are named in a fixed order, not in the order the declarations were found.
        result.Errors.Should().Contain(e => e.Contains("more than one entity")
            && e.Contains("profile")
            && e.Contains($"({nameof(Candidate)}, {nameof(UnscopableEntity)})"));
    }

    [Fact]
    public async Task TheSameGroupOnTheSameEntityIsNotAmbiguous()
    {
        // Registering the same assembly twice yields the same declaration twice. That names one index
        // and one table, so it is not the ambiguity above.
        var result = await Validator(
            [Source(Group()), Source(Group())],
            [new FakeSearchProvider()],
            withScopeProvider: true).ValidateAsync();

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ArgumentsAreValidated()
    {
        var scopeFactory = new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        FluentActions.Invoking(() => new SearchStartupValidator(null!, [], scopeFactory))
            .Should().Throw<ArgumentNullException>().WithParameterName("sources");
        FluentActions.Invoking(() => new SearchStartupValidator([], null!, scopeFactory))
            .Should().Throw<ArgumentNullException>().WithParameterName("providers");
        FluentActions.Invoking(() => new SearchStartupValidator([], [], null!))
            .Should().Throw<ArgumentNullException>().WithParameterName("scopeFactory");
    }

    private static SearchStartupValidator Validator(
        ISearchGroupSource[] sources,
        ISearchProvider[] providers,
        bool withScopeProvider = false)
    {
        var services = new ServiceCollection();
        if (withScopeProvider)
            services.AddScoped<ISearchScopeProvider>(_ => new FixedScopeProvider(SearchScopeState.Unrestricted));

        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
        return new SearchStartupValidator(sources, providers, scopeFactory);
    }

    private static SearchGroupConfiguration Group(Type? entityType = null, bool isUnscoped = false) => new(
        "profile",
        entityType ?? typeof(Candidate),
        [new SearchField("Name", (System.Linq.Expressions.Expression<Func<Candidate, string?>>)(c => c.Name), 1.0)],
        SearchMode.Prefix,
        "de",
        isUnscoped);

    private static ISearchGroupSource Source(params SearchGroupConfiguration[] groups) => new GroupSource(groups);

    private static FakeSearchProvider ReportingProvider()
    {
        // A provider with a configuration error of its own, which the ambiguity must not reach.
        var provider = new FakeSearchProvider();
        provider.ConfigurationErrors.Add("Search group 'profile' has no full-text index.");
        return provider;
    }

    private sealed class UnscopableEntity
    {
        public string Name { get; set; } = string.Empty;
    }

    private sealed class TenantOnlyEntity : ITenantEntity
    {
        public string? TenantId { get; set; }
    }

    private sealed class OrganizationOnlyEntity : IOrganizationScopedEntity
    {
        public Guid OrganizationUnitId { get; set; }
    }

    private sealed class GroupSource : ISearchGroupSource
    {
        public GroupSource(SearchGroupConfiguration[] groups) => Groups = groups;

        public IReadOnlyList<SearchGroupConfiguration> Groups { get; }
    }
}
