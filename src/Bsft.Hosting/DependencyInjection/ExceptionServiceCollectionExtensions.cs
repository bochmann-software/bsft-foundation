using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Bsft.Hosting.DependencyInjection;

/// <summary>Extension methods for registering exception mappers, exception observers and the exception handler.</summary>
public static class ExceptionServiceCollectionExtensions
{
    /// <summary>
    /// Registers one <see cref="IBsftExceptionMapper"/>. Mappers are asked in registration order, before the
    /// framework's own mapping, and the first result wins; both <see cref="BsftExceptionMiddleware"/> and
    /// <see cref="BsftExceptionHandlerAdapter"/> use them.
    /// </summary>
    /// <typeparam name="TMapper">The mapper.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>Registered as a singleton, once per implementation.</remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
    public static IServiceCollection AddBsftExceptionMapper<TMapper>(this IServiceCollection services)
        where TMapper : class, IBsftExceptionMapper
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddBsftExceptionResponder();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IBsftExceptionMapper, TMapper>());

        return services;
    }

    /// <summary>
    /// Registers one <see cref="IBsftExceptionObserver"/>, together with what <c>AddBsftObservers</c> registers.
    /// Observers are told in registration order after an exception is mapped; their failures are contained.
    /// </summary>
    /// <typeparam name="TObserver">The observer.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when <see cref="IBsftExceptionObserver"/> is already registered with a lifetime other than singleton.</exception>
    public static IServiceCollection AddBsftExceptionObserver<TObserver>(this IServiceCollection services)
        where TObserver : class, IBsftExceptionObserver
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddBsftExceptionResponder();
        services.AddBsftObserver<IBsftExceptionObserver, TObserver>();

        return services;
    }

    /// <summary>
    /// Registers <see cref="BsftExceptionHandlerAdapter"/> as an <see cref="IExceptionHandler"/>, together with what
    /// <c>AddBsftObservers</c> registers, so <c>app.UseExceptionHandler()</c> answers with the same mapping as
    /// <see cref="BsftExceptionMiddleware"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// <c>UseExceptionHandler()</c> without a path needs <c>AddProblemDetails()</c>, or the host must
    /// give the middleware a path or a handler of its own; with the handler registered here that
    /// middleware never reaches them for an exception the handler took.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
    public static IServiceCollection AddBsftExceptionHandler(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddBsftExceptionResponder();
        services.AddExceptionHandler<BsftExceptionHandlerAdapter>();

        return services;
    }

    private static void AddBsftExceptionResponder(this IServiceCollection services)
    {
        services.AddBsftObservers();
        services.TryAddSingleton<BuiltInExceptionMapper>();
        services.TryAddSingleton<IBsftExceptionResponder, BsftExceptionResponder>();
    }
}
