using Bsft.Data.Diagnostics;

namespace Bsft.Data.Sqlite.Tests.Support;

public sealed class NullTelemetry : IDataTelemetry
{
    public void SearchCompleted(string group, double seconds, int resultCount)
    {
    }
}
