namespace Bsft.Idempotency;

/// <summary>
/// The part of a response that is stored and written again on a replay: the status, the allowlisted
/// headers, and the body exactly as it was produced.
/// </summary>
/// <param name="StatusCode">The status the first request answered with.</param>
/// <param name="Headers">
/// The headers named by <see cref="BsftIdempotencyOptions.CachedResponseHeaders"/> and nothing else.
/// <c>Location</c> is the one that matters most: a create answers 201 with it, and a replay that
/// dropped it would leave a retrying client unable to find what it created.
/// </param>
/// <param name="Body">
/// The response body as bytes. Bytes rather than a string, because a replay has to be
/// byte-identical: re-serializing a decoded body would move whitespace and number formatting and
/// make the second answer differ from the first in ways no client can be asked to tolerate.
/// </param>
/// <remarks>
/// The body is an array, so two snapshots are equal only when they hold the same array instance.
/// Nothing compares snapshots; the record is a carrier, and the comparison that decides anything is
/// the fingerprint.
/// </remarks>
public sealed record IdempotencyResponseSnapshot(
    int StatusCode,
    IReadOnlyDictionary<string, string> Headers,
    byte[] Body);
