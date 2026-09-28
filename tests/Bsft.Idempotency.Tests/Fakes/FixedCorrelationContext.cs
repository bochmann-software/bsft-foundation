using Bsft.Correlation;

namespace Bsft.Idempotency.Tests.Fakes;

/// <summary>The one dependency <c>BsftExceptionMiddleware</c> takes beyond the request itself.</summary>
internal sealed class FixedCorrelationContext : ICorrelationContext
{
    public string CorrelationId => "idempotency-test-correlation";
}
