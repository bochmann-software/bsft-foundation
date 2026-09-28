using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.Mediator.Tests;

/// <summary>
/// What a caller sees when a handler fails before it ever returns a task. A handler that guards its
/// arguments synchronously throws on the dispatch call itself, and the exception it chose has to be
/// the one that reaches the caller and the behaviors logging it.
/// </summary>
public class MediatorHandlerResolutionTests
{
    [Fact]
    public async Task SendAsync_WhenTheHandlerThrowsBeforeReturningItsTask_SurfacesThatException()
    {
        var services = new ServiceCollection();
        services.AddScoped<IMediator, DefaultMediator>();
        services.AddTransient<ICommandHandler<GuardingCommand, string>, GuardingCommandHandler>();
        using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        var act = () => mediator.SendAsync<string>(new GuardingCommand());

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("guarded");
    }

    [Fact]
    public async Task QueryAsync_WhenTheHandlerThrowsBeforeReturningItsTask_SurfacesThatException()
    {
        var services = new ServiceCollection();
        services.AddScoped<IMediator, DefaultMediator>();
        services.AddTransient<IQueryHandler<GuardingQuery, string>, GuardingQueryHandler>();
        using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        var act = () => mediator.QueryAsync<string>(new GuardingQuery());

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("guarded");
    }

    [Fact]
    public async Task SendAsync_WhenTheResultlessHandlerThrowsBeforeReturningItsTask_SurfacesThatException()
    {
        var services = new ServiceCollection();
        services.AddScoped<IMediator, DefaultMediator>();
        services.AddTransient<ICommandHandler<GuardingVoidCommand>, GuardingVoidCommandHandler>();
        using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        var act = () => mediator.SendAsync(new GuardingVoidCommand());

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("guarded");
    }
}
