namespace Bsft.Mediator;

/// <summary>
/// The names of the pipeline behaviors that <see cref="MediatorConfiguration.UseDefaults"/>
/// registers, in the order the framework fixes for them. A host passes one of these to
/// <see cref="MediatorConfiguration.SuppressDefaultBehavior"/> instead of a literal, so a typo is a
/// compile error rather than a security behavior that quietly disappears.
/// </summary>
public static class MediatorDefaults
{
    /// <summary>The tracing behavior, outermost so its span encloses every other behavior.</summary>
    public const string Tracing = "Tracing";

    /// <summary>The tenant authorization behavior from <c>Bsft.Auth.Mediator</c>.</summary>
    public const string Authorization = "Authorization";

    /// <summary>The organization authorization behavior from <c>Bsft.Organization.Mediator</c>.</summary>
    public const string OrganizationAuthorization = "OrganizationAuthorization";

    /// <summary>The validation behavior, inside authorization so an unauthorized caller learns nothing about the payload.</summary>
    public const string Validation = "Validation";

    /// <summary>The audit behavior from <c>Bsft.Auditing.Mediator</c>.</summary>
    public const string Audit = "Audit";

    /// <summary>The retry behavior from <c>Bsft.Resilience.Mediator</c>, outside the transaction so each attempt gets its own.</summary>
    public const string Retry = "Retry";

    /// <summary>The transaction behavior, inside retry so every attempt begins, commits or rolls back its own transaction.</summary>
    /// <remarks>
    /// An attempt therefore never inherits a transaction a previous failure poisoned, which is what
    /// makes the default retry predicate — it retries everything but cancellation and the
    /// <c>Bsft.Core</c> domain exceptions, a deadlock or a dropped connection included — useful for a
    /// request marked both <c>[Transactional]</c> and <c>[Retryable]</c>. This matches EF Core's
    /// execution-strategy rule that a manually demarcated transaction is created inside the retried
    /// delegate. The handler must be safe to run twice, including any state tracked on a shared
    /// <c>DbContext</c>. See <c>docs/mediator.md</c> for the order and its escape hatch.
    /// </remarks>
    public const string Transaction = "Transaction";

    /// <summary>The caching behavior from <c>Bsft.Caching.Mediator</c>, innermost so it can never serve past authorization.</summary>
    public const string Caching = "Caching";
}
