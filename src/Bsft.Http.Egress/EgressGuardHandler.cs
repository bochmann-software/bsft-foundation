namespace Bsft.Http.Egress;

/// <summary>
/// Refuses a request whose URL the egress policy rejects, before the request is sent.
/// </summary>
/// <remarks>
/// <para>
/// It is the cheap half of the defence and not the load-bearing one. What it catches is a destination
/// that is wrong on its face — a scheme that is not https, a single-label internal name, a URL that
/// names <c>169.254.169.254</c> outright — and it catches it with the request URI in hand, so the
/// failure says which request was refused instead of surfacing as a connection that did not open.
/// </para>
/// <para>
/// It deliberately resolves nothing. A handler that resolved the name here and let the socket resolve
/// it again would be checking one answer and connecting on another, which is the DNS-rebinding race
/// this package exists to close. That check belongs at connect time and lives in
/// <see cref="IOutboundConnectGuard"/>; pair the two by registering this handler on a client whose
/// primary handler carries the guard's connect callback, which is what
/// <c>AddBsftHttpEgressGuard</c> does.
/// </para>
/// </remarks>
public sealed class EgressGuardHandler : DelegatingHandler
{
    private readonly IOutboundAddressPolicy _policy;

    /// <summary>
    /// Initializes a new instance of the <see cref="EgressGuardHandler"/> class.
    /// </summary>
    /// <param name="policy">The policy the request URL is judged against.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="policy"/> is null.</exception>
    public EgressGuardHandler(IOutboundAddressPolicy policy)
    {
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="request"/> is null.</exception>
    /// <exception cref="HttpRequestException">
    /// Thrown when the policy refused the URL, carrying an <see cref="OutboundAddressRejectedException"/>
    /// as its cause.
    /// </exception>
    /// <remarks>
    /// The refusal is wrapped, because the connect-time half of the same defence is wrapped: a
    /// <see cref="SocketsHttpHandler"/> reports whatever its connect callback threw inside an
    /// <see cref="HttpRequestException"/>. A caller that treats a blocked destination as an
    /// unreachable one — which is what most callers want — should not have to catch two different
    /// exception types depending on which half of the policy refused, and a caller that wants to tell
    /// a refusal from a network failure reads the cause either way.
    /// </remarks>
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var decision = _policy.Evaluate(request.RequestUri);

        if (!decision.IsAllowed)
            throw new HttpRequestException(decision.Reason, new OutboundAddressRejectedException(decision.Reason));

        return base.SendAsync(request, cancellationToken);
    }
}
