using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Bsft.Data.Search;

/// <summary>
/// Registers a search provider together with the declaration it answers for.
/// </summary>
/// <remarks>
/// The three provider packages register themselves the same way, down to the order of the argument
/// checks, and the one thing that is easy to get wrong about it — copying the caller's declaration at
/// the call rather than when the options are first resolved — was got wrong in all three at once.
/// Written here, it is one place to get right. Internal because it is not a surface an application
/// reaches; the provider packages see it through <c>InternalsVisibleTo</c>.
/// </remarks>
internal static class SearchProviderRegistration
{
    /// <summary>
    /// Registers the provider and the declaration it reads, taking a copy of the declaration.
    /// </summary>
    /// <typeparam name="TOptions">The provider's options type.</typeparam>
    /// <typeparam name="TProvider">The provider implementation.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="options">The declaration the caller built.</param>
    /// <param name="copyTo">Copies the first instance's declaration onto the second.</param>
    /// <returns>The same service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    /// <remarks>
    /// The copy is taken here rather than when the options are first resolved, so a caller that keeps
    /// building on its own instance after the call cannot change what the provider answers for.
    /// </remarks>
    public static IServiceCollection Add<TOptions, TProvider>(
        IServiceCollection services,
        TOptions options,
        Action<TOptions, TOptions> copyTo)
        where TOptions : class, new()
        where TProvider : class, ISearchProvider
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(copyTo);

        var declared = new TOptions();
        copyTo(options, declared);

        services.AddOptions<TOptions>().Configure(target => copyTo(declared, target));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ISearchProvider, TProvider>());
        return services;
    }
}
