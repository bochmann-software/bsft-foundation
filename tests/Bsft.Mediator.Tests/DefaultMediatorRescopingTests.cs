using Bsft.Mediator.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.Mediator.Tests;

/// <summary>
/// A rescoping behavior runs the rest of the pipeline in the request scope or in a fresh one: the fresh
/// scope is seeded before anything is resolved from it, the handler comes from it, and it is disposed
/// once the attempt completed.
/// </summary>
public sealed class DefaultMediatorRescopingTests
{
    [Fact]
    public async Task SendAsync_WhenABehaviorRunsTheRestInANewScope_ResolvesTheHandlerFromTheSeededScope()
    {
        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ScopeMarker>().Name = "request";
        var log = provider.GetRequiredService<RescopeLog>();

        var answer = await scope.ServiceProvider.GetRequiredService<IMediator>().SendAsync(new RescopedCommand());

        answer.Should().Be("request-seeded");
        log.AttemptMarkers.Select(marker => marker.Name).Should().Equal("request", "request-seeded");
        log.AttemptMarkers[1].IsDisposed.Should().BeTrue("the fresh scope ended with its attempt");
        log.AttemptMarkers[0].IsDisposed.Should().BeFalse();
    }

    [Fact]
    public async Task SendAsync_ForACommandWithoutAResult_RunsTheHandlerOfEachScope()
    {
        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ScopeMarker>().Name = "request";

        await scope.ServiceProvider.GetRequiredService<IMediator>().SendAsync(new RescopedVoidCommand());

        provider.GetRequiredService<RescopeLog>().Handled.Should().Equal("request", "request-seeded");
    }

    [Fact]
    public async Task QueryAsync_WhenABehaviorRunsTheRestInANewScope_ResolvesTheHandlerFromTheSeededScope()
    {
        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ScopeMarker>().Name = "request";

        var answer = await scope.ServiceProvider.GetRequiredService<IMediator>().QueryAsync(new RescopedQuery());

        answer.Should().Be("request-seeded");
    }

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<RescopeLog>();
        services.AddScoped<ScopeMarker>();
        services.AddScoped<IRequestScopeSeed, ScopeMarkerSeed>();
        services.AddBsftMediator(mediator => mediator
            .RegisterServicesFromAssembly(typeof(RescopedCommandHandler).Assembly)
            .AddBehavior(typeof(TwiceRescopingBehavior<,>)));

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }
}
