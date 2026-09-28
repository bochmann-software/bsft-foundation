using Bsft.Data.MultiTenancy;
using Bsft.EventAggregator;
using Bsft.EventAggregator.DependencyInjection;
using Bsft.Localization.DependencyInjection;
using Bsft.Localization.EFCore;
using Bsft.Localization.EFCore.DependencyInjection;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Bsft.Localization.Tests.Support;

/// <summary>
/// The host the endpoint suites are served from: the localization services over an in-memory store,
/// with the bundle routes mapped.
/// </summary>
/// <remarks>
/// One host for every endpoint suite, because what those suites differ in is the requests they send
/// and not the application they send them to. The seed carries both shared and module-scoped keys,
/// so a suite about the module filter and a suite about entity tags can read the same bundle.
/// </remarks>
internal static class LocalizationHost
{
    /// <summary>Starts the host.</summary>
    /// <param name="tenantContext">The tenant the requests are served for.</param>
    /// <param name="databaseName">The store to use, or <see langword="null"/> for a fresh one.</param>
    /// <param name="maxFilterModules">The ceiling on distinct modules a request may name.</param>
    /// <returns>The started host.</returns>
    public static async Task<IHost> StartAsync(
        ITenantContext tenantContext,
        string? databaseName = null,
        int maxFilterModules = 32)
    {
        var database = databaseName ?? NewDatabaseName();

        return await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services.AddLogging();
                    services.AddRouting();
                    services.AddBsftEventAggregator();
                    services.AddBsftTranslations(seed => seed.ForLocale("en", locale => locale
                        .Add("greeting", "Hello")
                        .Add("common.save", "Save")
                        .Add("modules.Orders.displayName", "Orders")
                        .Add("modules.Inventory.displayName", "Inventory")));
                    services.AddBsftLocalization(options =>
                    {
                        options.DefaultLocale = "en";
                        options.FallbackLocale = "en";
                        options.MaxFilterModules = maxFilterModules;
                    });
                    services.AddDbContext<TestTranslationDbContext>(opt => opt.UseInMemoryDatabase(database));
                    services.AddBsftLocalizationEFCore<TestTranslationDbContext>();
                    services.AddSingleton(tenantContext);
                });
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.MapBsftLocalization());
                });
            })
            .StartAsync();
    }

    /// <summary>Names a store no other test is writing to.</summary>
    /// <returns>The database name.</returns>
    public static string NewDatabaseName() => "TranslationBundle_" + Guid.NewGuid();

    /// <summary>Writes one translation into the host's store.</summary>
    /// <param name="host">The host whose store to write to.</param>
    /// <param name="key">The translation key.</param>
    /// <param name="value">The translated text.</param>
    /// <param name="tenantId">The tenant the row belongs to, or <see langword="null"/> for a global row.</param>
    public static void AddTranslation(IHost host, string key, string value, string? tenantId = null)
    {
        using var scope = host.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TestTranslationDbContext>();

        dbContext.Translations.Add(new TranslationEntity
        {
            Locale = "en",
            Key = key,
            Value = value,
            TenantId = tenantId
        });

        dbContext.SaveChanges();
    }

    /// <summary>Reads the entity tag a response carries, asserting that it carries one.</summary>
    /// <param name="response">The response to read.</param>
    /// <returns>The entity tag, as it was served.</returns>
    public static string EntityTagOf(HttpResponseMessage response)
    {
        response.Headers.ETag.Should().NotBeNull();

        return response.Headers.ETag!.ToString();
    }
}
