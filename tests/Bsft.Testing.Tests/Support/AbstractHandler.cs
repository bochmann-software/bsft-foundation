using Bsft.Core;
using Bsft.Mediator;

namespace Bsft.Testing.Tests.Support;

public abstract class AbstractHandler : ICommandHandler<AddNumbers, int>
{
    public abstract Task<int> HandleAsync(AddNumbers command, CancellationToken ct = default);
}
