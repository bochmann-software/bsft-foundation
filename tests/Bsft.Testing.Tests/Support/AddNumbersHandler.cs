using Bsft.Core;
using Bsft.Mediator;

namespace Bsft.Testing.Tests.Support;

public class AddNumbersHandler : ICommandHandler<AddNumbers, int>, IAuditedHandler
{
    public Task<int> HandleAsync(AddNumbers command, CancellationToken ct = default) =>
        Task.FromResult(command.Left + command.Right);
}
