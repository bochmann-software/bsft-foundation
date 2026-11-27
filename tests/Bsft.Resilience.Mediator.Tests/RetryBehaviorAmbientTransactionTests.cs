using Bsft.Core.Persistence;
using Bsft.Mediator;
using Bsft.Resilience.Mediator.DependencyInjection;
using Bsft.Resilience.Mediator.Tests.Fakes;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Bsft.Resilience.Mediator.Tests;

/// <summary>
/// With an ambient transaction in the scope only the outermost unit is retried, every further attempt
/// runs in a fresh scope, and a failure the store's provider classifies as a transaction conflict is
/// retried whatever the host's predicate says.
/// </summary>
public sealed class RetryBehaviorAmbientTransactionTests
{
    [Retryable]
    private sealed record RetryableRequest;

    private sealed record UnmarkedRequest;

    [Fact]
    public async Task HandleAsync_WhenNestedInAnOpenAmbientTransaction_PassesTheFailureThroughUntried()
    {
        using var provider = BuildProvider(() => new FakeAmbientTransaction { IsActive = true });
        using var scope = provider.CreateScope();
        var calls = 0;
        var continuation = new FakePipelineContinuation<string>(scope.ServiceProvider, _ =>
        {
            calls++;
            throw new TimeoutException("conflict");
        });

        var act = () => Behavior(scope.ServiceProvider, shouldRetry: _ => true).HandleAsync(new RetryableRequest(), continuation);

        await act.Should().ThrowAsync<TimeoutException>();
        calls.Should().Be(1, "the outermost unit is the one to retry");
    }

    [Fact]
    public async Task HandleAsync_WhenTheOutermostUnitConflicts_RetriesItInAFreshScopeThoughThePredicateRefuses()
    {
        using var provider = BuildProvider(() => new FakeAmbientTransaction { ConflictType = typeof(TimeoutException) });
        using var scope = provider.CreateScope();
        var attemptScopes = new List<IServiceProvider>();
        var isRetryingInAttempt = new List<bool>();
        var continuation = new FakePipelineContinuation<string>(scope.ServiceProvider, attemptScope =>
        {
            attemptScopes.Add(attemptScope);
            isRetryingInAttempt.Add(attemptScope.GetRequiredService<IRequestNesting>().IsRetrying);
            return attemptScopes.Count == 1 ? throw new TimeoutException("conflict") : Task.FromResult("ok");
        });

        var result = await Behavior(scope.ServiceProvider, shouldRetry: _ => false).HandleAsync(new RetryableRequest(), continuation);

        result.Should().Be("ok");
        attemptScopes.Should().HaveCount(2);
        attemptScopes[0].Should().BeSameAs(scope.ServiceProvider, "the first attempt runs in the request scope");
        attemptScopes[1].Should().BeSameAs(continuation.NewScopes.Single(), "the retry runs in a fresh scope");
        isRetryingInAttempt.Should().Equal([true, true], "each attempt's scope knows its commands run inside a retried request");
    }

    [Fact]
    public async Task HandleAsync_WhenTheFreshScopeFailsBeforeItsAttempt_DoesNotClassifyItByThePreviousTransaction()
    {
        using var provider = BuildProvider(() => new FakeAmbientTransaction { ConflictType = typeof(TimeoutException) });
        using var scope = provider.CreateScope();
        var continuation = new FakePipelineContinuation<string>(
            scope.ServiceProvider, _ => throw new TimeoutException("conflict"))
        {
            NewScopeFailure = new TimeoutException("the fresh scope could not be set up"),
        };

        var act = () => Behavior(scope.ServiceProvider, shouldRetry: _ => false).HandleAsync(new RetryableRequest(), continuation);

        (await act.Should().ThrowAsync<TimeoutException>()).WithMessage("the fresh scope could not be set up");
        continuation.NewScopeRuns.Should().Be(1,
            "the failed setup ran in no transaction, so the first attempt's conflict does not retry it again");
    }

    [Fact]
    public async Task HandleAsync_WhenTheOutermostFailureIsNoConflict_LeavesItToThePredicate()
    {
        using var provider = BuildProvider(() => new FakeAmbientTransaction { ConflictType = typeof(TimeoutException) });
        using var scope = provider.CreateScope();
        var calls = 0;
        var continuation = new FakePipelineContinuation<string>(scope.ServiceProvider, _ =>
        {
            calls++;
            throw new InvalidOperationException("bug");
        });

        var act = () => Behavior(scope.ServiceProvider, shouldRetry: _ => false).HandleAsync(new RetryableRequest(), continuation);

        await act.Should().ThrowAsync<InvalidOperationException>();
        calls.Should().Be(1);
    }

