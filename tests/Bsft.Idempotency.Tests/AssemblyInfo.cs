using Xunit;

// The telemetry test attaches a process-global MeterListener to the "Bsft.Idempotency" meter, and
// several acceptance tests boot a TestServer of their own. Running the classes sequentially keeps a
// capturing test from seeing another test's measurements.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
