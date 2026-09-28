namespace Bsft.Mediator;

/// <summary>
/// Puts the resolved pipeline behaviors into the order the pipeline is folded in. It is registered
/// only when the host asked for the defaults, so a host that wires its own chain keeps registration
/// order as the pipeline order.
/// </summary>
internal interface IPipelineBehaviorOrderer
{
    /// <summary>
    /// Orders the behaviors resolved for one dispatch, outermost first.
    /// </summary>
    /// <typeparam name="TBehavior">The closed pipeline behavior contract.</typeparam>
    /// <param name="behaviors">The behaviors in registration order.</param>
    /// <returns>The behaviors in the order the pipeline is folded in.</returns>
    IReadOnlyList<TBehavior> Order<TBehavior>(IReadOnlyList<TBehavior> behaviors)
        where TBehavior : notnull;
}
