using Bsft.Data.Search;
using Bsft.Data.Filtering;
using Bsft.Data.Contracts.Filtering;
using Bsft.Data.Contracts.Pagination;
using Bsft.Data.Tests.Support;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Bsft.Core;
using Bsft.Data.DependencyInjection;
using Bsft.Data.Diagnostics;

namespace Bsft.Data.Tests.Search;

public class SearchRegistrationTests
{
    [Fact]
    public void TheSearchExecutorIsRegisteredAlongsideTheQueryExecutor()
    {
        using var sp = BuildProvider();
        using var scope = sp.CreateScope();

        scope.ServiceProvider.GetService<ISearchExecutor<Candidate, CandidateDto>>().Should().NotBeNull();
        scope.ServiceProvider.GetService<IQueryExecutor<Candidate, CandidateDto>>().Should().NotBeNull();
    }

    [Fact]
    public void TheSearchExecutorIsScopedSoItsScopeProviderIsTheCallersOwn()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ISearchProvider, FakeSearchProvider>();
        services.AddBsftDataQueryFiltering(c => c.FromAssembly(typeof(CandidateQueryConfiguration).Assembly));

        services.Where(d => d.ServiceType == typeof(ISearchExecutor<Candidate, CandidateDto>))
            .Should().NotBeEmpty()
            .And.OnlyContain(d => d.Lifetime == ServiceLifetime.Scoped);
    }

    [Fact]
    public void TheCompiledConfigurationAnswersAsAGroupSource()
    {
        using var sp = BuildProvider();

        var compiled = sp.GetRequiredService<CompiledSearchConfiguration<Candidate, CandidateDto>>();
        sp.GetServices<ISearchGroupSource>().Should().Contain(s => ReferenceEquals(s, compiled));
    }

    [Fact]
    public void TheStartupValidatorIsRegisteredOnce()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ISearchProvider, FakeSearchProvider>();
        services.AddBsftDataQueryFiltering(c => c.FromAssembly(typeof(CandidateQueryConfiguration).Assembly));
        services.AddBsftDataQueryFiltering(c => c.FromAssembly(typeof(CandidateQueryConfiguration).Assembly));

        using var sp = services.BuildServiceProvider();

        sp.GetServices<IStartupValidator>().OfType<SearchStartupValidator>().Should().ContainSingle();
    }

    [Fact]
    public void TheQueryConfigurationValidatorIsRegisteredOnceWithASourcePerConfiguration()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ISearchProvider, FakeSearchProvider>();
        services.AddBsftDataQueryFiltering(c => c.FromAssembly(typeof(CandidateQueryConfiguration).Assembly));
        services.AddBsftDataQueryFiltering(c => c.FromAssembly(typeof(CandidateQueryConfiguration).Assembly));

        using var sp = services.BuildServiceProvider();

        sp.GetServices<IStartupValidator>().OfType<QueryConfigurationStartupValidator>().Should().ContainSingle();
        sp.GetServices<IQueryConfigurationSource>().Should().NotBeEmpty();
    }

    [Fact]
    public void TheCompiledConfigurationAnswersAsAQueryConfigurationSource()
    {
        using var sp = BuildProvider();

        var compiled = sp.GetRequiredService<CompiledSearchConfiguration<Candidate, CandidateDto>>();
        sp.GetServices<IQueryConfigurationSource>().Should().Contain(s => ReferenceEquals(s, compiled));
    }

    [Fact]
    public void DataTelemetryIsRegistered()
    {
        using var sp = BuildProvider();

        sp.GetService<IDataTelemetry>().Should().NotBeNull();
    }

    [Fact]
    public async Task StartupFailsWhenAGroupIsDeclaredWithoutAProvider()
    {
        var services = new ServiceCollection();
        services.AddBsftDataQueryFiltering(c => c.FromAssembly(typeof(CandidateQueryConfiguration).Assembly));

        using var sp = services.BuildServiceProvider();
        var validator = sp.GetServices<IStartupValidator>().OfType<SearchStartupValidator>().Single();

        var result = await validator.ValidateAsync();

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains(nameof(ISearchProvider), StringComparison.Ordinal));
    }

    [Fact]
    public void TheContainerValidatesWithoutASearchProviderSoTheValidatorsMessageIsTheOneReached()
    {
        // The regression: AddBsftDefaults builds the container with ValidateOnBuild, which walks
        // every descriptor including the scoped ISearchExecutor. While the executor demanded an
        // ISearchProvider, that walk threw a generic activation error at builder.Build() — before
        // any startup validator ran. It hit every host on a provider the framework ships none for,
        // whether or not it had declared a single search group.
        var services = new ServiceCollection();
        services.AddBsftDataQueryFiltering(c => c.FromAssembly(typeof(CandidateQueryConfiguration).Assembly));

        var act = () => services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        }).Dispose();

        act.Should().NotThrow();
    }

    [Fact]
    public async Task AnApplicationWithoutASearchProviderStillServesPagedQueries()
    {
        // The other half of the promise: a host that registers no provider does not merely start, it
        // serves the plain paged queries it did ask for.
        var services = new ServiceCollection();
        services.AddDbContext<CandidateContext>(o => o.UseInMemoryDatabase(nameof(AnApplicationWithoutASearchProviderStillServesPagedQueries)));
        services.AddBsftDataQueryFiltering(c => c.FromAssembly(typeof(CandidateQueryConfiguration).Assembly));

        using var sp = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
        using var scope = sp.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<CandidateContext>();
        context.Candidates.AddRange(
            new Candidate { Id = 1, Name = "Ada" },
            new Candidate { Id = 2, Name = "Grace" },
            new Candidate { Id = 3, Name = "Alan" });
        await context.SaveChangesAsync();

        scope.ServiceProvider.GetService<ISearchProvider>().Should().BeNull("no provider is registered");
        scope.ServiceProvider.GetService<ISearchExecutor<Candidate, CandidateDto>>().Should().NotBeNull();

        var executor = scope.ServiceProvider.GetRequiredService<IQueryExecutor<Candidate, CandidateDto>>();
        var result = await executor.ExecuteAsync(context.Candidates, new PagedRequest(Page: 1, PageSize: 2));

        result.TotalCount.Should().Be(3);
        result.Items.Should().HaveCount(2);
    }

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ISearchProvider, FakeSearchProvider>();
        services.AddBsftDataQueryFiltering(c => c.FromAssembly(typeof(CandidateQueryConfiguration).Assembly));
        return services.BuildServiceProvider();
    }
}
