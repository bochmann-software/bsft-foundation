using Bsft.Core;
using Bsft.Mediator;

namespace Bsft.Testing.Tests.Support;

public interface ITrace
{
    IReadOnlyList<string> Entries { get; }

    void Add(string entry);
}
