using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Bsft.Mediator.Tests.Support;

internal sealed record MetricMeasurement(string Instrument, double Value, string? Outcome);
