namespace Bsft.Idempotency.EFCore.Tests.Support;

/// <summary>Cuts one table's CREATE TABLE statement out of an EF Core create script.</summary>
internal static class CreateScript
{
    public static string Table(string script, string table)
    {
        var start = script.IndexOf($"CREATE TABLE \"{table}\" (", StringComparison.Ordinal);
        if (start < 0)
            throw new InvalidOperationException($"The create script has no table {table}.");
        var end = script.IndexOf(");", start, StringComparison.Ordinal);
        return script[start..(end + 2)];
    }
}
