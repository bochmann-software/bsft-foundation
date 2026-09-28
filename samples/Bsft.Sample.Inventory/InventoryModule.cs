using Bsft.Core;
using Bsft.Localization.DependencyInjection;
using Bsft.Modules;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bsft.Sample.Inventory;

/// <summary>
/// Inventory management module. Depends on the Orders module to ensure correct startup ordering.
/// Subscribes to stock reservation events and manages product stock levels.
/// </summary>
public class InventoryModule : IModule
{
    /// <inheritdoc />
    public string Name => "Inventory";

    /// <inheritdoc />
    public string Version => "1.0.0";

    /// <inheritdoc />
    public IReadOnlyList<string> DependsOn => ["Orders"];

    /// <inheritdoc />
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<StockReservationSubscriber>();

        // The product write commands share one validator. It is registered per command type
        // because the handlers ask for IValidator<TCommand> by constructor injection; the mediator
        // pipeline cannot reach a validator typed to a concrete command — see the remarks on
        // ProductCommandValidator.
        services.AddScoped<IValidator<CreateProductCommand>, ProductCommandValidator>();
        services.AddScoped<IValidator<UpdateProductCommand>, ProductCommandValidator>();

        services.AddBsftTranslations(seed => seed
            .ForLocale("en", en => en
                .Add("modules.Inventory.displayName", "Inventory")
                .Add("modules.Inventory.description", "Stock and inventory management"))
            .ForLocale("de", de => de
                .Add("modules.Inventory.displayName", "Lagerverwaltung")
                .Add("modules.Inventory.description", "Lager- und Bestandsverwaltung")));
    }

    /// <inheritdoc />
    public Task OnStartAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        var subscriber = services.GetRequiredService<StockReservationSubscriber>();
        subscriber.Subscribe();

        var logger = services.GetRequiredService<ILogger<InventoryModule>>();
        logger.LogInformation("InventoryModule started");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task OnStopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
