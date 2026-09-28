namespace Bsft.Messaging;

/// <summary>
/// Supplies the correlation id that is stored with a staged integration event and restored when it is
/// handled, so logs, traces and the audit trail can follow one request through a message that is
/// delivered later and possibly in another service.
/// </summary>
/// <remarks>
/// The messaging packages do not reference <c>Bsft.Correlation</c>: that would drag the ASP.NET Core
/// request pipeline into a background component. Register the bridge from
/// <c>Bsft.Messaging.Correlation</c> to connect the two. Without it, staged events carry no
/// correlation id except the one of a message being handled, and messaging behaves exactly as before.
/// </remarks>
public interface IMessageCorrelationAccessor
{
    /// <summary>Gets the correlation id to stage an event under, or <c>null</c> when unknown.</summary>
    string? CorrelationId { get; }
}
