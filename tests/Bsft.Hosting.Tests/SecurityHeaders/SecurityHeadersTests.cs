using Bsft.Hosting.DependencyInjection;
using Bsft.Hosting.SecurityHeaders;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace Bsft.Hosting.Tests.SecurityHeaders;

public class SecurityHeadersTests
{
    private static async Task<IHost> StartAsync(
        RequestDelegate handler,
        Action<BsftSecurityHeadersOptions>? configure = null,
        IConfiguration? configuration = null,
        bool register = true)
    {
        return await new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    if (register && configuration is null)
                    {
                        services.AddBsftSecurityHeaders(configure);
                    }
                    else if (register)
                    {
                        services.AddBsftSecurityHeaders(configuration!, configure);
                    }
                });
                web.Configure(app =>
                {
                    app.UseBsftSecurityHeaders();
                    app.Run(handler);
                });
            })
            .StartAsync();
    }

    private static Task Ok(HttpContext context) => context.Response.WriteAsync("ok");

    private static async Task<HttpResponseMessage> GetAsync(IHost host, string path = "/")
    {
        using var client = host.GetTestClient();
        return await client.GetAsync(path);
    }

    private static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? string.Join(",", values) : null;

    [Fact]
    public async Task UseBsftSecurityHeaders_WithDefaults_SendsTheSecureBaseline()
    {
        using var host = await StartAsync(Ok);

        var response = await GetAsync(host);

        Header(response, "X-Content-Type-Options").Should().Be("nosniff");
        Header(response, "X-Frame-Options").Should().Be("DENY");
        Header(response, "Referrer-Policy").Should().Be("no-referrer");
        Header(response, "Content-Security-Policy").Should().Be("frame-ancestors 'none'");
        Header(response, "Permissions-Policy").Should().BeNull();
        Header(response, "Strict-Transport-Security").Should().BeNull();
    }

    [Fact]
    public async Task UseBsftSecurityHeaders_WithoutRegistration_SendsTheDefaults()
    {
        using var host = await StartAsync(Ok, register: false);

        var response = await GetAsync(host);

        Header(response, "X-Content-Type-Options").Should().Be("nosniff");
    }

    [Fact]
    public async Task UseBsftSecurityHeaders_WithConfiguredValues_SendsThem()
    {
        using var host = await StartAsync(Ok, options =>
        {
            options.FrameOptions = "SAMEORIGIN";
            options.ReferrerPolicy = "strict-origin";
            options.ContentSecurityPolicy = "default-src 'none'";
            options.PermissionsPolicy = "camera=()";
        });

        var response = await GetAsync(host);

        Header(response, "X-Frame-Options").Should().Be("SAMEORIGIN");
        Header(response, "Referrer-Policy").Should().Be("strict-origin");
        Header(response, "Content-Security-Policy").Should().Be("default-src 'none'");
        Header(response, "Permissions-Policy").Should().Be("camera=()");
    }

    [Fact]
    public async Task UseBsftSecurityHeaders_WithBoundConfiguration_SendsTheBoundValues()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Bsft:Hosting:SecurityHeaders:ReferrerPolicy"] = "same-origin",
            ["Bsft:Hosting:SecurityHeaders:ContentSecurityPolicyExemptPaths:0"] = "/docs",
        }).Build();
        using var host = await StartAsync(Ok, configuration: configuration);

        var page = await GetAsync(host, "/docs/index");

        Header(page, "Referrer-Policy").Should().Be("same-origin");
        Header(page, "Content-Security-Policy").Should().BeNull();
    }

    [Fact]
    public async Task UseBsftSecurityHeaders_WithAHeaderSetToNullOrEmpty_SendsNoSuchHeader()
    {
        using var host = await StartAsync(Ok, options =>
        {
            options.ContentTypeOptions = null;
            options.FrameOptions = "";
            options.ContentSecurityPolicy = null;
        });

        var response = await GetAsync(host);

        Header(response, "X-Content-Type-Options").Should().BeNull();
        Header(response, "X-Frame-Options").Should().BeNull();
        Header(response, "Content-Security-Policy").Should().BeNull();
        Header(response, "Referrer-Policy").Should().Be("no-referrer");
    }

    [Fact]
    public async Task UseBsftSecurityHeaders_WhenTheEndpointSetsAHeader_KeepsTheEndpointValue()
    {
        using var host = await StartAsync(context =>
        {
            context.Response.Headers.ContentSecurityPolicy = "sandbox";
            context.Response.Headers.XFrameOptions = "SAMEORIGIN";
            return Ok(context);
        });

        var response = await GetAsync(host);

        Header(response, "Content-Security-Policy").Should().Be("sandbox");
        Header(response, "X-Frame-Options").Should().Be("SAMEORIGIN");
        Header(response, "X-Content-Type-Options").Should().Be("nosniff");
    }

    [Fact]
    public async Task UseBsftSecurityHeaders_OnAnErrorResponse_StillSendsTheHeaders()
    {
        using var host = await StartAsync(context =>
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            return Task.CompletedTask;
        });

        var response = await GetAsync(host);

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.InternalServerError);
        Header(response, "X-Content-Type-Options").Should().Be("nosniff");
    }

    [Theory]
    [InlineData("/swagger", false)]
    [InlineData("/swagger/index.html", false)]
    [InlineData("/swaggerish", true)]
    [InlineData("/api", true)]
    public async Task UseBsftSecurityHeaders_OnAnExemptPath_OmitsOnlyTheContentSecurityPolicy(string path, bool hasPolicy)
    {
        using var host = await StartAsync(Ok, options => options.ContentSecurityPolicyExemptPaths.Add("/swagger"));

        var response = await GetAsync(host, path);

        (Header(response, "Content-Security-Policy") is not null).Should().Be(hasPolicy);
        Header(response, "X-Content-Type-Options").Should().Be("nosniff");
    }

    [Fact]
    public async Task UseBsftSecurityHeaders_WhenAnExceptionHandlerReExecutesOnAnotherPath_KeepsTheExemptionOfTheRequestPath()
    {
        using var host = await new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services => services.AddBsftSecurityHeaders(options => options.ContentSecurityPolicyExemptPaths.Add("/swagger")));
                web.Configure(app =>
                {
                    app.UseBsftSecurityHeaders();
                    app.UseExceptionHandler("/error");
                    app.Map("/error", error => error.Run(context =>
                    {
                        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                        return Ok(context);
                    }));
                    app.Run(_ => throw new InvalidOperationException("boom"));
                });
            })
            .StartAsync();

        var response = await GetAsync(host, "/swagger/index.html");

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.InternalServerError);
        Header(response, "Content-Security-Policy").Should().BeNull();
        Header(response, "X-Content-Type-Options").Should().Be("nosniff");
    }

    [Fact]
    public async Task AddBsftSecurityHeaders_WithAnInvalidValue_FailsHostStart()
    {
        var act = () => StartAsync(Ok, options => options.FrameOptions = "nope");

        (await act.Should().ThrowAsync<OptionsValidationException>()).Which.Message.Should().Contain("FrameOptions");
    }

    [Fact]
    public void AddBsftSecurityHeaders_WithNullArguments_Throws()
    {
        var services = new ServiceCollection();

        ((Action)(() => HeadersServiceCollectionExtensions.AddBsftSecurityHeaders(null!))).Should().Throw<ArgumentNullException>();
        ((Action)(() => services.AddBsftSecurityHeaders((IConfiguration)null!))).Should().Throw<ArgumentNullException>();
        ((Action)(() => HeadersServiceCollectionExtensions.AddBsftSecurityHeaders(null!, new ConfigurationBuilder().Build()))).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void UseBsftSecurityHeaders_WithNullBuilder_Throws()
    {
        var act = () => HeadersApplicationBuilderExtensions.UseBsftSecurityHeaders(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task SecurityHeadersMiddleware_WithNullArguments_Throws()
    {
        var options = Options.Create(new BsftSecurityHeadersOptions());

        ((Action)(() => new SecurityHeadersMiddleware(null!, options))).Should().Throw<ArgumentNullException>();
        ((Action)(() => new SecurityHeadersMiddleware(Ok, null!))).Should().Throw<ArgumentNullException>();
        await ((Func<Task>)(() => new SecurityHeadersMiddleware(Ok, options).InvokeAsync(null!))).Should().ThrowAsync<ArgumentNullException>();
    }
}
