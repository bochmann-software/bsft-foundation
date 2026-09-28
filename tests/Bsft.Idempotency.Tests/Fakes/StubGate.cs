using Bsft.Idempotency.Replay;
using Microsoft.AspNetCore.Http;

namespace Bsft.Idempotency.Tests.Fakes;

/// <summary>A gate that answers what the test set, and remembers what it was asked.</summary>
internal sealed class StubGate : IIdempotencyGate
{
    public IdempotencyGateResult Result { get; set; } =
        new("scope", "fingerprint", new IdempotencyBeginResult(IdempotencyBeginOutcome.Started));

    public int RetryAfter { get; set; } = 1;

    /// <summary>Gets the responses the middleware stored, in order.</summary>
    public List<IdempotencyCompletionRequest> Completed { get; } = [];

    /// <summary>Gets the reservations the middleware dropped, in order.</summary>
    public List<(string Scope, string Key, string Fingerprint)> Released { get; } = [];

    public DateTimeOffset ReplayableUntil => DateTimeOffset.UnixEpoch.AddHours(24);

    public ValueTask<IdempotencyGateResult> BeginAsync(
        HttpContext context, string key, CancellationToken ct = default) =>
        ValueTask.FromResult(Result);

    public Task CompleteAsync(IdempotencyCompletionRequest request, CancellationToken ct = default)
    {
        Completed.Add(request);
        return Task.CompletedTask;
    }

    public Task ReleaseAsync(
        string scope, string key, string fingerprint, CancellationToken ct = default)
    {
        Released.Add((scope, key, fingerprint));
        return Task.CompletedTask;
    }

    public bool IsReplayable(int statusCode, long bodyLength) => true;

    public int RetryAfterSeconds(DateTimeOffset? leaseExpiresAt) => RetryAfter;
}
