namespace Bsft.Data.Transactions;

/// <summary>
/// Knows the container's root provider, so that a context whose options are built from it — a pooled
/// context, a context factory, or a context resolved outside any scope — is refused instead of handed
/// one shared transaction for every request.
/// </summary>
/// <remarks>
/// Registered as a singleton through a factory, which the container calls with its root provider. A
/// scoped context's options are built with the provider of its scope, which is never the root one.
/// </remarks>
internal sealed class SharedTransactionRoot
{
    private readonly IServiceProvider _rootProvider;

    /// <summary>Initializes a new instance of the <see cref="SharedTransactionRoot"/> class.</summary>
    /// <param name="rootProvider">The container's root provider.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="rootProvider"/> is null.</exception>
    public SharedTransactionRoot(IServiceProvider rootProvider) =>
        _rootProvider = rootProvider ?? throw new ArgumentNullException(nameof(rootProvider));

    /// <summary>Throws when <paramref name="serviceProvider"/> is the root provider.</summary>
    /// <param name="serviceProvider">The provider a context's options are being built with.</param>
    /// <param name="contextType">The type of the context.</param>
    /// <exception cref="InvalidOperationException">Thrown when the options are built outside a scope.</exception>
    public void ThrowIfRoot(IServiceProvider serviceProvider, Type contextType)
    {
        if (ReferenceEquals(serviceProvider, _rootProvider))
        {
            throw new InvalidOperationException(
                $"UseBsftSharedTransaction configures the context '{contextType.Name}' from the root service provider, outside any scope. Its options would then hold one connection and one transaction for every request: register the context with AddDbContext, not AddDbContextPool or AddDbContextFactory, and resolve it from a scope.");
        }
    }
}
