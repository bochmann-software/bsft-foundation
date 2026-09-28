using Bsft.Core;
using Bsft.Mediator;

namespace Bsft.Testing.Tests.Support;

public class RecordNoteHandler : ICommandHandler<RecordNote>
{
    private readonly ITrace _trace;

    public RecordNoteHandler(ITrace trace) => _trace = trace;

    public Task HandleAsync(RecordNote command, CancellationToken ct = default)
    {
        _trace.Add($"handled:{command.Text}");
        return Task.CompletedTask;
    }
}
