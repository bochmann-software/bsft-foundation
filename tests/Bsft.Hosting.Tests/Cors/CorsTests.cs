using Bsft.Hosting.Cors;
using Bsft.Hosting.DependencyInjection;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Bsft.Hosting.Tests.Cors;

public class CorsTests
{
    [Fact]
    public void AddBsftCors_WithDefaults_RegistersCorsPolicy()
    {
        var services = new ServiceCollection();
        services.AddBsftCors();

        var provider = services.BuildServiceProvider();
        var corsOptions = provider.GetRequiredService<IOptions<CorsOptions>>();

        corsOptions.Value.Should().NotBeNull();
        var policy = corsOptions.Value.GetPolicy(HostingDefaults.CorsPolicyName);
        policy.Should().NotBeNull();
    }

    [Fact]
    public void AddBsftCors_WithDefaults_AllowsNoOrigins()
    {
        var services = new ServiceCollection();
        services.AddBsftCors();

        var provider = services.BuildServiceProvider();
        var corsOptions = provider.GetRequiredService<IOptions<CorsOptions>>();
        var policy = corsOptions.Value.GetPolicy(HostingDefaults.CorsPolicyName)!;

        policy.AllowAnyOrigin.Should().BeFalse();
        policy.Origins.Should().BeEmpty();
    }

    [Fact]
    public void AddBsftCors_WithOrigins_ConfiguresOrigins()
    {
        var services = new ServiceCollection();
        services.AddBsftCors(o => o.AllowOrigins("https://example.com", "https://test.com"));

        var provider = services.BuildServiceProvider();
        var corsOptions = provider.GetRequiredService<IOptions<CorsOptions>>();
        var policy = corsOptions.Value.GetPolicy(HostingDefaults.CorsPolicyName)!;

        policy.Origins.Should().Contain("https://example.com");
        policy.Origins.Should().Contain("https://test.com");
    }

    [Fact]
    public void AddBsftCors_AllowAny_AllowsAllOrigins()
    {
        var services = new ServiceCollection();
        services.AddBsftCors(o => o.AllowAny());

        var provider = services.BuildServiceProvider();
        var corsOptions = provider.GetRequiredService<IOptions<CorsOptions>>();
        var policy = corsOptions.Value.GetPolicy(HostingDefaults.CorsPolicyName)!;

        policy.AllowAnyOrigin.Should().BeTrue();
    }

    [Fact]
    public void AddBsftCors_AllowCredentials_SetsCredentialsFlag()
    {
        var services = new ServiceCollection();
        services.AddBsftCors(o => o
            .AllowOrigins("https://example.com")
            .AllowCredentials());

        var provider = services.BuildServiceProvider();
        var corsOptions = provider.GetRequiredService<IOptions<CorsOptions>>();
        var policy = corsOptions.Value.GetPolicy(HostingDefaults.CorsPolicyName)!;

        policy.SupportsCredentials.Should().BeTrue();
    }

    [Fact]
    public void AddBsftCors_ThrowsOnNullServices()
    {
        IServiceCollection services = null!;
        var act = () => services.AddBsftCors();
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AllowOrigins_WithNullOrigins_ThrowsArgumentNullException()
    {
        // Exercises BsftCorsOptions.AllowOrigins line 22: null guard.
        var options = new BsftCorsOptions();

        var act = () => options.AllowOrigins(null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("origins");
    }

    [Fact]
    public void UseBsftCors_WithNullApp_ThrowsArgumentNullException()
    {
        // Exercises CorsApplicationBuilderExtensions.UseBsftCors lines 16-18: null guard (branch not taken path).
        IApplicationBuilder app = null!;

        var act = () => app.UseBsftCors();

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("app");
    }

    [Fact]
    public void UseBsftCors_WithValidApp_ReturnsApp()
    {
        // Exercises CorsApplicationBuilderExtensions.UseBsftCors lines 20-21.
        var services = new ServiceCollection();
        services.AddBsftCors();
        var provider = services.BuildServiceProvider();
        var app = new Microsoft.AspNetCore.Builder.ApplicationBuilder(provider);

        var result = app.UseBsftCors();

        result.Should().NotBeNull();
    }


    [Fact]
    public void AddBsftCors_WithConfiguration_BindsSection()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Bsft:Hosting:Cors:Origins:0"] = "https://bound.example",
                ["Bsft:Hosting:Cors:AreCredentialsAllowed"] = "true"
            })
            .Build();
        var services = new ServiceCollection();

