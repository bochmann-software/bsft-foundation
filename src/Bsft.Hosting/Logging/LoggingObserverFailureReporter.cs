using Bsft.Core.Observers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bsft.Hosting.Logging;

/// <summary>Logs the observer failures an <see cref="IObserverGuard"/> contained.</summary>
internal sealed class LoggingObserverFailureReporter : IObserverFailureReporter
{
    private readonly ILogger<LoggingObserverFailureReporter> _logger;
    private readonly bool _includeExceptionDetails;

    /// <summary>Initializes a new instance of the <see cref="LoggingObserverFailureReporter"/> class.</summary>
    /// <param name="logger">Where a failure is logged.</param>
    /// <param name="options">Decides whether the exception itself is logged.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    public LoggingObserverFailureReporter(ILogger<LoggingObserverFailureReporter> logger, IOptions<ObserverFailureLoggingOptions> options)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _includeExceptionDetails = (options ?? throw new ArgumentNullException(nameof(options))).Value.IncludeExceptionDetails;
    }

    /// <inheritdoc />
    public void ReportObserverFailed(object observer, Exception failure)
    {
        ArgumentNullException.ThrowIfNull(observer);
        ArgumentNullException.ThrowIfNull(failure);

        // The exception message is data of the observed operation; by default only its type is logged.
        if (_includeExceptionDetails)
            _logger.LogWarning(failure, "An observer ({Observer}) failed with {FailureType}; the observed operation carries on.", observer.GetType().Name, failure.GetType().Name);
        else
            _logger.LogWarning("An observer ({Observer}) failed with {FailureType}; the observed operation carries on.", observer.GetType().Name, failure.GetType().Name);
    }
}
