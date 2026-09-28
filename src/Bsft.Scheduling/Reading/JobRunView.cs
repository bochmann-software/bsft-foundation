namespace Bsft.Scheduling.Reading;

/// <summary>
/// One job run together with the arguments it was enqueued with.
/// </summary>
/// <remarks>
/// The payload comes back in the form the store holds it rather than deserialized: a listing crosses
/// every job at once and has no way to know what any one of them carries.
/// </remarks>
/// <param name="Summary">Which job, where it stands and when.</param>
/// <param name="Payload">The arguments, or null when the run was enqueued without any.</param>
public sealed record JobRunView(JobRunSummary Summary, string? Payload);
