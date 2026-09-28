using Bsft.Correlation;
using Bsft.Data.MultiTenancy;
using Bsft.Testing.Contexts;
using Bsft.Testing.Tests.Support;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Bsft.Correlation.DependencyInjection;
using Bsft.Testing.DependencyInjection;

namespace Bsft.Testing.Tests.DependencyInjection;

public class TestingServiceCollectionExtensionsTests
{
    [Fact]
    public void Test_tenant_serves_both_tenant_abstractions()
    {
        var services = new ServiceCollection();
        services.AddScoped<ITenantContext, TenantContext>();
        var tenant = new TestTenantContext("acme");

        services.AddBsftTestTenant(tenant);

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ITenantContext>().Should().BeSameAs(tenant);
        provider.GetRequiredService<TestTenantContext>().Should().BeSameAs(tenant);
    }

    [Fact]
    public void Test_correlation_replaces_the_concrete_context_the_middleware_uses()
    {
        var services = new ServiceCollection();
        services.AddBsftCorrelation();
        var correlation = new TestCorrelationContext("fixed");

        services.AddBsftTestCorrelation(correlation);

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<CorrelationContext>().Should().BeSameAs(correlation);
        provider.GetRequiredService<ICorrelationContext>().Should().BeSameAs(correlation);
        provider.GetRequiredService<TestCorrelationContext>().Should().BeSameAs(correlation);
    }

    [Fact]
    public void Test_contexts_registers_both_defaults()
    {
        var services = new ServiceCollection();

        services.AddBsftTestContexts();

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ITenantContext>().TenantId.Should().Be(TestTenantContext.DefaultTenantId);
        provider.GetRequiredService<ICorrelationContext>().CorrelationId
            .Should().Be(TestCorrelationContext.DefaultCorrelationId);
    }

    [Fact]
    public async Task Test_database_replaces_the_configured_provider()
    {
        var services = new ServiceCollection();
        services.AddDbContext<TestDbContext>(options => options.UseSqlServer("Server=nowhere;Database=none"));

        services.AddBsftTestDatabase<TestDbContext>(nameof(Test_database_replaces_the_configured_provider));

        using var provider = services.BuildServiceProvider();
        await using var context = provider.GetRequiredService<TestDbContext>();
        context.Database.ProviderName.Should().Be("Microsoft.EntityFrameworkCore.InMemory");
        context.Notes.Add(new TestNote { Text = "written without a server" });
        await context.SaveChangesAsync();
        context.Notes.Should().ContainSingle();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Test_database_rejects_a_blank_name(string? databaseName)
    {
        var register = () => new ServiceCollection().AddBsftTestDatabase<TestDbContext>(databaseName!);

        register.Should().Throw<ArgumentException>().WithParameterName("databaseName");
    }

    [Fact]
    public void Null_arguments_are_rejected()
    {
        var services = new ServiceCollection();

        var calls = new List<Action>
        {
            () => TestingServiceCollectionExtensions.AddBsftTestTenant(null!, new TestTenantContext()),
            () => services.AddBsftTestTenant(null!),
            () => TestingServiceCollectionExtensions.AddBsftTestCorrelation(null!, new TestCorrelationContext()),
            () => services.AddBsftTestCorrelation(null!),
            () => TestingServiceCollectionExtensions.AddBsftTestContexts(null!),
            () => TestingServiceCollectionExtensions.AddBsftTestDatabase<TestDbContext>(null!, "name"),
        };

        calls.Should().AllSatisfy(call => call.Should().Throw<ArgumentNullException>());
    }
}
