using System.Diagnostics.Metrics;
using System.Threading.RateLimiting;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.RateLimiting;

namespace Bsft.Hosting.Tests;

public sealed class RateLimitRejectionWriterTests : IDisposable
{
    private readonly Meter _meter = new($"{nameof(RateLimitRejectionWriterTests)}-{Guid.NewGuid():N}");
    private readonly MeterListener _listener = new();
    private readonly List<(long Value, object? Policy)> _measurements = [];

    public RateLimitRejectionWriterTests()
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (ReferenceEquals(instrument.Meter, _meter))
                listener.EnableMeasurementEvents(instrument);
        };
        _listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            foreach (var tag in tags)
            {
                if (tag.Key == "policy")
                    _measurements.Add((value, tag.Value));
            }
        });
        _listener.Start();
    }

    [Fact]
    public async Task WriteAsync_ResponseAlreadyStarted_CountsButLeavesTheResponseAlone()
    {
        var writer = Writer();
        var context = new DefaultHttpContext();
        context.Features.Set<IHttpResponseFeature>(new StartedResponseFeature());

        await writer.WriteAsync(Rejected(context, retryAfter: null), CancellationToken.None);

        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        context.Response.Headers.RetryAfter.Should().BeEmpty();
        _measurements.Should().ContainSingle().Which.Should().Be((1L, (object?)RateLimitRejectionWriter.UnnamedPolicyTag));
    }

    [Fact]
    public async Task WriteAsync_EstimateWithAFraction_RoundsRetryAfterUp()
    {
        var writer = Writer();
        var context = new DefaultHttpContext();

        await writer.WriteAsync(Rejected(context, TimeSpan.FromMilliseconds(1200)), CancellationToken.None);

        context.Response.StatusCode.Should().Be(StatusCodes.Status429TooManyRequests);
        context.Response.Headers.RetryAfter.ToString().Should().Be("2");
    }

    [Fact]
    public async Task WriteAsync_EstimateOfZero_AnswersAtLeastOneSecond()
    {
        var writer = Writer();
        var context = new DefaultHttpContext();

        await writer.WriteAsync(Rejected(context, TimeSpan.Zero), CancellationToken.None);

        context.Response.Headers.RetryAfter.ToString().Should().Be("1");
    }

    [Fact]
    public async Task WriteAsync_LeaseWithoutEstimate_AnswersTheFallback()
    {
        var writer = Writer(TimeSpan.FromSeconds(13));
        var context = new DefaultHttpContext();

        await writer.WriteAsync(Rejected(context, retryAfter: null), CancellationToken.None);

        context.Response.Headers.RetryAfter.ToString().Should().Be("13");
        context.Response.ContentType.Should().Be("application/problem+json");
    }

    public void Dispose()
    {
        _listener.Dispose();
        _meter.Dispose();
    }

    private RateLimitRejectionWriter Writer(TimeSpan? fallback = null)
        => new(_meter.CreateCounter<long>(HostingDefaults.RateLimitRejectionsInstrument), fallback ?? TimeSpan.FromSeconds(1));

    private static OnRejectedContext Rejected(HttpContext context, TimeSpan? retryAfter)
        => new()
        {
            HttpContext = context,
            Lease = new ChainedRateLimitLease(isAcquired: false, [], retryAfter, onRelease: null)
        };

    private sealed class StartedResponseFeature : HttpResponseFeature
    {
        public override bool HasStarted => true;
    }
}
