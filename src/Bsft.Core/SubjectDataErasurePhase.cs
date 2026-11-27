namespace Bsft.Core;

/// <summary>When, within the deletion of an account, an <see cref="ISubjectDataEraser"/> runs.</summary>
public enum SubjectDataErasurePhase
{
    /// <summary>
    /// Ends what grants access or can be re-created by a sign-in: sessions, tokens, credentials, factors, memberships,
    /// API keys. Every eraser of this phase runs before any of the <see cref="Data"/> phase, and runs once more after the
    /// account row is gone, to sweep what a sign-in that raced the delete established.
    /// </summary>
    Revoke = 0,

    /// <summary>Deletes data that grants nothing: preferences, notifications, consents, history. Runs once, after <see cref="Revoke"/>.</summary>
    Data = 1,
}
