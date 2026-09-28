using Bsft.Idempotency.DependencyInjection;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.Idempotency.Tests.DependencyInjection;

public class IdempotencyApplicationBuilderExtensionsTests
{
    [Fact]
    public void UseBsftIdempotency_OnAWiredApplication_IsAddedAndChains()
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddBsftIdempotency();

        using var provider = services.BuildServiceProvider();

        var app = new ApplicationBuilder(provider);

        app.UseBsftIdempotency().Should().BeSameAs(app);
    }

    [Fact]
    public void UseBsftIdempotency_WithoutAddBsftIdempotency_IsRefusedAtWiringTimeRatherThanOnTheFirstRequest()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();

        FluentActions.Invoking(() => new ApplicationBuilder(provider).UseBsftIdempotency())
            .Should().Throw<InvalidOperationException>()
            .WithMessage("*AddBsftIdempotency*");
    }

    [Fact]
    public void UseBsftIdempotency_NullApplicationBuilder_IsRefused() =>
        FluentActions.Invoking(() => IdempotencyApplicationBuilderExtensions.UseBsftIdempotency(null!))
            .Should().Throw<ArgumentNullException>().WithParameterName("app");
}
