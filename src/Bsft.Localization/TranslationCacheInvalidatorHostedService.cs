using Microsoft.Extensions.Hosting;

namespace Bsft.Localization;

/// <summary>
/// A hosted service that ensures the <see cref="TranslationCacheInvalidator"/>
/// is instantiated at application startup and disposed on shutdown.
/// </summary>
public sealed class TranslationCacheInvalidatorHostedService : IHostedService
{
    private readonly TranslationCacheInvalidator _invalidator;

    /// <summary>
    /// Initializes a new instance of the <see cref="TranslationCacheInvalidatorHostedService"/> class.
    /// </summary>
    /// <param name="invalidator">The cache invalidator to manage.</param>
    public TranslationCacheInvalidatorHostedService(TranslationCacheInvalidator invalidator)
    {
        _invalidator = invalidator ?? throw new ArgumentNullException(nameof(invalidator));
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _invalidator.Dispose();
        return Task.CompletedTask;
    }
}
