using Bsft.Data.MultiTenancy;
using Bsft.Localization.EFCore;
using Bsft.Localization.EFCore.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Bsft.Localization.Tests.Support;

/// <summary>
/// Builds the scope factory the localizer resolves its reader and tenant context out of.
/// </summary>
/// <remarks>
/// The reader is the real EF Core one, over an in-memory context: these tests are about the
/// resolution chain, and a fake reader would let the chain agree with a fake rather than with the
/// queries the framework actually issues. The overload taking a reader is for the one question the
/// in-memory provider cannot answer — what the chain does with rows a case-insensitive store
/// matched under another spelling.
/// </remarks>
internal static class TranslationReaderFactory
{
    /// <summary>Builds the scope factory over the shipped EF Core reader.</summary>
    /// <param name="dbContext">The store the reader reads.</param>
    /// <param name="tenantId">The tenant the scope is serving, or <see langword="null"/> for none.</param>
    /// <returns>The scope factory.</returns>
    public static IServiceScopeFactory ScopeFactoryFor(TestTranslationDbContext dbContext, string? tenantId = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ITranslationDbContext>(dbContext);
        services.AddBsftLocalizationEFCore<TestTranslationDbContext>();

        return ScopeFactoryFor(services, tenantId);
    }

    /// <summary>Builds the scope factory over a store that answers with fixed rows.</summary>
    /// <param name="reader">The store the chain reads through.</param>
    /// <param name="tenantId">The tenant the scope is serving, or <see langword="null"/> for none.</param>
    /// <returns>The scope factory.</returns>
    public static IServiceScopeFactory ScopeFactoryFor(ITranslationReader reader, string? tenantId = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(reader);

        return ScopeFactoryFor(services, tenantId);
    }

    /// <summary>States the tenant the scope is serving, if there is one, and builds the factory.</summary>
    /// <param name="services">The registrations the store half already made.</param>
    /// <param name="tenantId">The tenant the scope is serving, or <see langword="null"/> for none.</param>
    /// <returns>The scope factory.</returns>
    private static IServiceScopeFactory ScopeFactoryFor(IServiceCollection services, string? tenantId)
    {
        if (tenantId is not null)
        {
            var tenantContext = Substitute.For<ITenantContext>();
            tenantContext.IsResolved.Returns(true);
            tenantContext.TenantId.Returns(tenantId);
            services.AddSingleton(tenantContext);
        }

        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }
}
