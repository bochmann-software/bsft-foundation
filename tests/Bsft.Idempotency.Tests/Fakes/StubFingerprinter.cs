using Bsft.Idempotency.Fingerprinting;
using Microsoft.AspNetCore.Http;

namespace Bsft.Idempotency.Tests.Fakes;

/// <summary>
/// Answers with the digests a test dictated. Hand written rather than substituted, because
/// <see cref="IIdempotencyFingerprinter"/> is internal and a dynamic proxy of it would need
/// <c>DynamicProxyGenAssembly2</c> on the package under test.
/// </summary>
internal sealed class StubFingerprinter : IIdempotencyFingerprinter
{
    private readonly IdempotencyFingerprintResult _result;

    public StubFingerprinter(string scope = "scope", string fingerprint = "fingerprint") =>
        _result = new IdempotencyFingerprintResult(scope, fingerprint);

    public HttpContext? LastContext { get; private set; }

    public ValueTask<IdempotencyFingerprintResult> ComputeAsync(
        HttpContext context, CancellationToken ct = default)
    {
        LastContext = context;

        return ValueTask.FromResult(_result);
    }
}
