namespace Bsft.Core;

/// <summary>
/// Deletes everything one package stores about a subject, when the account is deleted.
/// </summary>
/// <remarks>
/// <para>
/// A contributor seam: every package that owns a table keyed by an account registers one implementation
/// next to its stores (<c>TryAddEnumerable</c>), and whoever deletes an account runs all of them. The
/// contract lives here because it is the one type every one of those packages already reaches; the
/// package that deletes the account is <c>Bsft.Auth.Oidc.Organization</c>.
/// </para>
/// <para>
/// <strong>Contract.</strong> An implementation deletes rows and never revokes: nothing it leaves behind
/// may name the subject. It is idempotent, because a delete that failed half-way is run again, and it
/// treats a subject without data as success. It declares its <see cref="Phase"/>. It performs no authorization; the caller has authorized the
/// deletion. It does not span a transaction of its own across statements, so it orders its statements
/// children first and parents last, which makes a re-run after a partial failure converge.
/// </para>
/// </remarks>
public interface ISubjectDataEraser
{
    /// <summary>Gets when, within the deletion of an account, this eraser runs.</summary>
    /// <remarks>
    /// Every <see cref="SubjectDataErasurePhase.Revoke"/> eraser runs before any <see cref="SubjectDataErasurePhase.Data"/>
    /// eraser, so that nothing that grants access outlives the data it is deleted next to. Within a phase the order is that
    /// of registration and must not matter.
    /// </remarks>
    SubjectDataErasurePhase Phase { get; }

    /// <summary>Deletes everything this package stores about one subject.</summary>
    /// <param name="subjectId">The subject, which is the account's identifier.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A task that completes when no row of this package names the subject.</returns>
    Task EraseAsync(Guid subjectId, CancellationToken cancellationToken = default);
}
