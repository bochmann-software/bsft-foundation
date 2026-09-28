namespace Bsft.Messaging.Tests.Fakes;

/// <summary>
/// Carries a member no converter can read back. System.Text.Json refuses <see cref="Type"/> outright,
/// and it does so with a NotSupportedException rather than the JsonException a malformed payload gets.
/// </summary>
public record UndecodableEvent : IIntegrationEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();

    public DateTimeOffset OccurredAt { get; init; }

    public Type? Value { get; init; }
}
