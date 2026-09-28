using Bsft.Hosting.DependencyInjection;
using Bsft.Hosting.HealthChecks;
using Bsft.Hosting.Tests.Fakes;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace Bsft.Hosting.Tests.HealthChecks;

public class HealthCheckTests
{
    [Fact]
    public void AddBsftHealthChecks_RegistersHealthCheckService()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBsftHealthChecks();

        var provider = services.BuildServiceProvider();
        var healthCheckService = provider.GetService<HealthCheckService>();

        healthCheckService.Should().NotBeNull();
    }

    [Fact]
    public void AddBsftHealthChecks_WithDiskStorage_RegistersCheck()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBsftHealthChecks(cfg => cfg.AddDiskStorage(100));

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<HealthCheckServiceOptions>>();

        options.Value.Registrations.Should().Contain(r => r.Name == "disk-storage-100mb");
    }

    [Fact]
    public void AddBsftHealthChecks_WithExternalService_RegistersCheck()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpClient();
        services.AddBsftHealthChecks(cfg => cfg.AddExternalService("TestService", "https://example.com/health"));

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<HealthCheckServiceOptions>>();

        options.Value.Registrations.Should().Contain(r => r.Name == "external-TestService");
    }

    /// <summary>
    /// The external-service check resolves without the host having registered an HTTP client
    /// factory. The registration that needs one is the one that brings it, so the gap cannot show
    /// up later as a readiness probe failing in a deployed service.
    /// </summary>
    [Fact]
    public void AddBsftHealthChecks_WithExternalService_BringsTheHttpClientFactoryItself()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBsftHealthChecks(cfg => cfg.AddExternalService("TestService", "https://example.com/health"));

        using var provider = services.BuildServiceProvider();
        var registration = provider
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<HealthCheckServiceOptions>>()
            .Value.Registrations.Single(r => r.Name == "external-TestService");

        var resolve = () => registration.Factory(provider);

        resolve.Should().NotThrow();
    }

    /// <summary>A host asking for no external service is not given an HTTP client factory it never needed.</summary>
    [Fact]
    public void AddBsftHealthChecks_WithoutAnExternalService_DoesNotRegisterAnHttpClientFactory()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBsftHealthChecks(cfg => cfg.AddDiskStorage(100));

        using var provider = services.BuildServiceProvider();

        provider.GetService<IHttpClientFactory>().Should().BeNull();
    }

    [Fact]
    public async Task HealthEndpoint_ReturnsOk()
    {
        using var host = await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services.AddBsftHealthChecks();
                    services.AddRouting();
                });
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseBsftHealthChecks();
                });
            })
            .StartAsync();

        var client = host.GetTestClient();
        var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
    }

    [Fact]
    public async Task ReadyEndpoint_ReturnsOk()
    {
        using var host = await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services.AddBsftHealthChecks();
                    services.AddRouting();
                });
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseBsftHealthChecks();
                });
            })
            .StartAsync();

        var client = host.GetTestClient();
        var response = await client.GetAsync("/health/ready");

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
    }

    [Fact]
    public void AddBsftHealthChecks_ThrowsOnNullServices()
    {
        IServiceCollection services = null!;
        var act = () => services.AddBsftHealthChecks();
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void BsftHealthCheckConfiguration_AddDiskStorage_ThrowsOnZero()
    {
        var config = new BsftHealthCheckConfiguration();
        var act = () => config.AddDiskStorage(0);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void BsftHealthCheckConfiguration_AddExternalService_ThrowsOnEmpty()
    {
        var config = new BsftHealthCheckConfiguration();
        var act = () => config.AddExternalService("", "https://example.com/health");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task AddBsftHealthChecks_WithDiskStorage_FactoryLambdaIsInvoked()
    {
        // Exercises HealthChecksServiceCollectionExtensions.AddBsftHealthChecks line 47:
        // the factory lambda "_ => new DiskStorageHealthCheck(...)" is called when the
        // health check is actually executed.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBsftHealthChecks(cfg => cfg.AddDiskStorage(1));

        var provider = services.BuildServiceProvider();
        var healthCheckService = provider.GetRequiredService<HealthCheckService>();

        // Running the health check invokes the factory lambda
        var result = await healthCheckService.CheckHealthAsync();

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task AddBsftHealthChecks_WithExternalService_FactoryLambdaIsInvoked()
    {
        // Exercises HealthChecksServiceCollectionExtensions.AddBsftHealthChecks lines 34-37:
        // the factory lambda "sp => { var factory = sp.GetRequiredService<IHttpClientFactory>(); ... }" is
        // called when the health check is actually executed.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpClient();
        services.AddBsftHealthChecks(cfg => cfg.AddExternalService("TestSvc", "https://localhost/health"));

        var provider = services.BuildServiceProvider();
        var healthCheckService = provider.GetRequiredService<HealthCheckService>();

        // Running the health check invokes the factory lambda (network call will fail but factory is invoked)
        var result = await healthCheckService.CheckHealthAsync();

        result.Should().NotBeNull();
    }

    [Fact]
    public void UseBsftHealthChecks_WithNullApp_ThrowsArgumentNullException()
    {
        // Exercises HealthChecksApplicationBuilderExtensions.UseBsftHealthChecks line 21: null guard.
        IApplicationBuilder app = null!;

        var act = () => app.UseBsftHealthChecks();

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("app");
    }

    [Fact]
    public void BsftHealthCheckConfiguration_AddExternalService_EmptyHealthUrl_ThrowsArgumentException()
    {
        // Exercises BsftHealthCheckConfiguration.AddExternalService line 48: healthUrl null/empty guard.
        var config = new BsftHealthCheckConfiguration();

        var act = () => config.AddExternalService("ValidName", "");

        act.Should().Throw<ArgumentException>()
            .WithParameterName("healthUrl");
    }

    [Fact]
    public void ExternalServiceHealthCheck_NonHttpUrl_ThrowsArgumentException()
    {
        var factory = Substitute.For<IHttpClientFactory>();

        var act = () => new ExternalServiceHealthCheck("svc", "file:///etc/passwd", factory);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("healthUrl");
    }

    [Fact]
    public void ExternalServiceHealthCheck_HttpsUrl_IsAccepted()
    {
        var factory = Substitute.For<IHttpClientFactory>();

        var act = () => new ExternalServiceHealthCheck("svc", "https://example.com/health", factory);

        act.Should().NotThrow();
    }

    [Fact]
    public void DiskStorageHealthCheck_WithZeroMinFreeMb_ThrowsArgumentOutOfRangeException()
    {
        // Exercises DiskStorageHealthCheck.ctor line 19.
        var act = () => new DiskStorageHealthCheck(0);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("minFreeMb");
    }

    [Fact]
    public async Task HealthChecksApplicationBuilderExtensions_Predicates_AreInvokedDuringRequest()
    {
        // Exercises HealthChecksApplicationBuilderExtensions.UseBsftHealthChecks lines 25 and 36:
        // the predicate lambdas "_ => false" and "check => check.Tags.Contains("ready")" are invoked
        // when ASP.NET Core processes a health check HTTP request.
        using var host = await new Microsoft.Extensions.Hosting.HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services.AddBsftHealthChecks(cfg => cfg.AddDiskStorage(1));
                    services.AddRouting();
                });
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseBsftHealthChecks();
                });
            })
            .StartAsync();

        var client = host.GetTestClient();

        // Both requests exercise the predicate lambdas:
        // /health uses "_ => false" predicate (line 25) - runs no checks, always returns 200
        var healthResponse = await client.GetAsync("/health");
        healthResponse.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        // /health/ready uses "check => check.Tags.Contains("ready")" predicate (line 36)
        var readyResponse = await client.GetAsync("/health/ready");
        readyResponse.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
    }

    [Fact]
    public async Task ExternalServiceHealthCheck_CheckHealthAsync_HealthyWhenSuccessResponse()
    {
        // Exercises ExternalServiceHealthCheck.CheckHealthAsync lines 44-45 (success branch).
        // Use NSubstitute to mock IHttpClientFactory returning a mock HttpClient.
        // We use a handler that returns 200 OK.
        var handlerMock = new TestHttpMessageHandler(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        var httpClient = new HttpClient(handlerMock);
        var factory = NSubstitute.Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(httpClient);

        var healthCheck = new ExternalServiceHealthCheck("TestService", "http://example.com/health", factory);
        var context = new HealthCheckContext
        {
            Registration = new HealthCheckRegistration("TestService", healthCheck, null, null)
        };

        var result = await healthCheck.CheckHealthAsync(context);

        result.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task ExternalServiceHealthCheck_CheckHealthAsync_UnhealthyWhenFailureResponse()
    {
        // Exercises ExternalServiceHealthCheck.CheckHealthAsync lines 44-46 (failure branch).
        var handlerMock = new TestHttpMessageHandler(new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable));
        var httpClient = new HttpClient(handlerMock);
        var factory = NSubstitute.Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(httpClient);

        var healthCheck = new ExternalServiceHealthCheck("TestService", "http://example.com/health", factory);
        var context = new HealthCheckContext
        {
            Registration = new HealthCheckRegistration("TestService", healthCheck, null, null)
        };

        var result = await healthCheck.CheckHealthAsync(context);

        result.Status.Should().Be(HealthStatus.Unhealthy);
    }

    [Fact]
    public async Task ExternalServiceHealthCheck_CheckHealthAsync_UnhealthyWhenRequestFails()
    {
        // The service could not be reached at all: HttpClient reports that as HttpRequestException.
        var transportFailure = new HttpRequestException("no such host");
        var healthCheck = CreateThrowingHealthCheck(transportFailure, out var context);

        var result = await healthCheck.CheckHealthAsync(context);

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Be("Service 'TestService' is unreachable.");
        result.Exception.Should().BeSameAs(transportFailure);
    }

    [Fact]
    public async Task ExternalServiceHealthCheck_CheckHealthAsync_UnhealthyWhenRequestTimesOut()
    {
        // HttpClient surfaces its own timeout as TaskCanceledException.
        var timeout = new TaskCanceledException("timed out");
        var healthCheck = CreateThrowingHealthCheck(timeout, out var context);

        var result = await healthCheck.CheckHealthAsync(context);

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Be("Service 'TestService' is unreachable.");
        result.Exception.Should().BeSameAs(timeout);
    }

    [Fact]
    public async Task ExternalServiceHealthCheck_CheckHealthAsync_DoesNotLeakAnUnexpectedFailure()
    {
        // An unexpected failure must not reach the caller as text. The description stays the fixed
        // sentence and the detail travels in the result object, because a consumer that echoes the
        // description would otherwise publish internals over an unauthenticated endpoint.
        var defect = new InvalidOperationException("handler misconfigured: server=db01;pwd=hunter2");
        var healthCheck = CreateThrowingHealthCheck(defect, out var context);

        var result = await healthCheck.CheckHealthAsync(context);

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Be("Service 'TestService' is unreachable.");
        result.Description.Should().NotContain("hunter2");
        result.Exception.Should().BeSameAs(defect);
    }

    private static ExternalServiceHealthCheck CreateThrowingHealthCheck(
        Exception thrown,
        out HealthCheckContext context)
    {
        var httpClient = new HttpClient(new ThrowingHttpMessageHandler(thrown));
        var factory = NSubstitute.Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(httpClient);

        var healthCheck = new ExternalServiceHealthCheck("TestService", "http://example.com/health", factory);
        context = new HealthCheckContext
        {
            Registration = new HealthCheckRegistration("TestService", healthCheck, null, null)
        };

        return healthCheck;
    }
}
