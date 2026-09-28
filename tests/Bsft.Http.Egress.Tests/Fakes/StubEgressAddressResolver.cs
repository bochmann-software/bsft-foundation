using System.Net;

namespace Bsft.Http.Egress.Tests.Fakes;

/// <summary>A resolver that answers with whatever the test wants, standing in for DNS.</summary>
public sealed class StubEgressAddressResolver : IEgressAddressResolver
{
    private readonly IReadOnlyList<IPAddress>[] _answers;
    private int _calls;

    public StubEgressAddressResolver(params IReadOnlyList<IPAddress>[] answers) => _answers = answers;

    public int Calls => _calls;

    public Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken ct = default)
    {
        // Successive answers let one test express the rebinding case: the name that validated as public
        // when the destination was stored answers with a private address at connect time. No answers at
        // all stands for a name that does not resolve.
        IReadOnlyList<IPAddress> answer = _answers.Length == 0
            ? []
            : _answers[Math.Min(_calls, _answers.Length - 1)];
        _calls++;

        return Task.FromResult(answer);
    }
}
