namespace Bsft.Idempotency;

/// <summary>
/// Marks a controller action — or every action of a controller — as one the idempotency middleware
/// protects, so a repeated request under the same <c>Idempotency-Key</c> replays the first response
/// instead of running again.
/// </summary>
/// <remarks>
/// <para>
/// The attribute carries no behaviour: it is endpoint metadata, and <c>UseBsftIdempotency()</c> is
/// what reads it. An endpoint that is not marked is untouched, which is the point — the middleware
/// sits in the pipeline of the whole application but decides nothing about routes whose author did
/// not opt in.
/// </para>
/// <para>
/// It belongs on unsafe operations that are not idempotent by construction. Under
/// <c>docs/crud-conventions.md</c> that is <c>POST /api/{resource}</c>; <c>PUT</c> and
/// <c>DELETE</c> replace or remove a named resource and need no key.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [HttpPost]
/// [Idempotent]
/// public async Task&lt;ActionResult&lt;CreatedResponse&gt;&gt; Create(CreateProductRequest request) { ... }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class IdempotentAttribute : Attribute, IIdempotencyMetadata
{
    /// <summary>
    /// Initializes a new instance of the <see cref="IdempotentAttribute"/> class, leaving whether a
    /// key is mandatory to <see cref="BsftIdempotencyOptions.ShouldRequireKey"/>.
    /// </summary>
    public IdempotentAttribute()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IdempotentAttribute"/> class that overrides the
    /// host's default about a missing key.
    /// </summary>
    /// <param name="requireKey">
    /// <see langword="true"/> to answer 428 when this endpoint is called without a key, whatever the
    /// application default is; <see langword="false"/> to exempt this one endpoint from a strict
    /// default.
    /// </param>
    /// <remarks>
    /// A constructor overload rather than a settable property, because an attribute argument cannot
    /// be a nullable <see cref="bool"/> and the tri-state — "require", "do not require", "whatever
    /// the host says" — is the whole point of the member.
    /// </remarks>
    public IdempotentAttribute(bool requireKey) => ShouldRequireKey = requireKey;

    /// <inheritdoc />
    public bool? ShouldRequireKey { get; }
}
