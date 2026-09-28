using Bsft.Correlation.DependencyInjection;
using Bsft.Correlation.Http;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.Correlation.Tests;

public class ServiceRegistrationTests
{
    [Fact]
    public void AddBsftCorrelation_RegistersCorrelationServices()
    {
        var services = new ServiceCollection();

        services.AddBsftCorrelation();

        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        var context = scope.ServiceProvider.GetService<ICorrelationContext>();
        var concreteContext = scope.ServiceProvider.GetService<CorrelationContext>();

        context.Should().NotBeNull();
        concreteContext.Should().NotBeNull();
        context.Should().BeSameAs(concreteContext);
    }

    [Fact]
    public void AddBsftCorrelation_RegistersDelegatingHandler()
    {
        // The handler ships in Bsft.Correlation.Http since 1.3.5. This package references it so that a
        // host still wires both halves of correlation with one call - that promise is what this pins.
        var services = new ServiceCollection();

        services.AddBsftCorrelation();

        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        var handler = scope.ServiceProvider.GetService<CorrelationIdDelegatingHandler>();

        handler.Should().NotBeNull();
    }

    [Fact]
    public void AddBsftCorrelation_NullServices_ThrowsArgumentNullException()
    {
        IServiceCollection services = null!;

        var act = () => services.AddBsftCorrelation();

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("services");
    }

    [Fact]
    public void UseBsftCorrelation_NullApp_ThrowsArgumentNullException()
    {
        IApplicationBuilder app = null!;

        var act = () => app.UseBsftCorrelation();

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("app");
    }

    [Fact]
    public async Task UseBsftCorrelation_PutsTheCorrelationMiddlewareIntoThePipeline()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBsftCorrelation();
        using var provider = services.BuildServiceProvider();

        var builder = new ApplicationBuilder(provider);

        builder.UseBsftCorrelation().Should().BeSameAs(builder);

        // Run the built pipeline to prove the middleware really is in it, rather than only that
        // the call returned: the correlation middleware stamps the response with the id it resolved.
        var pipeline = builder.Build();
        using var scope = provider.CreateScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };

        await pipeline(context);

        context.Response.Headers["X-Correlation-Id"].ToString().Should().NotBeEmpty();
    }
}