    [Fact]
    public async Task HandleAsync_WithoutAnAmbientTransaction_RetriesInTheRequestScopeOnThePredicateAlone()
    {
        using var provider = BuildProvider(ambient: null);
        using var scope = provider.CreateScope();
        var attemptScopes = new List<IServiceProvider>();
        var continuation = new FakePipelineContinuation<string>(scope.ServiceProvider, attemptScope =>
        {
            attemptScopes.Add(attemptScope);
            return attemptScopes.Count == 1 ? throw new InvalidOperationException("transient") : Task.FromResult("ok");
        });

        var result = await Behavior(scope.ServiceProvider, shouldRetry: exception => exception is not null)
            .HandleAsync(new RetryableRequest(), continuation);

        result.Should().Be("ok");
        attemptScopes.Should().HaveCount(2).And.OnlyContain(attemptScope => attemptScope == scope.ServiceProvider);
        continuation.NewScopes.Should().BeEmpty();
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task HandleAsync_WhenAnOuterRequestIsRetriedOrHoldsATransaction_PassesTheFailureThroughUntried(
        bool isRetrying, bool isTransactionOpen)
    {
        using var provider = BuildProvider(() => new FakeAmbientTransaction { ConflictType = typeof(TimeoutException) });
        using var scope = provider.CreateScope();
        var nesting = (FakeRequestNesting)scope.ServiceProvider.GetRequiredService<IRequestNesting>();
        if (isRetrying)
            nesting.MarkRetrying();
        if (isTransactionOpen)
            nesting.MarkTransactionOpen();
        var calls = 0;
        var continuation = new FakePipelineContinuation<string>(scope.ServiceProvider, _ =>
        {
            calls++;
            throw new TimeoutException("conflict");
        });

        var act = () => Behavior(scope.ServiceProvider, shouldRetry: _ => true).HandleAsync(new RetryableRequest(), continuation);

        await act.Should().ThrowAsync<TimeoutException>();
        calls.Should().Be(1, "only the outermost request of the scope is retried");
    }

    [Fact]
    public async Task HandleAsync_WhenAnOuterRequestHoldsATransactionInAHostWithoutAnAmbientOne_RetriesTheNestedRequest()
    {
        using var provider = BuildProvider(ambient: null);
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<IRequestNesting>().MarkTransactionOpen();
        var calls = 0;
        var continuation = new FakePipelineContinuation<string>(scope.ServiceProvider, _ =>
        {
            calls++;
            return calls == 1 ? throw new TimeoutException("transient") : Task.FromResult("ok");
        });

        var result = await Behavior(scope.ServiceProvider, shouldRetry: exception => exception is not null).HandleAsync(new RetryableRequest(), continuation);

        result.Should().Be("ok");
        calls.Should().Be(2, "the outer transaction is the request's own, which the nested request did not join");
    }

    [Fact]
    public async Task HandleAsync_WhenTheAmbientTransactionWasMisused_DoesNotRetryItThoughThePredicateWould()
    {
        using var provider = BuildProvider(() => new FakeAmbientTransaction { ConflictType = typeof(TimeoutException) });
        using var scope = provider.CreateScope();
        var calls = 0;
        var continuation = new FakePipelineContinuation<string>(scope.ServiceProvider, _ =>
        {
            calls++;
            throw new AmbientTransactionMisuseException("a nested failure was swallowed", new InvalidOperationException("bug"));
        });

        var act = () => Behavior(scope.ServiceProvider, shouldRetry: _ => true).HandleAsync(new RetryableRequest(), continuation);

        await act.Should().ThrowAsync<AmbientTransactionMisuseException>();
        calls.Should().Be(1, "a misuse fails the same way on every attempt");
        continuation.NewScopes.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_WhenTheMisuseSwallowedAConflict_RetriesTheUnitInAFreshScope()
    {
        using var provider = BuildProvider(() => new FakeAmbientTransaction { ConflictType = typeof(AmbientTransactionMisuseException) });
        using var scope = provider.CreateScope();
        var calls = 0;
        var continuation = new FakePipelineContinuation<string>(scope.ServiceProvider, _ =>
        {
            calls++;
            return calls == 1
                ? throw new AmbientTransactionMisuseException("a deadlock was swallowed", new TimeoutException("deadlock"))
                : Task.FromResult("ok");
        });

        var result = await Behavior(scope.ServiceProvider, shouldRetry: _ => false).HandleAsync(new RetryableRequest(), continuation);

        result.Should().Be("ok");
        calls.Should().Be(2, "the store classified the swallowed failure as a conflict, so the attempt is retried");
        continuation.NewScopes.Should().ContainSingle();
    }

    [Fact]
    public async Task HandleAsync_WhenTheRequestIsNotRetryable_RunsTheRestOnce()
    {
        using var provider = BuildProvider(() => new FakeAmbientTransaction());
        using var scope = provider.CreateScope();
        var continuation = new FakePipelineContinuation<string>(scope.ServiceProvider, _ => Task.FromResult("ok"));
        var behavior = new RetryBehavior<UnmarkedRequest, string>(
            Pipeline(_ => true),
            Options.Create(new BsftResilienceMediatorOptions()),
            new FakeRequestMetadataProvider(),
            new FakeRequestNesting(),
            new FakeMediatorTransactionPolicy());

        var result = await behavior.HandleAsync(new UnmarkedRequest(), continuation);

        result.Should().Be("ok");
        continuation.NewScopes.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_WithNullContinuation_Throws()
    {
        using var provider = BuildProvider(ambient: null);
        using var scope = provider.CreateScope();

        var act = () => Behavior(scope.ServiceProvider, _ => true).HandleAsync(new RetryableRequest(), (IPipelineContinuation<string>)null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_WithANullArgument_Throws()
    {
        var pipeline = Pipeline(_ => true);
        var options = Options.Create(new BsftResilienceMediatorOptions());
        var metadata = new FakeRequestMetadataProvider();
        var nesting = new FakeRequestNesting();
        var policy = new FakeMediatorTransactionPolicy();

        FluentActions.Invoking(() => new RetryBehavior<RetryableRequest, string>(pipeline, null!, metadata, nesting, policy))
            .Should().Throw<ArgumentNullException>().WithParameterName("options");
        FluentActions.Invoking(() => new RetryBehavior<RetryableRequest, string>(pipeline, options, null!, nesting, policy))
            .Should().Throw<ArgumentNullException>().WithParameterName("metadataProvider");
        FluentActions.Invoking(() => new RetryBehavior<RetryableRequest, string>(pipeline, options, metadata, null!, policy))
            .Should().Throw<ArgumentNullException>().WithParameterName("nesting");
        FluentActions.Invoking(() => new RetryBehavior<RetryableRequest, string>(pipeline, options, metadata, nesting, null!))
            .Should().Throw<ArgumentNullException>().WithParameterName("transactionPolicy");
    }

    [Fact]
    public void AddBsftResilienceMediator_WithTheMediatorSeams_ActivatesTheBehaviorWithTheScope()
    {
        using var provider = BuildProvider(() => new FakeAmbientTransaction());
        using var scope = provider.CreateScope();

        var behaviors = scope.ServiceProvider.GetServices<IPipelineBehavior<RetryableRequest, string>>();

        behaviors.Should().ContainSingle().Which.Should().BeOfType<RetryBehavior<RetryableRequest, string>>();
    }

    private static RetryBehavior<RetryableRequest, string> Behavior(IServiceProvider scope, Func<Exception?, bool> shouldRetry) =>
        new(Pipeline(shouldRetry),
            Options.Create(new BsftResilienceMediatorOptions()),
            new FakeRequestMetadataProvider(),
            scope.GetRequiredService<IRequestNesting>(),
            scope.GetRequiredService<IMediatorTransactionPolicy>());

    private static ServiceProvider BuildProvider(Func<IAmbientTransaction>? ambient)
    {
        var services = new ServiceCollection();
        services.AddScoped<IRequestNesting, FakeRequestNesting>();
        services.AddSingleton<IRequestMetadataProvider, FakeRequestMetadataProvider>();
        services.AddSingleton<IMediatorTransactionPolicy>(new FakeMediatorTransactionPolicy());
        if (ambient is not null)
            services.AddScoped(_ => ambient());
        services.AddBsftResilienceMediator();

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private static MediatorResiliencePipelineProvider Pipeline(Func<Exception?, bool> shouldRetry) =>
        new(Options.Create(new BsftResilienceMediatorOptions { BaseDelay = TimeSpan.Zero, ShouldRetry = shouldRetry }));
}
