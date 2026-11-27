using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;

namespace Bsft.Core.Persistence;

/// <summary>
/// The shipped <see cref="ITimeOrderedIdGenerator"/>: 41 bits of milliseconds since
/// <see cref="Epoch"/>, 12 bits of node id and 10 bits of sequence, with the sign bit always clear.
/// </summary>
/// <remarks>
/// <para>
/// The generator keeps one counter, the last value's milliseconds and sequence packed into a single
/// <see langword="long"/>, and moves it with a compare-and-swap to the larger of "one more than last
/// time" and "now with sequence zero". That one rule gives every property the ids need: values rise
/// strictly however many threads ask, a clock that steps back does not move them back (the counter
/// keeps counting from where it was), and more than <see cref="MaxSequence"/> + 1 values in one
/// millisecond carry into the next millisecond instead of repeating. The counter runs ahead of the
/// clock in the last two cases and falls back onto it once the clock has caught up.
/// </para>
/// <para>
/// <see cref="ForNode(int, TimeProvider)"/> hands out one instance per node id, clock and process, so two
/// containers in one process that use the same node id over the same clock still share one counter and
/// cannot produce the same value. The clock is the host's registered <see cref="TimeProvider"/>, which is
/// one instance per process in production.
/// </para>
/// <para>
/// <see cref="ForRandomNode(TimeProvider)"/> gives each clock a node id of its own for up to 4096 clocks per
/// process, so within that limit two containers with clocks of their own that configured no node id cannot
/// produce the same value at the same instant either. After 4096 clocks a node id repeats, and two clocks
/// that share one can. The counter stays per clock rather than per node: a counter shared across clocks
/// would let one clock's time run into another's ids, so a test clock set ahead would misdate the ids of
/// every other clock in the process. A node id a host configures is that host's promise to be the only writer using
/// it, which is why the same configured node id over two clocks is as wrong as over two processes.
/// </para>
/// </remarks>
public sealed class TimeOrderedIdGenerator : ITimeOrderedIdGenerator
{
    /// <summary>The largest node id, 4095.</summary>
    public const int MaxNodeId = (1 << NodeBits) - 1;

    /// <summary>The largest sequence within one millisecond, 1023.</summary>
    public const int MaxSequence = (1 << SequenceBits) - 1;

    private const int NodeBits = 12;
    private const int SequenceBits = 10;
    private const int TimeBits = 41;
    private const long MaxMilliseconds = (1L << TimeBits) - 1;

    /// <summary>
    /// The generators handed out so far, per clock and node id. Weak on the clock, so a clock a test
    /// made and dropped takes its generators with it.
    /// </summary>
    private static readonly ConditionalWeakTable<TimeProvider, ConcurrentDictionary<int, TimeOrderedIdGenerator>> PerClock = new();

    /// <summary>The generator for a host that configured no node id, per clock. Weak like the table above.</summary>
    private static readonly ConditionalWeakTable<TimeProvider, TimeOrderedIdGenerator> PerClockRandomNode = new();

    /// <summary>
    /// The last node id handed to a clock, only ever moved with <see cref="Interlocked"/>. Starts at random
    /// once per process, so processes differ, and counts up from there, so up to 4096 clocks of one process
    /// differ and after that a node id repeats. It is static because uniqueness has to hold across the
    /// containers of a process, and a singleton from dependency injection exists once per container.
    /// </summary>
    private static readonly StrongBox<int> LastRandomNodeId = new(RandomNumberGenerator.GetInt32(MaxNodeId + 1));

    private readonly TimeProvider _timeProvider;

    /// <summary>Milliseconds since the epoch and sequence of the last value, packed.</summary>
    private long _counter;

