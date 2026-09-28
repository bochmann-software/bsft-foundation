using Bsft.Core;
using Bsft.Mediator;

namespace Bsft.Testing.Tests.Support;

public record NoteRecorded(string Text) : INotification;
