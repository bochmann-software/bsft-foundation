namespace Bsft.Mediator;

/// <summary>
/// The scoped marks of <see cref="IRequestNesting"/>, one <see cref="AsyncLocal{T}"/> per mark and scope.
/// </summary>
/// <remarks>
/// Each mark is an <see cref="AsyncLocal{T}"/> held by this scoped instance, so it is bound to the scope
/// by the instance and to the asynchronous flow by the <see cref="AsyncLocal{T}"/>: set inside a
/// behavior's asynchronous method, it flows into every request the handler dispatches in the same scope
/// and is gone again for the caller once that method returns.
/// </remarks>
internal sealed class RequestNesting : IRequestNesting
{
    private readonly AsyncLocal<bool> _retrying = new();
    private readonly AsyncLocal<bool> _transactionOpen = new();

    /// <inheritdoc />
    public bool IsRetrying => _retrying.Value;

    /// <inheritdoc />
    public bool IsTransactionOpen => _transactionOpen.Value;

    /// <inheritdoc />
    public void MarkRetrying() => _retrying.Value = true;

    /// <inheritdoc />
    public void MarkTransactionOpen() => _transactionOpen.Value = true;
}
