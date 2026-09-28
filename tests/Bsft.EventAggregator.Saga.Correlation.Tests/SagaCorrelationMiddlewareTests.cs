using Bsft.Correlation;
using Bsft.EventAggregator.Saga.Correlation.DependencyInjection;
using Bsft.EventAggregator.Saga.Timeouts;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.EventAggregator.Saga.Correlation.Tests;

public class SagaCorrelationMiddlewareTests
{
    [Fact]
    public async Task TheRequestsCorrelationIdIsAmbientForTheRestOfThePipeline()
    {
        var ambient = new SagaCorrelationScope();
        string? observed = null;

        var middleware = new SagaCorrelationMiddleware(_ => { observed = ambient.Current; return Task.CompletedTask; }, ambient);

        await middleware.InvokeAsync(new DefaultHttpContext(), new CorrelationContext { CorrelationId = "request-3" });

        observed.Should().Be("request-3");
    }

    [Fact]
    public async Task TheAmbientIdIsReleasedAgainWhenTheRequestEnds()
    {
        var ambient = new SagaCorrelationScope();
        var middleware = new SagaCorrelationMiddleware(_ => Task.CompletedTask, ambient);

        await middleware.InvokeAsync(new DefaultHttpContext(), new CorrelationContext { CorrelationId = "request-3" });

        ambient.Current.Should().BeNull();
    }

    [Fact]
    public async Task AFailingPipelineStillReleasesTheAmbientId()
    {
        var ambient = new SagaCorrelationScope();
        var middleware = new SagaCorrelationMiddleware(
            _ => throw new InvalidOperationException("boom"),
            ambient);

        var act = () => middleware.InvokeAsync(new DefaultHttpContext(), new CorrelationContext());

        await act.Should().ThrowAsync<InvalidOperationException>();
        ambient.Current.Should().BeNull();
    }

    [Fact]
    public void TheMiddlewareRejectsItsNullArguments()
    {
        var ambient = new SagaCorrelationScope();

        FluentActions.Invoking(() => new SagaCorrelationMiddleware(null!, ambient))
            .Should().Throw<ArgumentNullException>().WithParameterName("next");
        FluentActions.Invoking(() => new SagaCorrelationMiddleware(_ => Task.CompletedTask, null!))
            .Should().Throw<ArgumentNullException>().WithParameterName("sagaCorrelationScope");
    }

    [Fact]
    public async Task TheMiddlewareRejectsANullContextAndCorrelationContext()
    {
        var middleware = new SagaCorrelationMiddleware(_ => Task.CompletedTask, new SagaCorrelationScope());

        await FluentActions.Invoking(() => middleware.InvokeAsync(null!, new CorrelationContext()))
            .Should().ThrowAsync<ArgumentNullException>().WithParameterName("context");
        await FluentActions.Invoking(() => middleware.InvokeAsync(new DefaultHttpContext(), null!))
            .Should().ThrowAsync<ArgumentNullException>().WithParameterName("correlationContext");
    }

    [Fact]
    public void UseSagaCorrelation_PutsTheMiddlewareIntoThePipeline()
    {
        var services = new ServiceCollection();
        services.AddBsftSagaCorrelation();
        using var provider = services.BuildServiceProvider();

        var builder = new ApplicationBuilder(provider);

        builder.UseBsftSagaCorrelation().Should().BeSameAs(builder);
        builder.Build().Should().NotBeNull();
    }

    [Fact]
    public void UseSagaCorrelation_RejectsANullApplicationBuilder()
    {
        FluentActions.Invoking(() => SagaCorrelationApplicationBuilderExtensions.UseBsftSagaCorrelation(null!))
            .Should().Throw<ArgumentNullException>().WithParameterName("app");
    }

    [Fact]
    public void TheInitializerAdoptsTheIdItIsGiven()
    {
        var context = new CorrelationContext { CorrelationId = "scope-own" };

        new SagaCorrelationInitializer(context).Restore("from-caller");

        context.CorrelationId.Should().Be("from-caller");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TheInitializerRejectsAnEmptyCorrelationId(string? correlationId)
    {
        var initializer = new SagaCorrelationInitializer(new CorrelationContext());

        // Adopting an empty id would silently break the chain it exists to preserve.
        FluentActions.Invoking(() => initializer.Restore(correlationId!))
            .Should().Throw<ArgumentException>().WithParameterName("correlationId");
    }

    [Fact]
    public void TheInitializerRequiresACorrelationContext()
    {
        FluentActions.Invoking(() => new SagaCorrelationInitializer(null!))
            .Should().Throw<ArgumentNullException>().WithParameterName("correlationContext");
    }
}
