namespace Bsft.Logging.LogFiles;

/// <summary>Contains one newest-first page and the exclusive sequence for the next older page.</summary>
/// <param name="Entries">The entries, newest first.</param>
/// <param name="NextBefore">The oldest returned sequence when older matches remain; otherwise null.</param>
public sealed record LogReadPageResult(IReadOnlyList<LogFileEntry> Entries, long? NextBefore);
