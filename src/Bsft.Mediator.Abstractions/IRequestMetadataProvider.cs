namespace Bsft.Mediator;

/// <summary>
/// Supplies the <see cref="IRequestMetadata"/> descriptor for a dispatched request, so a pipeline
/// behavior can read the concrete request type and its attributes without reflecting on every call.
/// </summary>
/// <remarks>
/// Registered by <c>AddBsftMediator</c> as a singleton and injectable into any behavior. It lives in
/// the abstractions package on purpose: a behavior satellite (<c>Bsft.&lt;X&gt;.Mediator</c>)
/// references the contracts only, and taking this seam must not drag the dispatcher into its closure.
/// <para>
/// Implementations cache per request type and are safe to call from several threads at once.
/// </para>
/// </remarks>
public interface IRequestMetadataProvider
{
    /// <summary>
    /// Returns the descriptor for the runtime type of the given request instance.
    /// </summary>
    /// <param name="request">The dispatched request. Must not be <c>null</c>.</param>
    /// <returns>The descriptor for <c>request.GetType()</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <c>null</c>.</exception>
    IRequestMetadata GetMetadata(object request);

    /// <summary>
    /// Returns the descriptor for a request type, for a caller that has the type but no instance.
    /// </summary>
    /// <param name="requestType">The concrete request type. Must not be <c>null</c>.</param>
    /// <returns>The descriptor for <paramref name="requestType"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="requestType"/> is <c>null</c>.</exception>
    IRequestMetadata GetMetadata(Type requestType);
}
