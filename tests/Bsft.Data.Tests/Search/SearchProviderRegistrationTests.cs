using Bsft.Data.Contracts.Search;
using Bsft.Data.Search;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Bsft.Data.Tests.Search;

/// <summary>
/// The three provider packages register themselves through this, so what it guarantees about the
/// declaration is what all three guarantee.
/// </summary>
public class SearchProviderRegistrationTests
{
    [Fact]
    public void Add_RegistersTheProviderOnce()
    {
        var services = new ServiceCollection();

        Add(services, new DeclaredOptions());
        Add(services, new DeclaredOptions());

        using var provider = services.BuildServiceProvider();
        provider.GetServices<ISearchProvider>().Should().ContainSingle()
            .Which.Should().BeOfType<DeclaringSearchProvider>();
    }

    [Fact]
    public void Add_MutatedAfterTheCall_DoesNotChangeWhatWasRegistered()
    {
        // The copy has to be taken at the call, not when the options are first asked for: a host that
        // keeps building on its own instance would otherwise hand the provider the later state.
        var options = new DeclaredOptions { Group = "profile" };
        var services = new ServiceCollection();
        Add(services, options);

        options.Group = "something-else";

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IOptions<DeclaredOptions>>().Value.Group.Should().Be("profile");
    }

    [Fact]
    public void Add_ValidatesItsArguments()
    {
        var services = new ServiceCollection();

        FluentActions.Invoking(() => Add(null!, new DeclaredOptions()))
            .Should().Throw<ArgumentNullException>().WithParameterName("services");
        FluentActions.Invoking(() => Add(services, null!))
            .Should().Throw<ArgumentNullException>().WithParameterName("options");
        FluentActions.Invoking(() => SearchProviderRegistration
                .Add<DeclaredOptions, DeclaringSearchProvider>(services, new DeclaredOptions(), null!))
            .Should().Throw<ArgumentNullException>().WithParameterName("copyTo");
    }

    private static void Add(IServiceCollection services, DeclaredOptions options) =>
        SearchProviderRegistration.Add<DeclaredOptions, DeclaringSearchProvider>(
            services, options, static (from, to) => to.Group = from.Group);

    private sealed class DeclaredOptions
    {
        public string? Group { get; set; }
    }

    private sealed class DeclaringSearchProvider : SearchProviderBase
    {
        public override IQueryable<RankedItem<TEntity>> ApplySearch<TEntity>(
            IQueryable<TEntity> source,
            ResolvedSearchRequest query) => throw new NotSupportedException();

        protected override IEnumerable<string> Describe(SearchGroupConfiguration group) => [];
    }
}
