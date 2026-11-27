using Bsft.Mediator;
using Bsft.Resilience.Mediator.DependencyInjection;
using Bsft.Resilience.Mediator.Tests.Fakes;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Bsft.Resilience.Mediator.Tests;

/// <summary>
/// With <see cref="BsftResilienceMediatorOptions.ShouldRetryAllCommands"/> every command is retried without
/// the attribute, queries are not, and only the outermost retried request of a scope runs again.
/// </summary>
public sealed class RetryBehaviorAllCommandsTests
{
    private sealed record UnmarkedCommand : ICommand<string>;

    private sealed record UnmarkedQuery : IQuery<string>;

    [Retryable]
    private sealed record MarkedCommand : ICommand<string>;

    [Fact]
    public async Task HandleAsync_WithThePolicy_RetriesAnUnmarkedCommand()
    {
        var behavior = Behavior<UnmarkedCommand>(isAppliedToAllCommands: true, new FakeRequestNesting());
        var calls = 0;

        var result = await behavior.HandleAsync(new UnmarkedCommand(), () =>
        {
            calls++;
            return calls == 1 ? throw new TimeoutException("transient") : Task.FromResult("ok");
        });

        result.Should().Be("ok");
        calls.Should().Be(2);
    }

    [Fact]
    public async Task HandleAsync_WithoutThePolicy_PassesAnUnmarkedCommandThrough()
    {
        var behavior = Behavior<UnmarkedCommand>(isAppliedToAllCommands: false, new FakeRequestNesting());
        var calls = 0;

        var act = () => behavior.HandleAsync(new UnmarkedCommand(), () =>
        {
            calls++;
            throw new TimeoutException("transient");
        });

        await act.Should().ThrowAsync<TimeoutException>();
        calls.Should().Be(1);
    }

    [Fact]
    public async Task HandleAsync_WithThePolicy_PassesAnUnmarkedQueryThrough()
    {
        var behavior = Behavior<UnmarkedQuery>(isAppliedToAllCommands: true, new FakeRequestNesting());
        var calls = 0;

        var act = () => behavior.HandleAsync(new UnmarkedQuery(), () =>
        {
            calls++;
            throw new TimeoutException("transient");
        });

        await act.Should().ThrowAsync<TimeoutException>();
        calls.Should().Be(1);
    }

    [Fact]
    public async Task HandleAsync_WithThePolicy_RetriesOnlyTheOutermostCommandOfTheScope()
    {
        var nesting = new FakeRequestNesting();
        var outer = Behavior<UnmarkedCommand>(isAppliedToAllCommands: true, nesting);
        var inner = Behavior<UnmarkedCommand>(isAppliedToAllCommands: true, nesting);
        var outerCalls = 0;
        var innerCalls = 0;

        var result = await outer.HandleAsync(new UnmarkedCommand(), () =>
        {
            outerCalls++;
            return inner.HandleAsync(new UnmarkedCommand(), () =>
            {
                innerCalls++;
                return innerCalls == 1 ? throw new TimeoutException("transient") : Task.FromResult("ok");
            });
        });

        result.Should().Be("ok");
        innerCalls.Should().Be(2);
        outerCalls.Should().Be(2, "the nested failure reached the outer attempt, which ran again");
        nesting.IsRetrying.Should().BeFalse("the mark ended with the outer request");
    }

    [Fact]
    public async Task HandleAsync_WhenAnOuterRequestIsRetriedInAHostThatDidNotOptIn_RetriesOnlyTheOuterRequest()
    {
        // Neither an ambient transaction nor either policy: the outermost-only rule for a request
        // nested in a retrying one holds in every host, so the attempts are not multiplied.
        var nesting = new FakeRequestNesting();
        var outer = Behavior<MarkedCommand>(isAppliedToAllCommands: false, nesting);
        var inner = Behavior<MarkedCommand>(isAppliedToAllCommands: false, nesting);
        var outerCalls = 0;
        var innerCalls = 0;

        var result = await outer.HandleAsync(new MarkedCommand(), () =>
        {
            outerCalls++;
            return inner.HandleAsync(new MarkedCommand(), () =>
            {
                innerCalls++;
                return innerCalls == 1 ? throw new TimeoutException("transient") : Task.FromResult("ok");
            });
        });

        result.Should().Be("ok");
        innerCalls.Should().Be(2);
        outerCalls.Should().Be(2, "the nested failure reached the outer attempt, which ran again");
    }

