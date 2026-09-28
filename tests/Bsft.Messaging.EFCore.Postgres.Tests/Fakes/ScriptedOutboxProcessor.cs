using Bsft.Messaging.Outbox;

namespace Bsft.Messaging.EFCore.Postgres.Tests.Fakes;

/// <summary>
/// An outbox that says how many messages each pass delivered, from a script, and counts the passes it
/// was asked for.
/// </summary>
public sealed class ScriptedOutboxProcessor : IOutboxProcessor
{
    private readonly Queue<int> _delivered;

    /// <summary>Initializes a new instance of the <see cref="ScriptedOutboxProcessor"/> class.</summary>
    /// <param name="delivered">The counts to answer with, in order; anything after them is zero.</param>
    public ScriptedOutboxProcessor(params int[] delivered) => _delivered = new Queue<int>(delivered);

    /// <summary>Gets how many drain passes were asked for.</summary>
    public int Passes { get; private set; }

    /// <inheritdoc />
    public Task<int> ProcessOnceAsync(CancellationToken ct = default)
    {
        Passes++;

        return Task.FromResult(_delivered.Count > 0 ? _delivered.Dequeue() : 0);
    }
}
