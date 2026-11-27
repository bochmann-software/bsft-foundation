# Bsft.Hosting.StartGates

Start gates for hosted background loops: register a precondition once, and every loop registered as gated
starts only after it has opened. The loops themselves know nothing about gates.

## Install

```bash
dotnet add package Bsft.Hosting.StartGates
```

Requires `Bsft.Core` (the gate contract) and the generic host's abstractions, and nothing else.

## Register a gate

```csharp
internal sealed class SchemaGate : IHostedWorkStartGate
{
    public Task WaitAsync(CancellationToken cancellationToken) => /* completes once the schema is current */;
}

services.AddBsftHostedWorkStartGate<SchemaGate>();
// or, for a gate the host also registers under its own type:
services.AddBsftHostedWorkStartGate(sp => sp.GetRequiredService<SchemaGate>());
```

Several gates may be registered; a loop waits for all of them, in registration order. A gate that also
implements `IHostedWorkStartGateOverdueSignal` is what the opt-in `hosted-work-start-gates` health check of
`Bsft.Hosting` reports.

## Register a loop as gated

```csharp
services.AddBsftGatedHostedService<MyLoop>();
services.AddBsftGatedHostedService(sp => new MyLoop(sp.GetRequiredService<IWork>()));
```

The replacement for `AddHostedService` for a loop that must not run before the host's preconditions hold. The
framework's own dispatchers, sweepers and workers are registered this way. Which gates exist is decided when the
host starts, so the order of the two registrations does not matter.

- With no gate registered the host gets the loop itself: nothing differs from `AddHostedService`.
- With gates the host gets a wrapper whose start returns at once and which starts the loop after the gates. It is a
  `BackgroundService`, so `HostOptions.ServicesStartConcurrently` and `BackgroundServiceExceptionBehavior` apply
  to it as to any loop; a gate that throws faults it like a loop that fails.
- A stop while a gate is pending ends the wrapper cleanly and does not stop the loop, which never started.

Hosted services that react to events or run once at start (subscribers, validators, bootstrappers) stay on
`AddHostedService` and are never held back.

## Documentation

Topic page: [docs/hosting.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/hosting.md#start-gates-for-hosted-loops).
