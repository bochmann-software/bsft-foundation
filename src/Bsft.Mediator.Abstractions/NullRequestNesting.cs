namespace Bsft.Mediator;

/// <summary>
/// The <see cref="IRequestNesting"/> of a behavior constructed without the scope's marks: it reports no
/// outer request and forgets every mark.
/// </summary>
/// <remarks>
/// It keeps the constructors that predate <see cref="IRequestNesting"/> working: a behavior built with
/// it treats every request as the outermost of its scope, which is how such a behavior has always
/// behaved.
/// </remarks>
public sealed class NullRequestNesting : IRequestNesting
{
    /// <summary>Gets the shared instance; it holds no state.</summary>
    public static NullRequestNesting Instance { get; } = new();

    /// <inheritdoc />
    public bool IsRetrying => false;

    /// <inheritdoc />
    public bool IsTransactionOpen => false;

    /// <inheritdoc />
    public void MarkRetrying()
    {
        // Nothing, on purpose: there is no scope to mark.
    }

    /// <inheritdoc />
    public void MarkTransactionOpen()
    {
        // Nothing, on purpose: there is no scope to mark.
    }
}
