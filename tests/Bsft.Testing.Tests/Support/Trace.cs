using Bsft.Core;
using Bsft.Mediator;

namespace Bsft.Testing.Tests.Support;

public sealed class Trace : ITrace
{
    private readonly List<string> _entries = [];

    public IReadOnlyList<string> Entries => _entries;

    public void Add(string entry) => _entries.Add(entry);
}
