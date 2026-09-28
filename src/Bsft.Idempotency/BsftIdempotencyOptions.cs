namespace Bsft.Idempotency;

/// <summary>
/// What a host decides about idempotent unsafe requests: which header carries the key, which methods
/// are considered, how long a first response stays replayable, and what is replayed with it.
/// </summary>
public sealed class BsftIdempotencyOptions
{
    /// <summary>The configuration section these options bind from.</summary>
    public const string SectionName = "Bsft:Idempotency";

    /// <summary>
    /// Gets or sets the request header the key is read from. Defaults to
    /// <see cref="IdempotencyDefaults.HeaderName"/>.
    /// </summary>
    public string HeaderName { get; set; } = IdempotencyDefaults.HeaderName;

    /// <summary>
    /// Gets the HTTP methods the middleware considers at all. Defaults to <c>POST</c> alone.
    /// </summary>
    /// <remarks>
    /// <c>PUT</c> and <c>DELETE</c> are idempotent by construction under
    /// <c>docs/crud-conventions.md</c>, so a key on them changes nothing and they are not listed. The
    /// one <c>PATCH</c> the framework serves is a merge patch, which is not idempotent in general, so
    /// it can be added here rather than being excluded by design.
    /// </remarks>
    public IList<string> Methods { get; } = new List<string> { "POST" };

    /// <summary>
    /// Gets or sets a value indicating whether an opted-in endpoint refuses a request that carries no
    /// key. Defaults to <see langword="false"/>, which lets an un-keyed request through unprotected.
    /// </summary>
    /// <remarks>
    /// Turning it on is what makes the protection non-optional for the caller: a client that forgets
    /// the header gets a 428 it can fix, rather than a duplicate it will not notice.
    /// </remarks>
    public bool ShouldRequireKey { get; set; }

    /// <summary>
    /// Gets or sets how long a completed record stays replayable. Defaults to 24 hours.
    /// </summary>
    /// <remarks>
    /// This is a contract, not an implementation detail: after it, the same key runs again. It is
    /// also the pruner's horizon.
    /// </remarks>
    public TimeSpan Retention { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// Gets or sets how long a reservation may stand before it is treated as abandoned and the key
    /// becomes retryable. Defaults to 60 seconds.
    /// </summary>
    /// <remarks>
    /// It is what makes a crash between reservation and completion recoverable without an operator:
    /// the lease lapses, and the next request under that key runs.
    /// </remarks>
    public TimeSpan InProgressTimeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Gets or sets the <c>Retry-After</c> the in-progress 409 asks for. Defaults to one second.
    /// </summary>
    /// <remarks>
    /// It is not derived from <see cref="InProgressTimeout"/>, which would tell a client to wait a
    /// minute for a request that usually takes 200 ms. The value actually sent is this capped by
    /// what is left of the lease, so a client is never told to wait past the point where the key is
    /// retryable anyway.
    /// </remarks>
    public TimeSpan InProgressRetryAfter { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Gets or sets the largest request body the fingerprint will buffer and hash. Defaults to 1 MiB.
    /// </summary>
    /// <remarks>
    /// A body above it is refused with a 413 rather than let through: an un-hashed body means the
    /// endpoint is not protected, and silent non-protection is the failure this package exists to
    /// remove. Endpoints that stream — a file upload — do not opt in at all.
    /// </remarks>
    public long MaxBodyBytes { get; set; } = 1024 * 1024;

    /// <summary>
    /// Gets or sets the largest response that is stored for replay. Defaults to 1 MiB.
    /// </summary>
    /// <remarks>
    /// A response above it is sent to the client normally and simply not stored, and the reservation
    /// is released so the key runs again. That is the opposite direction from
    /// <see cref="MaxBodyBytes"/>, and deliberately so: the request bound is refused because a body
    /// that is not hashed leaves the endpoint unprotected, whereas here the endpoint has already
    /// run and its side effect has already happened — refusing the response would throw away work
    /// the client is entitled to. What the bound buys is that one caller cannot grow the record
    /// table without limit by repeatedly asking for a large answer.
    /// </remarks>
    public long MaxResponseBytes { get; set; } = 1024 * 1024;

    /// <summary>Gets or sets the longest key accepted. Defaults to 255 characters.</summary>
    public int MaxKeyLength { get; set; } = 255;

    /// <summary>
    /// Gets the response headers stored with a completed response and written again on a replay.
    /// Defaults to <c>Location</c> and <c>Content-Type</c>.
    /// </summary>
    /// <remarks>
    /// An allowlist, never a copy of everything: a <c>Set-Cookie</c> or a one-time token replayed to
    /// a second caller would be a security defect, and the only way to be sure none is replayed is to
    /// name the ones that are. <c>Location</c> is on it because a create answers 201 with it, and a
    /// replay that dropped it would leave a retrying client unable to find what it created.
    /// </remarks>
    public IList<string> CachedResponseHeaders { get; } = new List<string> { "Location", "Content-Type" };

    /// <summary>
    /// Gets the client-error statuses that are <em>not</em> cached, so the request runs again.
    /// Defaults to 401, 403, 408 and 429.
    /// </summary>
    /// <remarks>
    /// A 4xx is replayable by default, because a request refused for a business reason will be
    /// refused again and replaying the refusal is cheaper and more truthful than re-running it. These
    /// four are the exceptions: the caller may have fixed its token, been granted the permission, or
    /// simply waited, and replaying the old refusal would keep it locked out of a request that would
    /// now succeed. Server errors are never cached and are not configurable — a retry after a 5xx is
    /// the whole point of the header.
    /// </remarks>
    public IList<int> NonReplayableStatusCodes { get; } = new List<int> { 401, 403, 408, 429 };

    /// <summary>
    /// Gets or sets whether the reservation commits with the endpoint's own transaction. Defaults to
    /// <see cref="IdempotencyMode.Detached"/>.
    /// </summary>
    public IdempotencyMode Mode { get; set; } = IdempotencyMode.Detached;
}
