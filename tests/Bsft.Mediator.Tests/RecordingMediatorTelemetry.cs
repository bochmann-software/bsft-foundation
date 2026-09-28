using Bsft.Mediator.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.Mediator.Tests;

/// <summary>A host's own telemetry emitter, recording what the mediator reports to it.</summary>
public sealed class RecordingMediatorTelemetry : IMediatorTelemetry
{
    public List<string> Calls { get; } = [];

    public IRequestTelemetryScope BeginRequest(string requestName, string requestType, string? correlationId)
    {
        Calls.Add($"BeginRequest:{requestName}");
        return new Scope(Calls);
    }

    public IRequestTelemetryScope BeginPublish(string notificationName, int handlerCount)
    {
        Calls.Add($"BeginPublish:{notificationName}");
        return new Scope(Calls);
    }

    private sealed class Scope(List<string> calls) : IRequestTelemetryScope
    {
        public void Complete() => calls.Add("Complete");

        public void Fail(Exception exception) => calls.Add("Fail");

        public void Dispose() => calls.Add("Dispose");
    }
}
