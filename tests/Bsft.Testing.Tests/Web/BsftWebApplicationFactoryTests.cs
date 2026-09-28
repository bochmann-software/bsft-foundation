using Bsft.Testing.Contexts;
using Bsft.Testing.Tests.Support;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Bsft.Testing.Tests.Web;

public class BsftWebApplicationFactoryTests
{
    [Fact]
    public async Task Request_arrives_in_the_test_tenant_with_the_test_correlation()
    {
        using var factory = new TestAppFactory();
        using var client = factory.CreateClient();

        var body = await client.GetStringAsync("/context");

        body.Should().Be(string.Join(
            '|',
            TestTenantContext.DefaultTenantId,
            TestCorrelationContext.DefaultCorrelationId));
    }

    [Fact]
    public async Task Overridden_doubles_are_what_the_application_sees()
    {
        using var factory = new OtherContextsTestAppFactory();
        using var client = factory.CreateClient();

        var body = await client.GetStringAsync("/context");

        body.Should().Be("other-tenant|other-correlation");
    }

    [Fact]
    public async Task Database_is_served_from_memory_instead_of_the_configured_server()
    {
        using var factory = new TestAppFactory();
        using var client = factory.CreateClient();

        var body = await client.GetStringAsync("/notes");

        body.Should().Be("Microsoft.EntityFrameworkCore.InMemory|1");
    }

    [Fact]
    public void Missing_host_builder_is_rejected()
    {
        using var factory = new ProbeFactory();

        var configure = () => factory.ConfigureWebHostWith(null!);

        configure.Should().Throw<ArgumentNullException>().WithParameterName("builder");
    }

    [Fact]
    public void Application_boots_in_the_testing_environment()
    {
        using var factory = new TestAppFactory();

        factory.Services.GetRequiredService<IHostEnvironment>().EnvironmentName.Should().Be("Testing");
    }

    [Fact]
    public void Overridden_environment_is_the_one_the_application_boots_in()
    {
        using var factory = new CustomEnvironmentTestAppFactory();

        factory.Services.GetRequiredService<IHostEnvironment>().EnvironmentName
            .Should().Be(CustomEnvironmentTestAppFactory.Environment);
    }

    [Fact]
    public async Task Doubles_resolved_from_the_host_steer_the_next_request()
    {
        using var factory = new TestAppFactory();
        using var client = factory.CreateClient();
        var tenant = factory.Services.GetRequiredService<TestTenantContext>();

        tenant.SetTenant("switched");
        var body = await client.GetStringAsync("/context");

        body.Should().StartWith("switched|");
    }
}
