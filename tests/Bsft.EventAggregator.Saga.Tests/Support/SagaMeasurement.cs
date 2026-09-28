namespace Bsft.EventAggregator.Saga.Tests.Support;

/// <summary>One metric measurement captured from the Bsft.Saga meter.</summary>
internal sealed record SagaMeasurement(string Instrument, double Value, string? Outcome, string? SagaType);
