using Bsft.Core;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Bsft.Resilience.Mediator.Tests;

public class RetryBehaviorTests
{
    [Retryable]
    private sealed record RetryableRequest;

    private sealed record PlainRequest;

    private static BsftResilienceMediatorOptions FastOptions() =>
        new() { BaseDelay = TimeSpan.Zero };

    private static MediatorResiliencePipelineProvider ProviderFrom(BsftResilienceMediatorOptions options) =>
        new(Options.Create(options));

    [Fact]
    public void Constructor_NullProvider_Throws()
    {
        var act = () => new RetryBehavior<RetryableRequest, string>(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task NullNext_Throws()
    {
        var behavior = new RetryBehavior<RetryableRequest, string>(ProviderFrom(FastOptions()));

        var act = () => behavior.HandleAsync(new RetryableRequest(), null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task RetryableRequest_TransientFailure_IsRetriedUntilSuccess()
    {
        var options = FastOptions();
        var behavior = new RetryBehavior<RetryableRequest, string>(ProviderFrom(options));
        var calls = 0;

        Task<string> Next()
        {
            calls++;
            // Fail on every attempt except the last allowed one.
            if (calls <= options.MaxRetryAttempts)
                throw new InvalidOperationException("transient");
            return Task.FromResult("ok");
        }

        var result = await behavior.HandleAsync(new RetryableRequest(), Next);

        result.Should().Be("ok");
        calls.Should().Be(options.MaxRetryAttempts + 1);
    }

    [Fact]
    public async Task RetryableRequest_PersistentTransientFailure_ExhaustsRetriesThenThrows()
    {
        var options = FastOptions();
        var behavior = new RetryBehavior<RetryableRequest, string>(ProviderFrom(options));
        var calls = 0;

        Task<string> Next()
        {
            calls++;
            throw new InvalidOperationException("transient");
        }

        var act = () => behavior.HandleAsync(new RetryableRequest(), Next);

        await act.Should().ThrowAsync<InvalidOperationException>();
        calls.Should().Be(options.MaxRetryAttempts + 1);
    }

    [Fact]
    public async Task RetryableRequest_Success_InvokesHandlerOnce()
    {
        var behavior = new RetryBehavior<RetryableRequest, string>(ProviderFrom(FastOptions()));
        var calls = 0;

        Task<string> Next()
        {
            calls++;
            return Task.FromResult("ok");
        }

        var result = await behavior.HandleAsync(new RetryableRequest(), Next);

        result.Should().Be("ok");
        calls.Should().Be(1);
    }

    [Fact]
    public async Task RetryableRequest_DomainException_IsNotRetried()
    {
        var behavior = new RetryBehavior<RetryableRequest, string>(ProviderFrom(FastOptions()));
        var calls = 0;

        Task<string> Next()
        {
            calls++;
            throw new ConflictException("duplicate");
        }

        var act = () => behavior.HandleAsync(new RetryableRequest(), Next);

        await act.Should().ThrowAsync<ConflictException>();
        calls.Should().Be(1);
    }

    [Fact]
    public async Task RetryableRequest_CancelledToken_IsNotRetried()
    {
        var behavior = new RetryBehavior<RetryableRequest, string>(ProviderFrom(FastOptions()));
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var calls = 0;

        Task<string> Next()
        {
            calls++;
            throw new OperationCanceledException();
        }

        var act = () => behavior.HandleAsync(new RetryableRequest(), Next, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        calls.Should().BeLessThanOrEqualTo(1);
    }

    [Fact]
    public async Task PlainRequest_IsNotRetried_AndPropagatesException()
    {
        var behavior = new RetryBehavior<PlainRequest, string>(ProviderFrom(FastOptions()));
        var calls = 0;

        Task<string> Next()
        {
            calls++;
            throw new InvalidOperationException("transient");
        }

        var act = () => behavior.HandleAsync(new PlainRequest(), Next);

        await act.Should().ThrowAsync<InvalidOperationException>();
        calls.Should().Be(1);
    }
}
