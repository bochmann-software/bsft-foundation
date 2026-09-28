namespace Bsft.Messaging;

/// <summary>
/// One outbox row with the message it holds.
/// </summary>
/// <remarks>
/// The content read. What an integration event carries is whatever the publishing aggregate put in
/// it, which in practice is customer data, so this is answered by its own route behind its own
/// permission and the read is recorded.
/// </remarks>
/// <param name="Summary">Everything the listing already showed.</param>
/// <param name="PayloadJson">The serialized event, exactly as it would be published.</param>
public sealed record OutboxMessageView(OutboxMessageSummary Summary, string PayloadJson);
