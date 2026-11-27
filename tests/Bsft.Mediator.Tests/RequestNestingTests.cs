using Bsft.Mediator.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.Mediator.Tests;

/// <summary>
/// The marks of the requests nested in a scope: set inside an asynchronous method, seen by what it
/// awaits, gone for its caller, and never shared between scopes.
/// </summary>
public sealed class RequestNestingTests
{
    [Fact]
    public async Task MarkRetrying_FlowsIntoTheAwaitedWorkAndEndsWithTheMarkingMethod()
    {
        var nesting = new RequestNesting();
        var isRetryingInside = false;

        async Task MarkAndRunAsync()
        {
            nesting.MarkRetrying();
            await Task.Yield();
            isRetryingInside = nesting.IsRetrying;
        }

        await MarkAndRunAsync();

        isRetryingInside.Should().BeTrue();
        nesting.IsRetrying.Should().BeFalse();
    }

    [Fact]
    public async Task MarkTransactionOpen_FlowsIntoTheAwaitedWorkAndEndsWithTheMarkingMethod()
    {
        var nesting = new RequestNesting();
        var isOpenInside = false;

        async Task MarkAndRunAsync()
        {
            nesting.MarkTransactionOpen();
            await Task.Yield();
            isOpenInside = nesting.IsTransactionOpen;
        }

        await MarkAndRunAsync();

        isOpenInside.Should().BeTrue();
        nesting.IsTransactionOpen.Should().BeFalse();
    }

    [Fact]
    public async Task AddBsftMediator_RegistersOneNestingPerScope()
    {
        var services = new ServiceCollection();
        services.AddBsftMediator(_ => { });
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var first = provider.CreateAsyncScope();
        await using var second = provider.CreateAsyncScope();

        var nesting = first.ServiceProvider.GetRequiredService<IRequestNesting>();

        first.ServiceProvider.GetRequiredService<IRequestNesting>().Should().BeSameAs(nesting);
        second.ServiceProvider.GetRequiredService<IRequestNesting>().Should().NotBeSameAs(nesting);
        provider.GetRequiredService<IMediatorTransactionPolicy>().Should().BeSameAs(provider.GetRequiredService<MediatorConfiguration>());
    }
}
