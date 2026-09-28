# Bsft.Testing

The test doubles and harnesses the framework's own suites use: a web application factory that boots an
application with a resolved tenant and a fixed correlation id already in place, a fake OIDC authority,
a fake Web Push service, a controllable clock, and in-process harnesses for the mediator and for sagas.

## Install

```bash
dotnet add package Bsft.Testing
```

Requires `Bsft.Correlation`, `Bsft.Data`, `Bsft.EventAggregator.Saga` and `Bsft.Mediator`, plus
`Microsoft.AspNetCore.Mvc.Testing` and the EF Core in-memory provider. It is marked a development
dependency, so it does not flow transitively into anything a consumer publishes: its doubles replace
production registrations wholesale, and that must never reach an application's own package graph.

## Register

```csharp
services.AddBsftTestContexts();                       // tenant and correlation doubles
services.AddBsftTestDatabase<AppDbContext>("orders"); // repoint a context at an in-memory store
```

Every method replaces rather than adds, so all of them are safe to call on a container an application
has already fully configured — which is what `BsftWebApplicationFactory<TEntryPoint>` does. There is
no single `AddBsftTesting`: a test picks the doubles it wants, and there is no sensible default set
for a package whose whole purpose is to be selective.

## Configure

None. The package binds no configuration section and exposes no options type; the doubles are handed
their values in the constructor (`new TestTenantContext("acme")`) rather than configured.

| Option | Default | Effect |
| --- | --- | --- |
| — | — | — |

Validation: `AddBsftTestDatabase` rejects a blank database name, and every method rejects a null
argument.

## Extend

`BsftWebApplicationFactory<TEntryPoint>` is designed for inheritance and is the intended entry point:
derive from it, override `ConfigureTestServices`, and share the derived factory across a project's
tests. A factory that brings doubles of its own kind overrides `ConfigureTestDoubles` and calls the
base first. `IFakeOidcAuthority` (issues authorization codes, tokens and back-channel logout tokens
for a federated-login test), `IFakePushService` (receives and decrypts Web Push deliveries),
`ITestEnvironmentPolicy` (decides whether the host counts as a test host, for a test-only scheme that
must refuse to run anywhere else) and `ITestClock` (a `TimeProvider` a test advances) are the
replaceable seams. `IMediatorTestHarness` and `ISagaTestHarness` are built through their builders
rather than resolved, so a test composes exactly the handlers it wants.

## Documentation

Topic page: [docs/testing.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/testing.md).
Conventions: [docs/package-conventions.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/package-conventions.md).
