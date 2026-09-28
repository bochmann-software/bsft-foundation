using Xunit;

// Telemetry tests attach process-global ActivityListener/MeterListener instances to the
// "Bsft.Messaging" source. Running test classes sequentially keeps the process-global source and
// listener lists still while a listener is being registered: a parallel class disposing a service
// provider disposes its ActivitySource, and that removal can make the registration walk skip the
// source under test.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
