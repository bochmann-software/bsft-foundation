namespace Bsft.Messaging.EFCore.Tests.Fakes;

public sealed class CorrelationLog
{
    private readonly List<string?> _entries = [];

    public IReadOnlyList<string?> Entries => _entries;

    public void Add(string? entry) => _entries.Add(entry);
}
