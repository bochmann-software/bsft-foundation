namespace Bsft.Idempotency;

/// <summary>
/// Marks an idempotent endpoint whose response carries a secret, such as a minted client secret or an
/// API key, so the response stored for replay is encrypted at rest instead of kept as plaintext.
/// </summary>
/// <remarks>
/// <para>
/// Without it the idempotency store keeps the first response body as it was sent, for the whole replay
/// window (<see cref="BsftIdempotencyOptions.Retention"/>, 24 hours by default), and that is the right
/// default for an ordinary create. A response that discloses a credential exactly once is not
/// ordinary: a plaintext copy in the record table is a second, longer-lived disclosure that nobody
/// rotates. With the marker the body is sealed with ASP.NET Core Data Protection before it is stored and
/// unsealed when it is replayed; the status and the allowlisted headers are stored as before.
/// </para>
/// <para>
/// When a stored body cannot be unsealed — the key ring is not shared between the replicas, a key was
/// revoked, the record was written before the marker was added — the replay is answered with a 409
/// carrying <see cref="IdempotencyDefaults.ResponseWithheldErrorCode"/> rather than with anything
/// readable: the request did run, the secret was issued, and the client rotates it instead of retrying.
/// </para>
/// <para>
/// It is read only on an endpoint that also carries <see cref="IIdempotencyMetadata"/>; on its own it
/// protects nothing because nothing is stored.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class SensitiveIdempotentResponseAttribute : Attribute
{
}
