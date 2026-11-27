using Microsoft.Extensions.Logging;

namespace Bsft.Hosting.BackgroundWork;

/// <summary>The log events of <see cref="PollingBackgroundServiceBase"/>, each with a stable event id and name.</summary>
internal static partial class PollingBackgroundServiceLog
{
    /// <summary>Reports that a polling service did not start its loop because it is disabled.</summary>
    /// <param name="logger">The service's logger.</param>
    /// <param name="service">The type name of the service.</param>
    [LoggerMessage(
        EventId = 7548,
        EventName = "PollingServiceDisabled",
        Level = LogLevel.Information,
        Message = "The polling service {Service} is disabled and does not run.")]
    internal static partial void Disabled(ILogger logger, string service);

    /// <summary>Reports a pass that failed; the loop waits the poll interval and tries again.</summary>
    /// <param name="logger">The service's logger.</param>
    /// <param name="failure">The failure of the pass.</param>
    /// <param name="service">The type name of the service.</param>
    [LoggerMessage(
        EventId = 7549,
        EventName = "PollingPassFailed",
        Level = LogLevel.Error,
        Message = "A pass of the polling service {Service} failed; it is tried again after the poll interval.")]
    internal static partial void PassFailed(ILogger logger, Exception failure, string service);

    /// <summary>Reports a pass cancelled by a token of its own while the service was not stopping.</summary>
    /// <param name="logger">The service's logger.</param>
    /// <param name="service">The type name of the service.</param>
    [LoggerMessage(
        EventId = 7550,
        EventName = "PollingPassCanceledWithoutStop",
        Level = LogLevel.Error,
        Message = "A pass of the polling service {Service} was canceled although the service was not asked to stop; it is tried again after the poll interval.")]
    internal static partial void PassCanceledWithoutStop(ILogger logger, string service);

    /// <summary>Reports that the loop is unwinding because the host is stopping.</summary>
    /// <param name="logger">The service's logger.</param>
    /// <param name="service">The type name of the service.</param>
    [LoggerMessage(
        EventId = 7551,
        EventName = "PollingServiceStopping",
        Level = LogLevel.Debug,
        Message = "The polling service {Service} is stopping.")]
    internal static partial void Stopping(ILogger logger, string service);
}
