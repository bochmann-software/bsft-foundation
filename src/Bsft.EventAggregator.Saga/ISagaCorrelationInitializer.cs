namespace Bsft.EventAggregator.Saga;

/// <summary>
/// Hands the correlation id a step is running under to the correlation implementation of the host,
/// so that a step observes the id of whatever caused it rather than one minted for its own scope.
/// </summary>
/// <remarks>
/// <para>
/// A saga step runs in a service scope the flow host creates from the root provider, which is what
/// makes a step independent of the request that triggered it. The cost is that every scoped service
/// in it starts empty, and a correlation context that mints an id per scope therefore starts a new
/// correlation on every hop — both for a step woken by a firing timer and for one triggered
/// synchronously out of an HTTP request.
/// </para>
/// <para>
/// The saga engine has no notion of correlation of its own, so this is a seam rather than a
/// behaviour: it is called only when the host registered an implementation, and the step keeps its
/// own fresh id when nothing is ambient. <c>Bsft.EventAggregator.Saga.Correlation</c> implements it
/// over <c>Bsft.Correlation</c>.
/// </para>
/// </remarks>
public interface ISagaCorrelationInitializer
{
    /// <summary>Adopts the given correlation id for the current step scope.</summary>
    /// <param name="correlationId">The correlation id the step is running under.</param>
    void Restore(string correlationId);
}
