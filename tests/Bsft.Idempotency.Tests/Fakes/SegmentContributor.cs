using Bsft.Idempotency.Fingerprinting;
using Microsoft.AspNetCore.Http;

namespace Bsft.Idempotency.Tests.Fakes;

/// <summary>Applies whatever transform a test handed it to the segment sequence.</summary>
internal sealed class SegmentContributor : IIdempotencyFingerprintContributor
{
    private readonly Func<HttpRequest, IReadOnlyList<string?>, IReadOnlyList<string?>?> _transform;

    public SegmentContributor(Func<HttpRequest, IReadOnlyList<string?>, IReadOnlyList<string?>?> transform) =>
        _transform = transform;

    public IReadOnlyList<string?> Contribute(HttpRequest request, IReadOnlyList<string?> segments) =>
        _transform(request, segments)!;
}
