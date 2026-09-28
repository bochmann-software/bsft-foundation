using Microsoft.EntityFrameworkCore;

namespace Bsft.Messaging.EFCore.Postgres.Tests.Fakes;

/// <summary>Counts the announcements the interceptor makes, and on which context.</summary>
public sealed class RecordingOutboxNotifier : IOutboxNotifier
{
    public List<DbContext> Synchronous { get; } = [];

    public List<DbContext> Asynchronous { get; } = [];

    public int Total => Synchronous.Count + Asynchronous.Count;

    public void Notify(DbContext context) => Synchronous.Add(context);

    /// <summary>Gets the tokens the asynchronous announcements were made under.</summary>
    public List<CancellationToken> Tokens { get; } = [];

    public Task NotifyAsync(DbContext context, CancellationToken ct = default)
    {
        Asynchronous.Add(context);
        Tokens.Add(ct);

        return Task.CompletedTask;
    }
}
