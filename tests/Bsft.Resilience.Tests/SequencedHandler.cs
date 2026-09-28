using System.Net;

namespace Bsft.Resilience.Tests;

/// <summary>
/// Test primary handler that returns a predefined sequence of status codes, one per attempt,
/// repeating the final status once the sequence is exhausted. Records every received request
/// so retries and propagated headers can be asserted.
/// </summary>
internal sealed class SequencedHandler : HttpMessageHandler
{
    private readonly Queue<HttpStatusCode> _statuses;
    private readonly object _gate = new();
    private int _callCount;

    public SequencedHandler(params HttpStatusCode[] statuses)
    {
        if (statuses.Length == 0)
            throw new ArgumentException("At least one status code is required.", nameof(statuses));

        _statuses = new Queue<HttpStatusCode>(statuses);
    }

    public IReadOnlyList<HttpRequestMessage> Requests
    {
        get
        {
            lock (_gate)
                return _recordedRequests.ToArray();
        }
    }

    public int CallCount => Volatile.Read(ref _callCount);

    private readonly List<HttpRequestMessage> _recordedRequests = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _callCount);

        HttpStatusCode status;
        lock (_gate)
        {
            _recordedRequests.Add(request);
            status = _statuses.Count > 1 ? _statuses.Dequeue() : _statuses.Peek();
        }

        return Task.FromResult(new HttpResponseMessage(status));
    }
}
