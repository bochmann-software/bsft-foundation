namespace Bsft.Data.Postgres.Tests.Support;

/// <summary>Tracks execution details for parity tests: handler runs and context instances.</summary>
public sealed class ParityTestLog
{
    public string ConnectionString { get; set; } = string.Empty;

    public int OuterHandlerRuns { get; set; }

    public int NestedHandlerRuns { get; set; }

    public List<int> OuterContextInstances { get; set; } = [];
}
