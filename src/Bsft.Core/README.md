# Bsft.Core

The primitives every other `Bsft.*` package builds on: the exception hierarchy the HTTP layer maps to
status codes, the validation contracts, the `Unit` type commands return when they produce no result,
the poll loop background services share, the `IRetryAfter` seam a failure asks to be tried later
through, the two unit-of-work contracts the framework states its persistence boundaries in, and the
closure calculator every closure table in the framework is maintained with.

## Install

```bash
dotnet add package Bsft.Core
```

No further Bsft packages; it depends on the BCL alone and is pulled transitively by every other
package in the framework.

## Register

Nothing to register. `Bsft.Core` contributes no services and therefore has no
`AddBsftCore` — it is a package of contracts, exceptions and value types that other packages
register implementations for. `IPollLoop`, `IValidator<T>` and `IStartupValidator` are registered by
whichever package supplies the implementation. `IClosureCalculator` is registered by each package
that keeps a closure table, with `TryAddSingleton<IClosureCalculator, ClosureCalculator>()`, so the
first registration wins.

## Configure

None. The package binds no configuration section and exposes no options type.

| Option | Default | Effect |
| --- | --- | --- |
| — | — | — |

Validation: none.

## Extend

`IPollLoop` (runs a poll pass until the service stops; `PollLoop` is the implementation and takes an
interval and a pass delegate), `IPollReporter` (phrases the three things the loop has to say, so a
service logs in its own vocabulary rather than the loop's), `IValidator<T>` (validates one instance
and answers a `ValidationResult`), `IStartupValidator` (checks configuration once at startup and
answers a `StartupValidationResult`), `IRetryAfter` (an exception saying when the work it failed should
be attempted again, so the code that learned of a wait does not sleep in a pipeline it shares; implement
it on an exception type you already have, or throw the ready-made `RetryAfterException`).
`IUnitOfWork` (commits whatever the scope staged, which is how a package that writes through the
host's storage closes its own unit of work — a background sweep, for one, has no change of its own to
ride along with) and `ITransactionalUnitOfWork` (runs several writes as one unit, handed over as a
delegate so a retrying provider can re-run the whole unit rather than resume it half-committed).
Neither names a `DbContext`; `Bsft.Data` ships `DbContextUnitOfWork<TContext>` behind both.
`IClosureCalculator` (computes the ancestor and descendant rows of a tree given as `(Id, ParentId)`
pairs, generic in the row: the caller passes a `(ancestorId, descendantId, depth)` factory for its own
closure entity; `ClosureCalculator` is the implementation).
`BsftException` is the one type here designed for inheritance:
derive an application exception from it and `BsftExceptionMiddleware` will map it and carry its
error code. Every exception the framework itself ships below that base is sealed.

## Documentation

Topic page: [docs/core.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/core.md).
Conventions: [docs/package-conventions.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/package-conventions.md).
