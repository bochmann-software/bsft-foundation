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
}
