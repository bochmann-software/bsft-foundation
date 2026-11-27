# Bsft.Outbox

The drain kernel every transactional outbox in a host shares: lease a batch, apply each row's side
effect, mark what succeeded, hold back what failed and retire what keeps failing.

## Install

```bash
dotnet add package Bsft.Outbox
```

Requires `Bsft.Outbox.Abstractions` (pulled transitively) and `Bsft.Core`. A store implementation
comes from `Bsft.Outbox.EFCore` or from the host.

## Register

```csharp
services.AddBsftOutbox<MyPayload>("my-outbox", options =>
{
    options.BatchSize = 50;
});
```

The store and the side effect stay the owning package's to register: which rows this outbox holds and
what they mean are the two things the kernel cannot guess. Run the drainer with
`Bsft.Core.Polling.PollLoop` from a hosted service of your own, drain on demand through
`IOutboxDrainer<TPayload>`, or let the optional hosted worker below do the loop.

## Run it: the hosted drain worker

```csharp
services.AddBsftOutboxDrainWorker<MyPayload>("my-outbox", options => options.BatchSize = 50);
```

It registers the outbox exactly as `AddBsftOutbox` does and adds a hosted loop on top. A host that
drains from its own hosted service keeps calling `AddBsftOutbox` and is unaffected. The worker:

- **Shutdown grace.** The host's stop only decides whether another row is started: after it no new row
  begins (the rest of the leased batch stays pending until its lease expires: `LeaseDuration`, default one minute, also when the stop lands during the lease call; use a short one for frequent rolling deploys). The row in flight runs
  on a token of its own, cancelled `ShutdownGrace` after the stop, so a row whose side effect is
  running (a mail being sent) still reaches its processed mark instead of being applied again by the
  next instance. The drainer offers the same split to a loop of your own as
  `DrainOnceAsync(mayStartRow, cancellationToken)`.
- **Drain to empty, then wait.** A pass repeats while it applies rows; then the worker waits for the
  poll interval, or for an `IOutboxWakeSignal` when one is registered, whichever comes first.
- **Failure.** A failing pass is logged and retried after the poll interval; only the stopping token
  ends the worker.
- **Seam.** An optional `IOutboxDrainPass<TPayload>`, registered with `AddBsftOutboxDrainPass<TPayload, TPass>()`,
  wraps each round in a scope of its own: do work before the batch is leased or after it was applied, or
  return without calling the `drainOnce` delegate to lease nothing. Without one the worker is unchanged.

An `IOutboxWakeSignal` is registered either as a keyed service under the outbox's name (one signal per
outbox) or without a key (a host with one outbox). The signal is a hint: the poll interval stays the
guarantee. `Bsft.Outbox.Postgres` ships one on PostgreSQL `LISTEN`.

Registering the worker for the same name twice registers it once. A name maps one to one to a payload
type: the drain pass exists once per payload type, so a second name for the same type throws. The `IConfiguration` overload binds
`Bsft:Outbox:Instances:<name>` and `Bsft:Outbox:DrainWorkers:<name>`.

## Configure

Every outbox is a named options instance, bound from `Bsft:Outbox:Instances:<name>` when the
registration is handed an `IConfiguration`:

```csharp
services.AddBsftOutbox<MyPayload>("my-outbox", builder.Configuration);
```

```json
{ "Bsft": { "Outbox": { "Instances": { "my-outbox": { "BatchSize": 50 } } } } }
```

The action overload still applies, and when both are used the action runs after the section.

| Option | Default | Effect |
| --- | --- | --- |
| `BsftOutboxOptions.PollInterval` | `00:00:05` | How long a loop waits after a pass that found nothing. Must be positive. |
| `BsftOutboxOptions.BatchSize` | `100` | The most rows one pass claims. Must be at least 1. |
| `BsftOutboxOptions.LeaseDuration` | `00:01:00` | How long a claim holds. Must be positive, and has to cover a whole batch. |
| `BsftOutboxOptions.MaxDeliveryAttempts` | `10` | How often a row may fail before it is retired. Must be at least 1. |
| `BsftOutboxOptions.RetryBackoff` | `00:00:05` | The first interval a failed row waits; doubles per attempt. Must not be negative. |
| `BsftOutboxOptions.MaxRetryDelay` | `00:05:00` | The ceiling of the schedule and the clamp on what a failure may ask for. Must be positive. |
| `BsftOutboxDrainWorkerOptions.ShutdownGrace` | `00:00:05` | How long a pass in flight may continue after the host stops, before it is cancelled. Zero cancels with the stop. Must not be negative and shorter than `HostOptions.ShutdownTimeout`, or the host is refused at start. Only read by the hosted worker. |

Validation (the worker's interval and batch are the two above, `PollInterval` and `BatchSize`): the host is refused at start, naming the outbox, the option and the value it was given.
The drainer's constructor asks the same rules again, for a caller that builds one without the
options pipeline.

## Extend

`IOutboxSideEffect<TPayload>` (what a row means, applied before the row is marked; there is no
default — the owning package writes one). `IOutboxStore<TPayload>` (where the rows live;
`Bsft.Outbox.EFCore` implements the lease protocol once). `IOutboxRetrySchedule` (how long a failed
row waits; the shipped one is exponential with jitter and honours `IRetryAfter` as a floor).

## Documentation

Topic page: [docs/outbox.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/outbox.md).
Conventions: [docs/package-conventions.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/package-conventions.md).
