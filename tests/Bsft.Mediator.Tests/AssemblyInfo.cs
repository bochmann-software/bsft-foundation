using Xunit;

// Telemetry tests attach process-global ActivityListener/MeterListener instances to the
// "Bsft.Mediator" source. Running test classes sequentially prevents cross-talk between a capturing
// test and a request/notification dispatched by another test in parallel.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
