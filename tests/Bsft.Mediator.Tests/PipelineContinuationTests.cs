using FluentAssertions;
using Xunit;

namespace Bsft.Mediator.Tests;

/// <summary>The rest of a pipeline as the mediator hands it to a rescoping behavior.</summary>
public sealed class PipelineContinuationTests
{
    [Fact]
    public void Constructor_WithANullArgument_Throws()
    {
        FluentActions.Invoking(() => new PipelineContinuation<string>(null!, _ => Task.FromResult("new")))
            .Should().Throw<ArgumentNullException>().WithParameterName("runInRequestScope");
        FluentActions.Invoking(() => new PipelineContinuation<string>(_ => Task.FromResult("request"), null!))
            .Should().Throw<ArgumentNullException>().WithParameterName("runInNewScope");
    }

    [Fact]
    public async Task RunAsync_WithNullAttempt_Throws()
    {
        var continuation = new PipelineContinuation<string>(_ => Task.FromResult("request"), _ => Task.FromResult("new"));

        await FluentActions.Awaiting(() => continuation.RunAsync(null!)).Should().ThrowAsync<ArgumentNullException>();
        await FluentActions.Awaiting(() => continuation.RunInNewScopeAsync(null!)).Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task RunAsync_WhenCanceled_RunsNothing()
    {
        var runs = 0;
        var continuation = new PipelineContinuation<string>(
            _ => Task.FromResult($"request {++runs}"), _ => Task.FromResult($"new {++runs}"));
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();

        await FluentActions.Awaiting(() => continuation.RunAsync((_, rest) => rest(), canceled.Token))
            .Should().ThrowAsync<OperationCanceledException>();
        await FluentActions.Awaiting(() => continuation.RunInNewScopeAsync((_, rest) => rest(), canceled.Token))
            .Should().ThrowAsync<OperationCanceledException>();
        runs.Should().Be(0);
    }
}
