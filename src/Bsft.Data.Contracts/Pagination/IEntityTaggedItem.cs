namespace Bsft.Data.Contracts.Pagination;

/// <summary>
/// A list item that carries the entity tag of the resource row it stands for, so a client can go
/// from a row of <see cref="PagedResult{T}"/> straight to a conditional write on that row.
/// </summary>
/// <remarks>
/// <para>
/// Without it, a screen bound to a list has to spend one <c>GET</c> by id per row before every edit
/// or delete, purely to learn the validator its <c>If-Match</c> needs. The tag is already known when
/// the list is projected — it is derived from the same version the row was read at — so publishing
/// it on the item removes that round trip entirely.
/// </para>
/// <para>
/// The member is nullable because it is additive: a resource that has no concurrency token, and a
/// host that has not adopted conditional writes, leaves it null and is unaffected. A null tag means
/// "this resource publishes no validator", never "the validator is empty" — a client that finds none
/// falls back to reading the resource, exactly as it had to before.
/// </para>
/// <para>
/// Two things about the value are part of the convention rather than left to the resource. It is the
/// <em>same</em> tag the resource's own <c>GET</c> by id puts on <c>ETag</c>, derived from the
/// version with <c>Bsft.Http</c>'s <c>ToVersionEntityTag</c> and never with a second spelling: a list
/// tag that merely resembled the detail tag would fail every conditional write it was used for. And
/// it is serialized as <c>etag</c>, which implementations pin with
/// <c>[property: JsonPropertyName("etag")]</c> — the default camel-case policy would otherwise spell
/// the acronym <c>eTag</c>, and the member a generic client looks for is named after the header it
/// is echoed into.
/// </para>
/// <para>
/// Declared here, next to the paged result it travels in, so that the contract a dependency-free
/// client is written against says what a tagged list item is. The derivation itself stays in
/// <c>Bsft.Http</c>, which is where entity tags are built; this package keeps its empty dependency
/// list.
/// </para>
/// </remarks>
public interface IEntityTaggedItem
{
    /// <summary>
    /// Gets the strong entity tag of the row, in the <c>"sha256-&lt;hex&gt;"</c> grammar the
    /// framework's endpoints serve, or <see langword="null"/> when the resource publishes none.
    /// </summary>
    string? ETag { get; }
}
