using Bsft.Mediator;

namespace Bsft.Caching.Mediator.Tests;

/// <summary>Handlers counting their invocations, so a cache hit is observable.</summary>
public sealed class CountingHandler :
    IQueryHandler<TaggedQuery, string>,
    IQueryHandler<PlainQuery, string>,
    ICommandHandler<CacheableCommand, string>
{
    private readonly Counter _counter;

    public CountingHandler(Counter counter)
    {
        _counter = counter ?? throw new ArgumentNullException(nameof(counter));
    }

    public Task<string> HandleAsync(TaggedQuery query, CancellationToken ct = default) => CountAsync();

    public Task<string> HandleAsync(PlainQuery query, CancellationToken ct = default) => CountAsync();

    public Task<string> HandleAsync(CacheableCommand command, CancellationToken ct = default) => CountAsync();

    private Task<string> CountAsync()
    {
        _counter.Calls++;
        return Task.FromResult($"result-{_counter.Calls}");
    }

    /// <summary>Shared invocation counter.</summary>
    public sealed class Counter
    {
        public int Calls { get; set; }
    }
}
