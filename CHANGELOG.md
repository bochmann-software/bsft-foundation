# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project uses a single `BsftFoundationVersion` across all packages.

## [Unreleased]

## [2.1.0]

The first release of Bsft Foundation on nuget.org and in a repository of its own. The packages
continue the version line of the framework they were cut from, so their history starts at 2.0.0,
which was not published here; see [docs/migration-2.1.0.md](docs/migration-2.1.0.md).

### Added

- **Foundation packages now also target net10.0.** Every package ships `net8.0` and `net10.0`
  assets. On `net10.0` the packages depend on the 10.0.x line of Microsoft.Extensions.* and
  ASP.NET Core; the `net8.0` dependencies are unchanged. The tests run, and the coverage gate holds
  each framework at 100 %, on both.

### Changed

- **The authentication test doubles left `Bsft.Testing` (breaking).** `Bsft.Testing` no longer
  references an authentication package: `TestUserContext`, `TestPermissionSource`,
  `TestPrincipalProvider`, `TestAuthenticationHandler`, `AddBsftTestUser`, `AddBsftTestPermissions`
  and `AddBsftTestAuthentication` are no longer part of it, and `AddBsftTestContexts` and
  `BsftWebApplicationFactory` install the test tenant and correlation only. A test that relied on
  the test user or the `BsftTest` authentication scheme registers its own.

## 2.0.0

The last version before this repository, not published on nuget.org. It is listed because 2.1.0
builds on it: a major release of the packages, see [docs/migration-2.0.0.md](docs/migration-2.0.0.md).

### Changed

- **Names follow [docs/naming.md](docs/naming.md) (breaking).** Two-letter acronyms in capitals
  (`Bsft.*.EFCore`, `Bsft.Messaging.RabbitMQ`), no abbreviations (`Org` → `Organization`, `Config`,
  `Dedup`, `Seq`, `Ref`, `Dest`, `Spec` written out), American spelling (`Canceled`, `Behavior`),
  one suffix per role (`*Validator`, `*Sweeper`, `*Reader`, `*Event`, `*QueryParameters`),
  `Get`/`Find`/`List` by what a method returns, and canonical parameter names; behavior is
  unchanged. Wire and persisted names follow their C# names with no shim: configuration keys
  (`…:EFCore`, `Bsft:RabbitMQ`), the sort query key (`Sort[i].Direction`), the mediator behavior
  name `OrganizationAuthorization`, the metric `bsft.saga.timeout.canceled` and the log property
  `PurgedCount`. The migration guide lists each with its step.
- **`RequiredPermissionsMetadata` consolidated (breaking).** The per-package copies of
  `RequiredPermissionsMetadata` are one public record in `Bsft.Http`.
- **Naming checks.** `verify-structure.mjs` checks the `name-*` rules (acronyms, abbreviations,
  spelling, type suffixes, options validators, hosted services, duplicate type names, namespace-type
  clashes, parameter names, test class names, and the written-out contracts: endpoint names, routes,
  table names, bound keys, log properties and contract constants), each with a firing and a
  non-firing fixture.
- **Namespaces follow features, not roles (breaking,
  [ADR-0013](docs/design/adr-0013-namespaces-by-feature.md)).** Every package drops its role
  namespaces (`Services`, `Models`, `Commands`, `Queries`, `Endpoints`, `Stores`, `Exceptions`,
  `Options`, `Extensions` and the folder namespaces called `Abstractions`); types live in the
  namespace of their feature or in the package root, and each namespace equals its folder, which the
  build enforces with IDE0130.
- **`AddBsft*`, `MapBsft*` and `UseBsft*` live in `<Package>.DependencyInjection` (breaking).** A host
  imports one `using <Package>.DependencyInjection;` per package it wires.
- **Structure check.** `.github/verify-structure.mjs` checks the package layout of ADR-0013 and
  [docs/package-conventions.md](docs/package-conventions.md) repo-wide: namespace equals folder, no
  role folders, nesting, one-file namespaces, small packages flat, the package root as a dependency
  sink, the direction of `DependencyInjection`, no namespace cycles, `.Abstractions` folders with a
  counterpart, test projects mirroring their package, `docs/packages.md` rows, solution membership
  and one type per file. Each rule has a fixture that breaks it.
- **Coverage measures async methods, iterators and lambdas.** Coverlet no longer excludes
  `CompilerGeneratedAttribute`, which had dropped every state machine and closure from the
  measurement; `tests/check-state-machine-coverage.sh` fails when a package with async code reports
  none. Still at 100 % line coverage with no new exclusion.
- **Conventions rules.** `bsft-single-service-collection-extensions` is replaced by
  `bsft-di-entry-point-namespace`, which requires the DI entry point classes to live in a namespace
  ending in `.DependencyInjection`; a package may now have several of them, one per feature it wires.
- **All `.Abstractions` packages declare the namespace of the package they abstract (breaking).**
  `Bsft.Messaging.Abstractions` declares `namespace Bsft.Messaging` instead of
  `Bsft.Messaging.Abstractions`, and likewise `Bsft.Correlation.Abstractions`,
  `Bsft.Mediator.Abstractions` and `Bsft.Outbox.Abstractions`; package and assembly names are
  unchanged, `.Contracts` packages are unchanged. The conventions rule
  `bsft-namespace-follows-assembly` enforces the layout.
- **Saga: `SagaStepConfigurator` takes its builder as a type argument (breaking).**
  `SagaStepConfigurator<TState, TEvent>` is now `SagaStepConfigurator<TState, TEvent, TBuilder>`, and
  the definition tracking of `ISagaBuilderFactory` moved to `ISagaDefinitionRegistry`, which the
  factory extends. The builder and the configurator no longer depend on each other. Fluent flow
  definitions compile unchanged; only code naming the configurator type spells the third argument.

[Unreleased]: https://github.com/bochmann-software/bsft-foundation/compare/v2.1.0...HEAD
[2.1.0]: https://github.com/bochmann-software/bsft-foundation/releases/tag/v2.1.0
