using Bsft.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Bsft.Hosting.StartupValidation;

/// <summary>
/// Hosted service that runs all registered <see cref="IStartupValidator"/> implementations
/// before the application begins accepting requests. On failure, logs a structured error and throws.
/// </summary>
/// <remarks>
/// Running a validator is guarded as one step: whatever a validator's <see
/// cref="IStartupValidator.ValidateAsync"/> does — including resolving and constructing its own
/// scoped dependencies, which several validators do lazily inside the call rather than through their
/// own constructor — becomes a <see cref="StartupValidationResult.Failure(string[])"/> naming the
/// validator's type if it throws, rather than an unhandled exception crashing the host outright. A
/// validator is host-authored code this class cannot know the failure modes of, and one validator's
/// bug must not prevent every other registered validator from still being run and reported.
/// </remarks>
public sealed class StartupValidationHostedService : IHostedService
{
    private readonly IEnumerable<IStartupValidator> _validators;
    private readonly ILogger<StartupValidationHostedService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="StartupValidationHostedService"/> class.
    /// </summary>
    /// <param name="validators">The collection of startup validators to execute.</param>
    /// <param name="logger">The logger instance.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public StartupValidationHostedService(
        IEnumerable<IStartupValidator> validators,
        ILogger<StartupValidationHostedService> logger)
    {
        _validators = validators ?? throw new ArgumentNullException(nameof(validators));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Runs all startup validators. Throws an <see cref="InvalidOperationException"/> if any validation fails.
    /// Every warning a validator reports is logged at warning level, whether or not its result is valid.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var validators = _validators.ToList();

        if (validators.Count == 0)
        {
            _logger.LogInformation("Startup validation skipped: no validators registered.");
            return;
        }

        var allErrors = new List<string>();
        var passed = new List<string>();

        foreach (var validator in validators)
        {
            var validatorName = validator.GetType().Name;
            var result = await RunOneAsync(validator, validatorName, cancellationToken).ConfigureAwait(false);

            // Warnings are reported regardless of the outcome and through this one channel, so a
            // validator never needs a logger of its own to be heard.
            foreach (var warning in result.Warnings)
            {
                _logger.LogWarning("{Validator}: {Warning}", validatorName, warning);
            }

            if (result.IsValid)
            {
                var msg = result.Message ?? "Passed.";
                _logger.LogInformation("{Validator}: {Message}", validatorName, msg);
                passed.Add(validatorName);
                continue;
            }

            _logger.LogError(
                "Startup validation failed in {Validator}: {Errors}",
                validatorName,
                string.Join("; ", result.Errors));

            allErrors.AddRange(result.Errors);
        }

        if (allErrors.Count > 0)
        {
            throw new InvalidOperationException(
                $"Startup validation failed with {allErrors.Count} error(s): {string.Join("; ", allErrors)}");
        }

        _logger.LogInformation(
            "Startup validation passed. Validators: {Validators}",
            string.Join(", ", passed));
    }

    /// <summary>
    /// No-op. Startup validation does not require cleanup.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Runs one validator, turning anything it throws into a named failure instead of a crash.</summary>
    private async Task<StartupValidationResult> RunOneAsync(
        IStartupValidator validator, string validatorName, CancellationToken cancellationToken)
    {
        try
        {
            return await validator.ValidateAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "Startup validator {Validator} threw instead of returning a result.", validatorName);
            return StartupValidationResult.Failure(
                $"{validatorName} threw {exception.GetType().Name}: {exception.Message}");
        }
    }
}
