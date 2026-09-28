namespace Bsft.Scheduling;

/// <summary>
/// The value range and vocabulary of one cron field: what numbers it accepts, which names stand in
/// for them, and what to call it in an error message.
/// </summary>
/// <param name="FieldName">The field name as it appears in parse errors, for example "day-of-week".</param>
/// <param name="Min">The lowest value the field accepts.</param>
/// <param name="Max">The highest value the field accepts.</param>
/// <param name="Names">Optional aliases such as <c>JAN</c> or <c>MON</c>, or <c>null</c> when the field is numeric only.</param>
internal sealed record CronFieldRange(
    string FieldName,
    int Min,
    int Max,
    IReadOnlyDictionary<string, int>? Names);
