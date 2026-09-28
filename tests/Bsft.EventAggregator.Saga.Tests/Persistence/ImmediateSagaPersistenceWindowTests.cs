using Bsft.EventAggregator.DependencyInjection;
using Bsft.EventAggregator.Saga.DependencyInjection;
using Bsft.EventAggregator.Saga.Persistence;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.EventAggregator.Saga.Tests.Persistence;

/// <summary>
/// The window used with stores that already write durably on their own. It has to be inert, and it
/// has to be what a saga engine without a transactional provider is configured with.
/// </summary>
public class ImmediateSagaPersistenceWindowTests
{
    [Fact]
    public async Task AWindowThatStagesNothing_CommitsAndDisposesWithoutEffect()
    {
        var window = new ImmediateSagaPersistenceWindow();

        await using var scope = await window.OpenAsync();
        var act = async () => await scope.CommitAsync();

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task EachOpenWindow_IsItsOwnScope()
    {
        var window = new ImmediateSagaPersistenceWindow();

        await using var first = await window.OpenAsync();
        await using var second = await window.OpenAsync();

        second.Should().NotBeSameAs(first);
    }

    [Fact]
    public void TheInMemoryEngine_UsesTheImmediateWindow()
    {
        var services = new ServiceCollection();
        services.AddBsftEventAggregator();

        services.AddBsftSaga();

        using var provider = services.BuildServiceProvider();
        provider.GetService<ISagaPersistenceWindow>().Should().BeOfType<ImmediateSagaPersistenceWindow>();
    }
}