        services.AddBsftCors(configuration);

        using var provider = services.BuildServiceProvider();
        var policy = provider.GetRequiredService<IOptions<CorsOptions>>()
            .Value.GetPolicy(HostingDefaults.CorsPolicyName)!;

        policy.Origins.Should().ContainSingle().Which.Should().Be("https://bound.example");
        policy.SupportsCredentials.Should().BeTrue();
    }

    [Fact]
    public void AddBsftCors_WithConfigurationAndConfigure_LetsCodeWin()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Bsft:Hosting:Cors:Origins:0"] = "https://bound.example"
            })
            .Build();
        var services = new ServiceCollection();

        services.AddBsftCors(configuration, o => o.AllowOrigins("https://code.example"));

        using var provider = services.BuildServiceProvider();
        var policy = provider.GetRequiredService<IOptions<CorsOptions>>()
            .Value.GetPolicy(HostingDefaults.CorsPolicyName)!;

        policy.Origins.Should().BeEquivalentTo(["https://bound.example", "https://code.example"]);
    }

    [Fact]
    public void AddBsftCors_WithNullConfiguration_Throws()
    {
        var services = new ServiceCollection();

        var act = () => services.AddBsftCors(configuration: null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("configuration");
    }

    [Fact]
    public void AddBsftCors_WithConfigurationAndNullServices_Throws()
    {
        IServiceCollection services = null!;

        var act = () => services.AddBsftCors(new ConfigurationBuilder().Build());

        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public void AddBsftCors_WithBlankOrigin_FailsOnValidation()
    {
        var services = new ServiceCollection();
        services.AddBsftCors(o => o.AllowOrigins("   "));

        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IOptions<BsftCorsOptions>>().Value;

        act.Should().Throw<OptionsValidationException>().WithMessage("*Origins[0]*");
    }

    private static CorsPolicy GetPolicy(IServiceProvider provider, string name) =>
        provider.GetRequiredService<IOptions<CorsOptions>>().Value.GetPolicy(name)!;

    [Fact]
    public void BsftCorsOptions_ByDefault_ExposesNoHeadersHasNoPredicateAndUsesTheConstantPolicyName()
    {
        var options = new BsftCorsOptions();

        options.ExposedHeaders.Should().BeEmpty();
        options.OriginPredicate.Should().BeNull();
        options.PolicyName.Should().Be(HostingDefaults.CorsPolicyName);
    }

    [Fact]
    public void ExposeHeaders_WithNull_Throws()
    {
        var act = () => new BsftCorsOptions().ExposeHeaders(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("headers");
    }

    [Fact]
    public void AllowOriginsWhere_WithNull_Throws()
    {
        var act = () => new BsftCorsOptions().AllowOriginsWhere(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("predicate");
    }

    [Fact]
    public void AddBsftCors_WithExposedHeaders_ExposesThemOnThePolicy()
    {
        var services = new ServiceCollection();
        services.AddBsftCors(o => o.AllowOrigins("https://a.example").ExposeHeaders("ETag", "Location"));

        using var provider = services.BuildServiceProvider();

        GetPolicy(provider, HostingDefaults.CorsPolicyName).ExposedHeaders
            .Should().BeEquivalentTo(["ETag", "Location"]);
    }

    [Fact]
    public void AddBsftCors_WithExposedHeadersAndAnyOrigin_ExposesThem()
    {
        var services = new ServiceCollection();
        services.AddBsftCors(o => o.AllowAny().ExposeHeaders("ETag"));

        using var provider = services.BuildServiceProvider();

        GetPolicy(provider, HostingDefaults.CorsPolicyName).ExposedHeaders.Should().ContainSingle("ETag");
    }

    [Fact]
    public void AddBsftCors_WithConfiguration_BindsExposedHeadersAndPolicyName()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Bsft:Hosting:Cors:ExposedHeaders:0"] = "ETag",
                ["Bsft:Hosting:Cors:PolicyName"] = "Bound"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddBsftCors(configuration);

        using var provider = services.BuildServiceProvider();

        GetPolicy(provider, "Bound").ExposedHeaders.Should().ContainSingle("ETag");
    }

    [Fact]
    public void AddBsftCors_WithPredicate_AllowsMatchingOriginsAndExactOrigins()
    {
        var services = new ServiceCollection();
        services.AddBsftCors(o => o
            .AllowOrigins("https://exact.example")
            .AllowOriginsWhere(origin => origin.StartsWith("http://localhost", StringComparison.Ordinal))
            .AllowCredentials());

        using var provider = services.BuildServiceProvider();
        var policy = GetPolicy(provider, HostingDefaults.CorsPolicyName);

        policy.IsOriginAllowed("https://exact.example").Should().BeTrue();
        policy.IsOriginAllowed("http://localhost:5000").Should().BeTrue();
        policy.IsOriginAllowed("https://evil.example").Should().BeFalse();
        policy.SupportsCredentials.Should().BeTrue();
    }

    [Fact]
    public void AddBsftCors_WithoutPredicate_KeepsExactOriginMatching()
    {
        var services = new ServiceCollection();
        services.AddBsftCors(o => o.AllowOrigins("https://exact.example"));

        using var provider = services.BuildServiceProvider();
        var policy = GetPolicy(provider, HostingDefaults.CorsPolicyName);

        policy.IsOriginAllowed("https://exact.example").Should().BeTrue();
        policy.IsOriginAllowed("https://other.example").Should().BeFalse();
    }

    [Theory]
    [InlineData("https://exact.example")]
    [InlineData("https://EXACT.example")]
    [InlineData("https://exact.example/")]
    [InlineData("https://other.example")]
    [InlineData("http://exact.example")]
    public void IsOriginAllowed_WithoutPredicate_MatchesAspNetCoresPolicy(string origin)
    {
        var services = new ServiceCollection();
        services.AddBsftCors(o => o.AllowOrigins("https://Exact.example/"));
        using var provider = services.BuildServiceProvider();
        var reference = new CorsPolicyBuilder().WithOrigins("https://Exact.example/").Build();

        provider.GetRequiredService<IBsftCorsOriginEvaluator>().IsOriginAllowed(origin)
            .Should().Be(reference.AllowAnyOrigin || reference.IsOriginAllowed(origin));
    }

    [Theory]
    [InlineData("https://anything.example")]
    [InlineData("http://localhost")]
    public void IsOriginAllowed_WithWildcardOrigin_AllowsEveryOrigin(string origin)
    {
        var services = new ServiceCollection();
        services.AddBsftCors(o => o.AllowOrigins("*"));
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IBsftCorsOriginEvaluator>().IsOriginAllowed(origin).Should().BeTrue();
        GetPolicy(provider, HostingDefaults.CorsPolicyName).AllowAnyOrigin.Should().BeTrue();
    }

    [Fact]
    public void IsOriginAllowed_WithPredicate_MatchesThePolicy()
    {
        var services = new ServiceCollection();
        services.AddBsftCors(o => o
            .AllowOrigins("https://exact.example")
            .AllowOriginsWhere(candidate => candidate.StartsWith("http://localhost", StringComparison.Ordinal)));
        using var provider = services.BuildServiceProvider();
        var evaluator = provider.GetRequiredService<IBsftCorsOriginEvaluator>();
        var policy = GetPolicy(provider, HostingDefaults.CorsPolicyName);

        foreach (var origin in new[] { "https://exact.example", "http://localhost:5000", "https://evil.example" })
        {
            evaluator.IsOriginAllowed(origin).Should().Be(policy.IsOriginAllowed(origin));
        }
    }

    [Fact]
    public void AddBsftCors_WithHostEvaluator_PolicyFollowsIt()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IBsftCorsOriginEvaluator>(new FakeBsftCorsOriginEvaluator());
        services.AddBsftCors(o => o.AllowOrigins("https://exact.example"));
        using var provider = services.BuildServiceProvider();

        GetPolicy(provider, HostingDefaults.CorsPolicyName).IsOriginAllowed("https://other.example")
            .Should().BeTrue();
    }

    [Fact]
    public void AddBsftCors_WhenHostAddsSamePolicyAfterwards_HostPolicyWins()
    {
        var services = new ServiceCollection();
        services.AddBsftCors(o => o.AllowOrigins("https://bsft.example"));
        services.AddCors(c => c.AddPolicy(HostingDefaults.CorsPolicyName, p => p.WithOrigins("https://host.example")));
        using var provider = services.BuildServiceProvider();

        GetPolicy(provider, HostingDefaults.CorsPolicyName).Origins.Should().ContainSingle("https://host.example");
    }

    [Fact]
    public void IsOriginAllowed_WithoutOriginsOrPredicate_DeniesEverything()
    {
        var services = new ServiceCollection();
        services.AddBsftCors();

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IBsftCorsOriginEvaluator>()
            .IsOriginAllowed("https://any.example").Should().BeFalse();
    }

    [Fact]
    public void IsOriginAllowed_WithAnyOrigin_AllowsEverything()
    {
        var services = new ServiceCollection();
        services.AddBsftCors(o => o.AllowAny());

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IBsftCorsOriginEvaluator>()
            .IsOriginAllowed("https://any.example").Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsOriginAllowed_WithBlankOrigin_DeniesEvenWhenAnyOriginIsAllowed(string? origin)
    {
        var services = new ServiceCollection();
        services.AddBsftCors(o => o.AllowAny().AllowOriginsWhere(_ => true));
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IBsftCorsOriginEvaluator>().IsOriginAllowed(origin).Should().BeFalse();
    }

    private static async Task<HttpResponseMessage> SendCorsRequestAsync(
        Action<IServiceCollection> register,
        string origin,
        HttpMethod? method = null,
        Action<HttpRequestMessage>? adjust = null)
    {
        using var host = await new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(register)
                .Configure(app =>
                {
                    app.UseBsftCors();
                    app.Run(_ => Task.CompletedTask);
                }))
            .StartAsync();
        using var client = host.GetTestClient();
        using var request = new HttpRequestMessage(method ?? HttpMethod.Get, "/");
        request.Headers.Add("Origin", origin);
        adjust?.Invoke(request);

        return await client.SendAsync(request);
    }

    [Fact]
    public async Task UseBsftCors_WithHostEvaluatorAndAnyOrigin_BypassesTheEvaluator()
    {
        using var response = await SendCorsRequestAsync(services =>
        {
            services.AddSingleton<IBsftCorsOriginEvaluator>(new FakeBsftCorsOriginEvaluator(isAllowed: false));
            services.AddBsftCors(o => o.AllowAny());
        }, "https://x.example");

        response.Headers.GetValues("Access-Control-Allow-Origin").Should().ContainSingle("*");
        response.Headers.Contains("Vary").Should().BeFalse();
    }

    [Fact]
    public async Task UseBsftCors_WithHostEvaluatorWithoutPredicate_DrivesThePolicy()
    {
        using var denied = await SendCorsRequestAsync(services =>
        {
            services.AddSingleton<IBsftCorsOriginEvaluator>(new FakeBsftCorsOriginEvaluator(isAllowed: false));
            services.AddBsftCors(o => o.AllowOrigins("https://a.example"));
        }, "https://a.example");
        using var allowed = await SendCorsRequestAsync(services =>
        {
            services.AddSingleton<IBsftCorsOriginEvaluator>(new FakeBsftCorsOriginEvaluator(isAllowed: true));
            services.AddBsftCors();
        }, "https://other.example");

        denied.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
        allowed.Headers.GetValues("Access-Control-Allow-Origin").Should().ContainSingle("https://other.example");
        allowed.Headers.GetValues("Vary").Should().Contain("Origin");
    }

    [Fact]
    public async Task UseBsftCors_WithPredicate_AddsVaryOrigin()
    {
        using var response = await SendCorsRequestAsync(
            services => services.AddBsftCors(o => o.AllowOriginsWhere(_ => true)), "https://x.example");

        response.Headers.GetValues("Vary").Should().Contain("Origin");
    }

    [Fact]
    public void Constructor_WithNullRegistration_Throws()
    {
        var act = () => new BsftCorsOriginEvaluator(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("registration");
    }

    [Fact]
    public void Constructor_OfTheRegistrationWithNullOptions_Throws()
    {
        var act = () => new BsftCorsRegistration(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }

    [Fact]
    public void AddBsftCors_WhenHostConfiguredTheOptionsBefore_StillShowsThemInTheSharedOptions()
    {
        var services = new ServiceCollection();
        services.Configure<BsftCorsOptions>(o => o.AreCredentialsAllowed = true);
        services.AddBsftCors(o => o.AllowOrigins("https://a.example"));

        using var provider = services.BuildServiceProvider();
        var shared = provider.GetRequiredService<IOptions<BsftCorsOptions>>().Value;

        shared.AreCredentialsAllowed.Should().BeTrue();
        shared.Origins.Should().ContainSingle("https://a.example");
    }

    [Fact]
    public void AddBsftCors_WithEarlierPolicyOfAnotherNameAndPredicate_KeepsThatPolicyConsistent()
    {
        var services = new ServiceCollection();
        services.AddBsftCors(o =>
        {
            o.PolicyName = "First";
            o.AllowOriginsWhere(origin => origin == "https://first.example");
        });
        services.AddBsftCors(o => o.AllowOriginsWhere(origin => origin == "https://second.example"));
        using var provider = services.BuildServiceProvider();

        var first = GetPolicy(provider, "First");
        var last = GetPolicy(provider, HostingDefaults.CorsPolicyName);

        first.IsOriginAllowed("https://first.example").Should().BeTrue();
        first.IsOriginAllowed("https://second.example").Should().BeFalse();
        last.IsOriginAllowed("https://second.example").Should().BeTrue();
        provider.GetRequiredService<IBsftCorsOriginEvaluator>().IsOriginAllowed("https://second.example")
            .Should().BeTrue();
    }

    [Fact]
    public void AddBsftCors_WhenConfiguredAfterwardsThroughTheOptionsPipeline_ChangesNeitherPolicyNorEvaluator()
    {
        var services = new ServiceCollection();
        services.AddBsftCors(o => o.AllowOrigins("https://a.example"));
        services.Configure<BsftCorsOptions>(o => o.AllowOrigins("https://b.example"));

        using var provider = services.BuildServiceProvider();
        var policy = GetPolicy(provider, HostingDefaults.CorsPolicyName);
        var evaluator = provider.GetRequiredService<IBsftCorsOriginEvaluator>();

        policy.Origins.Should().ContainSingle("https://a.example");
        policy.IsOriginAllowed("https://b.example").Should().BeFalse();
        evaluator.IsOriginAllowed("https://b.example").Should().BeFalse();
        provider.GetRequiredService<IOptions<BsftCorsOptions>>().Value.Origins.Should().HaveCount(2);
    }

    [Fact]
    public async Task UseBsftCors_WhenALaterCallUsesTheDefaultName_AppliesTheDefaultName()
    {
        using var response = await SendCorsRequestAsync(services =>
        {
            services.AddBsftCors(o =>
            {
                o.PolicyName = "A";
                o.AllowOrigins("https://first.example");
            });
            services.AddBsftCors(o => o.AllowOrigins("https://second.example"));
        }, "https://second.example");

        response.Headers.GetValues("Access-Control-Allow-Origin").Should().ContainSingle("https://second.example");
    }

    [Fact]
    public async Task UseBsftCors_WithoutAddBsftCors_AppliesThePolicyOfTheDefaultName()
    {
        using var response = await SendCorsRequestAsync(
            services => services.AddCors(c => c.AddPolicy(HostingDefaults.CorsPolicyName, p => p.AllowAnyOrigin())),
            "https://any.example");

        response.Headers.GetValues("Access-Control-Allow-Origin").Should().ContainSingle("*");
    }

    [Fact]
    public async Task UseBsftCors_WithLaterCallWithoutPredicate_DropsThePredicateOfTheEarlierCall()
    {
        void Register(IServiceCollection services)
        {
            services.AddBsftCors(o => o.AllowOriginsWhere(_ => true));
            services.AddBsftCors(o => o.AllowOrigins("https://a.example"));
        }

        using var response = await SendCorsRequestAsync(Register, "https://other.example");

        response.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
    }

    [Fact]
    public void IsOriginAllowed_WithWildcardEntryNextToAnotherOrigin_TreatsTheWildcardAsAnOrdinaryEntry()
    {
        var services = new ServiceCollection();
        services.AddBsftCors(o => o.AllowOrigins("*", "https://a.example").AllowOriginsWhere(c => c == "https://p.example"));
        using var provider = services.BuildServiceProvider();
        var evaluator = provider.GetRequiredService<IBsftCorsOriginEvaluator>();

        evaluator.IsOriginAllowed("https://other.example").Should().BeFalse();
        evaluator.IsOriginAllowed("https://p.example").Should().BeTrue();
        evaluator.IsOriginAllowed("https://a.example").Should().BeTrue();
    }

    [Fact]
    public void IsOriginAllowed_WithDifferentlyCasedRequestOrigin_ComparesCaseSensitively()
    {
        var services = new ServiceCollection();
        services.AddBsftCors(o => o.AllowOrigins("https://a.example"));
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IBsftCorsOriginEvaluator>().IsOriginAllowed("https://A.example").Should().BeFalse();
    }

    [Fact]
    public void AddBsftCors_WhenEvaluatorAlreadyRegistered_KeepsIt()
    {
        var own = new FakeBsftCorsOriginEvaluator();
        var services = new ServiceCollection();
        services.AddSingleton<IBsftCorsOriginEvaluator>(own);
        services.AddBsftCors();

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IBsftCorsOriginEvaluator>().Should().BeSameAs(own);
    }

    [Fact]
    public async Task UseBsftCors_WithCustomPolicyName_AppliesThePolicyEndToEnd()
    {
        void Register(IServiceCollection services) => services.AddBsftCors(o =>
        {
            o.PolicyName = "Custom";
            o.AllowOriginsWhere(origin => origin == "https://pred.example");
            o.ExposeHeaders("ETag", "Location");
        });

        using var simple = await SendCorsRequestAsync(Register, "https://pred.example");
        using var preflight = await SendCorsRequestAsync(
            Register,
            "https://pred.example",
            HttpMethod.Options,
            request => request.Headers.Add("Access-Control-Request-Method", "GET"));
        using var denied = await SendCorsRequestAsync(Register, "https://evil.example");

        simple.Headers.GetValues("Access-Control-Allow-Origin").Should().ContainSingle("https://pred.example");
        simple.Headers.GetValues("Access-Control-Expose-Headers").Single()
            .Should().Contain("ETag").And.Contain("Location");
        preflight.Headers.GetValues("Access-Control-Allow-Origin").Should().ContainSingle("https://pred.example");
        denied.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
    }

    [Fact]
    public async Task UseBsftCors_WithDefaultPolicyName_AppliesThePolicyEndToEnd()
    {
        using var response = await SendCorsRequestAsync(
            services => services.AddBsftCors(o => o.AllowOrigins("https://a.example")), "https://a.example");

        response.Headers.GetValues("Access-Control-Allow-Origin").Should().ContainSingle("https://a.example");
    }

    [Fact]
    public async Task UseBsftCors_WhenAddBsftCorsIsCalledTwiceWithTheSameOrigin_SendsNoVary()
    {
        using var response = await SendCorsRequestAsync(services =>
        {
            services.AddBsftCors(o => o.AllowOrigins("https://a.example"));
            services.AddBsftCors(o => o.AllowOrigins("https://a.example"));
        }, "https://a.example");

        response.Headers.GetValues("Access-Control-Allow-Origin").Should().ContainSingle("https://a.example");
        response.Headers.Contains("Vary").Should().BeFalse();
    }

    [Fact]
    public async Task UseBsftCors_WhenAddBsftCorsIsCalledTwiceWithOtherOrigins_TheLastCallWins()
    {
        void Register(IServiceCollection services)
        {
            services.AddBsftCors(o => o.AllowOrigins("https://first.example"));
            services.AddBsftCors(o => o.AllowOrigins("https://second.example"));
        }

        using var first = await SendCorsRequestAsync(Register, "https://first.example");
        using var second = await SendCorsRequestAsync(Register, "https://second.example");

        first.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
        second.Headers.GetValues("Access-Control-Allow-Origin").Should().ContainSingle("https://second.example");
        second.Headers.Contains("Vary").Should().BeFalse();
    }

    [Fact]
    public async Task UseBsftCors_WithWildcardEntryAndHostEvaluator_BypassesTheEvaluator()
    {
        using var response = await SendCorsRequestAsync(services =>
        {
            services.AddSingleton<IBsftCorsOriginEvaluator>(new FakeBsftCorsOriginEvaluator(isAllowed: false));
            services.AddBsftCors(o => o.AllowOrigins("*"));
        }, "https://x.example");

        response.Headers.GetValues("Access-Control-Allow-Origin").Should().ContainSingle("*");
    }

    [Fact]
    public async Task UseBsftCors_WithWildcardEntryAndPredicate_BypassesThePredicate()
    {
        using var response = await SendCorsRequestAsync(
            services => services.AddBsftCors(o => o.AllowOrigins("*").AllowOriginsWhere(_ => false)),
            "https://x.example");

        response.Headers.GetValues("Access-Control-Allow-Origin").Should().ContainSingle("*");
    }

    private sealed class FakeBsftCorsOriginEvaluator(bool isAllowed = true) : IBsftCorsOriginEvaluator
    {
        public bool IsOriginAllowed(string? origin) => isAllowed;
    }
}
