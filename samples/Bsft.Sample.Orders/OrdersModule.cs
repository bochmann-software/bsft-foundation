using Bsft.Localization.DependencyInjection;
using Bsft.Modules;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bsft.Sample.Orders;

/// <summary>
/// Order management module. Registers translations for the orders domain
/// and logs module lifecycle events.
/// </summary>
public class OrdersModule : IModule
{
    /// <inheritdoc />
    public string Name => "Orders";

    /// <inheritdoc />
    public string Version => "1.0.0";

    /// <inheritdoc />
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddBsftTranslations(seed => seed
            .ForLocale("en", en => en
                .Add("modules.Orders.displayName", "Orders")
                .Add("modules.Orders.description", "Order management module")
                .Add("orders.status.pending", "Pending")
                .Add("orders.status.confirmed", "Confirmed")
                .Add("orders.status.canceled", "Canceled"))
            .ForLocale("de", de => de
                .Add("modules.Orders.displayName", "Bestellungen")
                .Add("modules.Orders.description", "Bestellverwaltung")
                .Add("orders.status.pending", "Ausstehend")
                .Add("orders.status.confirmed", "Bestaetigt")
                .Add("orders.status.canceled", "Storniert")));
    }

    /// <inheritdoc />
    public Task OnStartAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        var logger = services.GetRequiredService<ILogger<OrdersModule>>();
        logger.LogInformation("OrdersModule started");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task OnStopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