    /// <summary>
    /// Initializes a new instance of the <see cref="TimeOrderedIdGenerator"/> class.
    /// </summary>
    /// <param name="nodeId">The node id, from 0 to <see cref="MaxNodeId"/>.</param>
    /// <param name="timeProvider">The clock the time field is read from.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="nodeId"/> is out of range.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="timeProvider"/> is null.</exception>
    /// <remarks>
    /// A new instance has a counter of its own. Production code takes
    /// <see cref="ForNode(int, TimeProvider)"/> so a node id has one counter per process; this
    /// constructor is for a test that drives the clock.
    /// </remarks>
    public TimeOrderedIdGenerator(int nodeId, TimeProvider timeProvider)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(nodeId);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(nodeId, MaxNodeId);

        NodeId = nodeId;
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    /// <summary>Gets the instant the time field counts from: 2025-01-01T00:00:00Z.</summary>
    /// <remarks>41 bits of milliseconds from there reach into 2094.</remarks>
    public static DateTimeOffset Epoch { get; } = new(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <inheritdoc />
    public int NodeId { get; }

    /// <summary>Returns the process-wide generator for a node id over a clock.</summary>
    /// <param name="nodeId">The node id, from 0 to <see cref="MaxNodeId"/>.</param>
    /// <param name="timeProvider">The clock the time field is read from: the host's registered one.</param>
    /// <returns>The same instance for the same node id and clock for the lifetime of the process.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="nodeId"/> is out of range.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="timeProvider"/> is null.</exception>
    public static TimeOrderedIdGenerator ForNode(int nodeId, TimeProvider timeProvider)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(nodeId);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(nodeId, MaxNodeId);
        ArgumentNullException.ThrowIfNull(timeProvider);

        return PerClock
            .GetValue(timeProvider, static _ => new ConcurrentDictionary<int, TimeOrderedIdGenerator>())
            .GetOrAdd(nodeId, static (id, clock) => new TimeOrderedIdGenerator(id, clock), timeProvider);
    }

    /// <summary>
    /// Returns the process-wide generator used when the host configured no node id: one node id per clock,
    /// from a start drawn at random for the lifetime of the process. Up to 4096 clocks per process have
    /// different node ids; after that a node id repeats.
    /// </summary>
    /// <param name="timeProvider">The clock the time field is read from: the host's registered one.</param>
    /// <returns>The same instance for the same clock for the lifetime of the process.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="timeProvider"/> is null.</exception>
    public static TimeOrderedIdGenerator ForRandomNode(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        return PerClockRandomNode.GetValue(
            timeProvider,
            static clock => ForNode(Interlocked.Increment(ref LastRandomNodeId.Value) & MaxNodeId, clock));
    }

    /// <summary>Splits an identifier into the instant, node id and sequence it was made from.</summary>
    /// <param name="id">An identifier this type produced.</param>
    /// <returns>The instant (millisecond precision), the node id and the sequence.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="id"/> is negative.</exception>
    public static TimeOrderedIdInfo Decompose(long id)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(id);

        var milliseconds = id >> (NodeBits + SequenceBits);
        var nodeId = (int)((id >> SequenceBits) & MaxNodeId);
        var sequence = (int)(id & MaxSequence);

        return new TimeOrderedIdInfo(Epoch.AddMilliseconds(milliseconds), nodeId, sequence);
    }

    /// <inheritdoc />
    public long NextId()
    {
        var now = (_timeProvider.GetUtcNow() - Epoch).Ticks / TimeSpan.TicksPerMillisecond;
        var floor = Math.Max(now, 0) << SequenceBits;

        while (true)
        {
            var last = Volatile.Read(ref _counter);
            var next = Math.Max(last + 1, floor);

            if (next >> SequenceBits > MaxMilliseconds)
            {
                throw new InvalidOperationException(
                    "The time-ordered id space is exhausted: the time field holds 41 bits of milliseconds since 2025-01-01.");
            }

            if (Interlocked.CompareExchange(ref _counter, next, last) == last)
                return Compose(next);
        }
    }

    private long Compose(long counter)
    {
        var milliseconds = counter >> SequenceBits;
        var sequence = counter & MaxSequence;

        return (milliseconds << (NodeBits + SequenceBits)) | ((long)NodeId << SequenceBits) | sequence;
    }
}
