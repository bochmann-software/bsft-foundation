using Bsft.Mediator;
using Bsft.Resilience.Mediator.Tests.Fakes;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Bsft.Resilience.Mediator.Tests;

/// <summary>
/// The retry marker moved to <c>Bsft.Mediator.Abstractions</c>; the one left behind in this package keeps
/// working, so a contract compiled against either is retried the same, and the T0 retry budget holds for both.
/// </summary>
public class RetryBehaviorRetryableAttributeTests
{
    [Fact]
    public async Task HandleAsync_WithTheNewAttribute_RetriesATransientFailure()
    {
        var calls = await RunFailingOnceAsync(new CommandWithNewAttribute());

        calls.Should().Be(2);
    }

    [Fact]
    public async Task HandleAsync_WithTheLegacyAttribute_RetriesATransientFailure()
    {
        var calls = await RunFailingOnceAsync(new CommandWithLegacyAttribute());

        calls.Should().Be(2);
    }

    [Fact]
    public async Task HandleAsync_WithTheNewAttributeOnABaseType_RetriesTheDerivedRequest()
    {
        var calls = await RunFailingOnceAsync(new DerivedFromNewlyMarkedCommand());

        calls.Should().Be(2);
    }

    [Fact]
    public async Task HandleAsync_WithBothAttributes_RetriesOnce()
    {
        var calls = await RunFailingOnceAsync(new CommandWithBothAttributes());

        calls.Should().Be(2);
    }

    [Fact]
    public async Task HandleAsync_WithoutAnyAttribute_RunsOnce()
    {
        var behavior = Behavior<UnmarkedCommand>(maxRetryAttempts: 3);
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
    public async Task HandleAsync_WithTwoRetriesAndAPersistentFailure_RunsThreeTimes()
    {
        // T0 parity: at most three runs, the first and two retries.
        var behavior = Behavior<CommandWithNewAttribute>(maxRetryAttempts: 2);
        var calls = 0;

        var act = () => behavior.HandleAsync(new CommandWithNewAttribute(), () =>
        {
            calls++;
            throw new TimeoutException("transient");
        });

        await act.Should().ThrowAsync<TimeoutException>();
        calls.Should().Be(3);
    }

    [Fact]
    public async Task HandleAsync_WithTheNewAttributeInsideARetriedRequest_RunsOnce()
    {
        var nesting = new FakeRequestNesting();
        nesting.MarkRetrying();
        var behavior = Behavior<CommandWithNewAttribute>(maxRetryAttempts: 3, nesting);
        var calls = 0;

        var act = () => behavior.HandleAsync(new CommandWithNewAttribute(), () =>
        {
            calls++;
            throw new TimeoutException("transient");
        });

        await act.Should().ThrowAsync<TimeoutException>();
        calls.Should().Be(1);
    }

    private static async Task<int> RunFailingOnceAsync<TRequest>(TRequest request)
        where TRequest : notnull
    {
        var behavior = Behavior<TRequest>(maxRetryAttempts: 3);
        var calls = 0;

        var result = await behavior.HandleAsync(request, () =>
        {
            calls++;
            return calls == 1 ? throw new TimeoutException("transient") : Task.FromResult("ok");
        });

        result.Should().Be("ok");
        return calls;
    }

    private static RetryBehavior<TRequest, string> Behavior<TRequest>(
        int maxRetryAttempts, IRequestNesting? nesting = null)
        where TRequest : notnull =>
        new(new MediatorResiliencePipelineProvider(Options.Create(new BsftResilienceMediatorOptions
            {
                BaseDelay = TimeSpan.Zero,
                MaxRetryAttempts = maxRetryAttempts,
            })),
            Options.Create(new BsftResilienceMediatorOptions()),
            new FakeRequestMetadataProvider(),
            nesting ?? new FakeRequestNesting(),
            new FakeMediatorTransactionPolicy { ShouldApplyTransactionToAllCommands = false });

    [Bsft.Mediator.Retryable]
    private sealed record CommandWithNewAttribute : ICommand<string>;

    [Bsft.Resilience.Mediator.Retryable]
    private sealed record CommandWithLegacyAttribute : ICommand<string>;

    [Bsft.Mediator.Retryable]
    [Bsft.Resilience.Mediator.Retryable]
    private sealed record CommandWithBothAttributes : ICommand<string>;

    [Bsft.Mediator.Retryable]
    private class NewlyMarkedCommand : ICommand<string>;

    private sealed class DerivedFromNewlyMarkedCommand : NewlyMarkedCommand;

    private sealed record UnmarkedCommand : ICommand<string>;
}
