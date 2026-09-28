namespace Bsft.Messaging.Tests.Fakes;

public sealed class DeliveryLog
{
    private readonly List<string> _entries = [];

    public IReadOnlyList<string> Entries => _entries;

    public void Add(string entry) => _entries.Add(entry);
}
