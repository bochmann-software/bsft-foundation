using Bsft.Correlation;
using Microsoft.Extensions.Logging.Console;

namespace Bsft.Hosting.Logging;

/// <summary>
/// Options for the <see cref="BsftConsoleFormatter"/>.
/// </summary>
/// <remarks>
/// It carries no <c>SectionName</c> on purpose. A console formatter's options are bound by the logging
/// infrastructure itself, from the host's <c>Logging:Console:FormatterOptions</c> section, and
/// <c>AddBsftHostLogging</c> configures the one member it cares about. A constant here would name a
/// section nothing reads.
/// </remarks>
public sealed class BsftConsoleFormatterOptions : ConsoleFormatterOptions
{
    /// <summary>
    /// Gets or sets the scope keys the console line shows, with the same meaning as the file logger's
    /// <c>PersistedScopeKeys</c>: the correlation id in its bracketed slot, every other key in a
    /// <c>{Key=value;Key=value}</c> block after it, the innermost value of each. <c>AddBsftHostLogging</c> copies
    /// the file logger's list here. Defaults to the correlation id only.
    /// </summary>
    public IList<string> PersistedScopeKeys { get; set; } = new List<string> { LogScopeKeys.CorrelationId };
}
