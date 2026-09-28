namespace Bsft.Http.Egress;

/// <summary>
/// Thrown when a destination resolves to an address this application will not call.
/// </summary>
/// <remarks>
/// It is raised from the connect callback, so it surfaces to the caller wrapped in an
/// <see cref="HttpRequestException"/> exactly as a refused connection would. That is deliberate: the
/// attempt failed for a reason that is about the destination rather than about the network, and a
/// caller that recognises it can refuse for good rather than retrying an address that will not become
/// acceptable.
/// </remarks>
public sealed class OutboundAddressRejectedException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="OutboundAddressRejectedException"/> class.</summary>
    public OutboundAddressRejectedException()
        : this("The endpoint resolves to an address this application may not call.")
    {
    }

    /// <summary>Initializes a new instance of the <see cref="OutboundAddressRejectedException"/> class.</summary>
    /// <param name="message">Why the address was rejected. It names the rule, never the address.</param>
    public OutboundAddressRejectedException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="OutboundAddressRejectedException"/> class.</summary>
    /// <param name="message">Why the address was rejected.</param>
    /// <param name="innerException">The failure underneath, when there was one.</param>
    public OutboundAddressRejectedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
