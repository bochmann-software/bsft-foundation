using Xunit;

// Telemetry tests attach process-global ActivityListener/MeterListener instances to the "Bsft.Saga"
// source. Running test classes sequentially prevents cross-talk between a capturing test and a saga
// dispatched by another test in parallel.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