    [Fact]
    public async Task HandleAsync_WhenTheCommandRunsInAScopeOfItsOwn_IsTheOutermostOfThatScope()
    {
        var outer = Behavior<UnmarkedCommand>(isAppliedToAllCommands: true, new FakeRequestNesting());
        var inner = Behavior<UnmarkedCommand>(isAppliedToAllCommands: true, new FakeRequestNesting());
        var innerCalls = 0;

        var result = await outer.HandleAsync(new UnmarkedCommand(), () =>
            inner.HandleAsync(new UnmarkedCommand(), () =>
            {
                innerCalls++;
                return innerCalls == 1 ? throw new TimeoutException("transient") : Task.FromResult("ok");
            }));

        result.Should().Be("ok");
        innerCalls.Should().Be(2, "a command of another scope is retried by that scope's behavior");
    }

    [Fact]
    public async Task HandleAsync_WhenAnOuterTransactionIsOpenUnderTheTransactionPolicy_DoesNotRetryTheNestedCommandAlone()
    {
        // The nested command joined the transaction the outer one holds open, which its failure has
        // typically aborted: only the outer request can be retried.
        var nesting = new FakeRequestNesting();
        nesting.MarkTransactionOpen();
        var behavior = Behavior<MarkedCommand>(isAppliedToAllCommands: true, nesting, isTransactionAppliedToAllCommands: true);
        var calls = 0;

        var act = () => behavior.HandleAsync(new MarkedCommand(), () =>
        {
            calls++;
            throw new TimeoutException("transient");
        });

        await act.Should().ThrowAsync<TimeoutException>();
        calls.Should().Be(1);
    }

    [Fact]
    public async Task HandleAsync_WhenAnOuterTransactionIsOpenWithoutTheTransactionPolicy_RetriesTheNestedCommandAsBefore()
    {
        // A host without the transaction policy and without an ambient transaction: the outer request
        // opened a transaction of its own, which the nested command did not join, so it is retried on
        // its own as it was before the nesting marks existed.
        var nesting = new FakeRequestNesting();
        nesting.MarkTransactionOpen();
        var behavior = Behavior<MarkedCommand>(isAppliedToAllCommands: false, nesting);
        var calls = 0;

        var result = await behavior.HandleAsync(new MarkedCommand(), () =>
        {
            calls++;
            return calls == 1 ? throw new TimeoutException("transient") : Task.FromResult("ok");
        });

        result.Should().Be("ok");
        calls.Should().Be(2);
    }

    [Fact]
    public async Task AddBsftResilienceMediator_WithThePolicy_BuildsABehaviorThatRetriesCommands()
    {
        var services = new ServiceCollection();
        services.AddScoped<IRequestNesting, FakeRequestNesting>();
        services.AddSingleton<IRequestMetadataProvider, FakeRequestMetadataProvider>();
        services.AddSingleton<IMediatorTransactionPolicy>(new FakeMediatorTransactionPolicy());
        services.AddBsftResilienceMediator(options =>
        {
            options.ShouldRetryAllCommands = true;
            options.BaseDelay = TimeSpan.Zero;
        });
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();
        var behavior = scope.ServiceProvider.GetServices<IPipelineBehavior<UnmarkedCommand, string>>().Single();
        var calls = 0;

        var result = await behavior.HandleAsync(new UnmarkedCommand(), () =>
        {
            calls++;
            return calls == 1 ? throw new TimeoutException("transient") : Task.FromResult("ok");
        });

        result.Should().Be("ok");
        calls.Should().Be(2);
    }

    private static RetryBehavior<TRequest, string> Behavior<TRequest>(
        bool isAppliedToAllCommands, IRequestNesting nesting, bool isTransactionAppliedToAllCommands = false) =>
        new(Pipeline(),
            Options.Create(new BsftResilienceMediatorOptions { ShouldRetryAllCommands = isAppliedToAllCommands }),
            new FakeRequestMetadataProvider(),
            nesting,
            new FakeMediatorTransactionPolicy { ShouldApplyTransactionToAllCommands = isTransactionAppliedToAllCommands });

    private static MediatorResiliencePipelineProvider Pipeline() =>
        new(Options.Create(new BsftResilienceMediatorOptions { BaseDelay = TimeSpan.Zero }));
}
