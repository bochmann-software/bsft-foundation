using Bsft.Messaging.Consuming;

namespace Bsft.Messaging.Tests.Fakes;

/// <summary>
/// A decoder that fails the way a decoder is not supposed to: with an exception that does not describe
/// itself as a decode refusal. It stands in for an implementation raising outside its contract.
/// </summary>
public sealed class FailingIncomingMessageDecoder : IIncomingMessageDecoder
{
    public Exception Failure { get; set; } = new NotSupportedException("no converter for this member");

    public IIntegrationEvent Decode(IntegrationEventMessage message) => throw Failure;
}
