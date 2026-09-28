using Bsft.Core;
using Bsft.Mediator;

namespace Bsft.Testing.Tests.Support;

public class NoteRecordedHandler : INotificationHandler<NoteRecorded>
{
    private readonly ITrace _trace;

    public NoteRecordedHandler(ITrace trace) => _trace = trace;

    public Task HandleAsync(NoteRecorded notification, CancellationToken ct = default)
    {
        _trace.Add($"notified:{notification.Text}");
        return Task.CompletedTask;
    }
}
