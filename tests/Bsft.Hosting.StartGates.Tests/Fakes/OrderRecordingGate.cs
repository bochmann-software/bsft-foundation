using Bsft.Core.Hosting;

namespace Bsft.Hosting.StartGates.Tests.Fakes;

/// <summary>A gate that is already open and appends its name to a shared list when it is awaited.</summary>
internal sealed class OrderRecordingGate : IHostedWorkStartGate
{
    private readonly string _name;
    private readonly List<string> _awaited;

    /// <summary>Initializes a new instance of the <see cref="OrderRecordingGate"/> class.</summary>
    /// <param name="name">What the gate appends.</param>
    /// <param name="awaited">The list the gates of one test share.</param>
    public OrderRecordingGate(string name, List<string> awaited)
    {
        _name = name;
        _awaited = awaited;
    }

    /// <inheritdoc />
    public Task WaitAsync(CancellationToken cancellationToken)
    {
        _awaited.Add(_name);

        return Task.CompletedTask;
    }
}
