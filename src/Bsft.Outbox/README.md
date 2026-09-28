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
`Bsft.Core.Polling.PollLoop` from a hosted service of your own, or drain on demand through
`IOutboxDrainer<TPayload>`.

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

Validation: the host is refused at start, naming the outbox, the option and the value it was given.
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
