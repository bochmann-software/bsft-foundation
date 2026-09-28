using Bsft.Idempotency.Auditing;
using FluentAssertions;
using Xunit;

namespace Bsft.Idempotency.Tests.Auditing;

public class NullIdempotencyAuditSinkTests
{
    [Fact]
    public async Task RecordAsync_DiscardsTheEventWithoutComplaining()
    {
        var sink = new NullIdempotencyAuditSink();

        await FluentActions
            .Awaiting(() => sink.RecordAsync(new IdempotencyAuditEvent(
                IdempotencyAuditAction.Replayed, "scope", "key", DateTimeOffset.UnixEpoch)))
            .Should().NotThrowAsync();
    }

    [Fact]
    public async Task RecordAsync_EvenANullEvent_IsSimplyDiscarded() =>
        await FluentActions.Awaiting(() => new NullIdempotencyAuditSink().RecordAsync(null!))
            .Should().NotThrowAsync();
}
