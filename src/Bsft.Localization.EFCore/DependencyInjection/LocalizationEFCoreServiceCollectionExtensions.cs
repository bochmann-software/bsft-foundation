using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Bsft.Localization.EFCore.DependencyInjection;

/// <summary>
/// Extension methods for storing translations in the application's own database.
/// </summary>
public static class LocalizationEFCoreServiceCollectionExtensions
{
    /// <summary>
    /// Registers the EF Core translation reader against the application's context.
    /// </summary>
    /// <typeparam name="TContext">The application's context, implementing <see cref="ITranslationDbContext"/>.</typeparam>
    /// <param name="services">The service collection to add to.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
    /// <remarks>
    /// <para>
    /// Apply <see cref="TranslationEntityConfiguration"/> in the context's <c>OnModelCreating</c> and add
    /// a migration for it. Without it the entity still maps by convention, but the unique index over
    /// key, locale and tenant is silently absent — and that index is the only thing stopping one tenant
    /// from holding two overrides of one key, which the resolution chain would then pick between by
    /// whatever order the provider happened to return.
    /// </para>
    /// <para>
    /// The package has no options of its own and therefore no configuration section: what it registers
    /// is decided by the context type, which is a compile-time choice.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddBsftLocalizationEFCore<TContext>(this IServiceCollection services)
        where TContext : DbContext, ITranslationDbContext
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<ITranslationDbContext>(sp => sp.GetRequiredService<TContext>());
        services.TryAddScoped<ITranslationReader, EFCoreTranslationReader>();

        return services;
    }
}
