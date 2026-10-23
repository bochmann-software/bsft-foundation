# Package Conventions

How a `Bsft.*` package is laid out, named and wired. These rules are binding for
every package under `src/`, `samples/` and `tests/`; a change that violates them
does not pass review. They complement [architecture-rules.md](architecture-rules.md)
(what the framework assumes at runtime) and
[crud-conventions.md](crud-conventions.md) (how a resource looks on the wire).

The rules were derived from a survey of the 1.3.5 tree (2026-08-16), where 84 %
of packages were flat, DI entry points came in five shapes and the same role
was called `Store`, `Service`, `Provider` or `Factory` depending on the package.
The rules were applied to the whole tree in 1.3.5, including the public
renames they imply; the migration page lists them. 2.0.0 replaced the role
folders of that layout with feature folders and made the namespace equal the
folder without exceptions ([ADR-0013](design/adr-0013-namespaces-by-feature.md));
sections 2 to 4 describe that layout.

## 1. Package families

A feature ships as a **family** of packages sharing a prefix:

| Suffix | Contains | Depends on |
| --- | --- | --- |
| `Bsft.<Feature>.Abstractions` | The contract a consumer or a second implementation needs without the implementation: interfaces, contract records and enums, exceptions, defaults constants. **No** service registration, **no** options types, **no** live implementations (`DelegatingHandler`, middleware, stores). | Other `.Abstractions` only, plus BCL, `Microsoft.Extensions.*.Abstractions`, and `Bsft.Core`. `Bsft.Core` is on the list because it has no package references at all — it is exceptions, validation, the `Unit` type and pure BCL functions such as the closure calculator — so referencing it widens the closure by nothing, which is what the closure rule is protecting. |
| `Bsft.<Feature>` | The implementation and its DI entry point. | Its own `.Abstractions`, other Bsft packages, third-party libraries. |
| `Bsft.<Feature>.EFCore` | Persistence: `I<Feature>DbContext`, entities, entity configurations, EF-backed stores. | `Bsft.<Feature>` (or its `.Abstractions`), `Microsoft.EntityFrameworkCore`. |
| `Bsft.<Feature>.<Other>` | An adapter to another family (`Bsft.Auth.ApiKeys.Notifications`, `Bsft.Auditing.Idp`, `Bsft.Storage.S3`), or a live implementation of the family that a consumer must be able to take without the rest of it (`Bsft.Correlation.Http`). | Both families it bridges; a satellite of the second kind takes its own family's `.Abstractions` and whatever its one job needs. |

A package references the **family** it consumes and reaches that family's
contract package through it, rather than declaring its own closure:
`Bsft.Storage.S3` references `Bsft.Storage` and uses `Bsft.Storage.Abstractions`
through it, `Bsft.Data.Postgres` reaches `Bsft.Data.Contracts` the same way. The
column above is what a package is allowed to reference, not a list it has to
restate. Declare a package explicitly when it is used **without** the family in
front of it, which is what an `.Abstractions`-only consumer does.

The second kind of satellite is the answer to a question an `.Abstractions`
package cannot settle: what to do with a live implementation a dependency-light
consumer needs anyway. `Bsft.Correlation.Http` is the case — the outbound
`DelegatingHandler` may not sit in the contract package, and putting it in
`Bsft.Correlation` puts the ASP.NET Core middleware's framework reference in
front of every client that wants to attach a header. Split it out only when the
consumer that needs it cannot take the main package at all; a satellite for
tidiness alone is a fourth package for nothing. The main package references the
satellite, so its `AddBsft<Feature>` keeps registering what it always did.

**`Bsft.<X>.Mediator` is the behavior package for concern `X`.** It contains
that concern's `IPipelineBehavior<,>` implementation, its opt-in marker, and its
`AddBsft<X>Mediator` registration, and nothing else. **A concern package never
contains a pipeline behavior.** `Bsft.Auditing.Mediator`, `Bsft.Auth.Mediator`,
`Bsft.Caching.Mediator`, `Bsft.Organization.Mediator` and
`Bsft.Resilience.Mediator` are the five, and the name is now uniform enough to
state as a rule rather than as five coincidences.

The rule exists for the closure: a behavior left inside its concern puts
`Bsft.Mediator` in front of every consumer of that concern, whether or not the
consumer dispatches a single request — and `Bsft.Auth` is referenced by most of
the framework. It also makes "is the behavior on" a question about a package
reference rather than about a call. A marker the concern shares with something
that is not the mediator stays behind: `Bsft.Auth`'s `[TenantAuthorize]` is read
by the endpoint filter too, so only the behavior moved. What the behavior needs
from its concern it takes by referencing it; `Bsft.Resilience.Mediator` needs
nothing from `Bsft.Resilience` and references it not at all.

This is checked mechanically by `bsft-behavior-in-mediator-package`, listed with
the other conventions rules below.

**A package that purges takes `Bsft.Retention`.** Its only framework reference is `Bsft.Core`, so a
package that has to delete its own expired rows pays for the loop and for nothing else: it implements
`IRetentionPass` and registers it with `AddBsftRetention<TPass>` instead of hosting a sweeper of its
own. `Bsft.Retention` is what the 1.3.32 note below promised and 1.3.34 delivered.

The history is worth keeping, because it is the shape this rule is really about. The primitive first
shipped inside `Bsft.Scheduling`, and `Bsft.Auditing.EFCore` and `Bsft.Jobs.EFCore` referenced that
package to reach it. The arrow was recorded here as a deliberate exception rather than a precedent,
with the note that a second purging family adopting the primitive was the signal to extract a
dependency-light package instead of widening the exception. Two alternatives were weighed and
rejected at the time and are still rejected now:

- **`Bsft.Core`** — where the shared `PollLoop` already lives, and where the sweeper would need no new
  arrow at all, because every package references it. It is nevertheless the wrong home: `Bsft.Core` has
  **no package references whatsoever**, which is exactly the property section 1 relies on when it lets
  an `.Abstractions` package take it. A hosted `BackgroundService` with options, DI and logging would
  add five `Microsoft.Extensions.*` references to the one package the whole framework depends on, so
  the closure of every package in the tree would widen to pay for one loop.
- **A satellite (`Bsft.Auditing.Scheduling`)** — the shape section 1 prescribes for a cross-family need.
  It answers the wrong question: the sweeper is not a bridge between two families, it is a primitive
  several unrelated families need, and one satellite per family would be one package per consumer.

So the extraction happened rather than the exception growing, and there is no exception left to
record: `Bsft.Retention` is an ordinary `Bsft.<Feature>` package that a purging package references
like any other.

Split an `.Abstractions` package out only when a consumer must reference the
contract without pulling the implementation (a client assembly, a second
implementation, a package that must stay dependency-light). Otherwise a contract
lives in the implementation package, in the feature folder it serves or in the
package root when several features share it (section 2). Doing both
for the same family (some interfaces inline, some in the sibling) is not
allowed: once the sibling exists, every public interface of the family lives
there. The one exception is an interface whose signature needs a type the
`.Abstractions` package must not reference (`HttpContext`, EF Core types): it
stays in the implementation package, because the dependency closure of an
`.Abstractions` package (BCL and `Microsoft.Extensions.*.Abstractions` only) is
part of its contract and is pinned by tests where a client assembly depends on
it.

Every package that ships behaviour has exactly one test project,
`tests/<Package>.Tests`, whose folder tree mirrors the package. A contract-only
package is the exception: where its types are records, constants and marker
interfaces, they are exercised from the test project of the implementation that
consumes them, and the 100 % coverage gate is what makes that binding rather than
optional. Four packages are in that position — `Bsft.Correlation.Abstractions`,
`Bsft.Data.Contracts`, `Bsft.Mediator.Abstractions` and
`Bsft.Notifications.Abstractions`; the other twelve `.Abstractions` packages
carry behaviour of their own and have their own suite. A near-empty test project
asserting that a record holds what was put into it is not the point of the rule. Test fixtures live in `Support/`, fakes in `Fakes/`.
Test classes are named `<TypeUnderTest>Tests`; the test method carries the
scenario (`Method_State_Expectation`).

Test-only code that would otherwise be identical in two or more test projects
lives in `tests/Shared/` and is pulled in with
`<Compile Include="..\Shared\<File>.cs" Link="Shared\<File>.cs" />`, not with a
shared project reference. `tests/Shared/DependencyClosure.cs` is the case: three
packages assert the same "not the mediator" edge about themselves, and reading the
restored graph works the same way each time. It is a linked file rather than a
project because the code is not a package, ships nowhere and belongs to no
closure — the folder has no `.csproj`, so nothing enumerates it as a test project
and no implicit glob picks it up. The assertion stays with the package it is
about; only the mechanics move.

One file is compiled in by `Directory.Build.targets` instead of being linked:
`tests/Shared/RelationalModelWarmup.cs` goes into every test project whose
resolved references include EF Core's relational layer. EF Core builds a model's
relational view lazily, and two contexts asking for it at once can make one of
them throw "The model must be finalized"; parallel test classes sharing a context
type do that on a loaded machine. The file builds the view as each context
initializes, and it has to reach every such test project, including the next one,
without anybody remembering to link it.

### Tiers: Foundation and framework

The packages come in two tiers. The **Foundation** is the part of the framework
that stands on its own — the mediator, the event aggregator and sagas, data,
messaging, outbox, scheduling, caching, localization, hosting, resilience,
observability and their satellites — and is published to nuget.org under MIT
from a public repository, `bochmann-software/bsft-foundation`. Everything else
is the **framework** tier: commercial, released from here to the internal feed,
and free to build on the Foundation. This repository stays the source of truth
for both; the public repository is a snapshot exported from it.

**A package is Foundation if and only if its project file says
`<Version>$(BsftFoundationVersion)</Version>`.** Every other package says
`<Version>$(BsftVersion)</Version>`. There is no second marker — no list, no
naming rule, no folder — so moving a package between the tiers is a one-line
change the structure check sees.

The rules that make the cut real:

- **A Foundation package references only Foundation packages.** The export has
  to build without anything that stays here. A Foundation package that needs a
  framework concern takes it the way `Bsft.Mediator` takes the authorization
  and auditing behaviors: by name, at runtime, when the package is present, or
  through a seam the framework package implements — never by a reference.
- **`Bsft.Foundation.sln` is the manifest of the export.** It lists exactly the
  Foundation packages, the test project of each, and the Foundation samples
  (`Bsft.Sample.Inventory`, `Bsft.Sample.Orders` and their `.Core` projects),
  and every project in it references only projects in it. The export renames it
  to `Bsft.sln` in the public repository. `Bsft.sln` keeps listing every
  project, both tiers, and is what the gates here run on.
- **A Foundation test project tests with Foundation packages only.** A test that
  needs a framework package to say something about a Foundation one — the
  mediator's default chain with the authorization, organization and audit
  behaviors in it — lives in a framework test project
  (`tests/Bsft.Mediator.Scenarios.Tests`), and the Foundation package reaches its
  100 % coverage from its own test project alone. The same holds for a linked
  file from `tests/Shared/`: a Foundation test project links only files that
  compile without framework packages.
- **A framework package may reference any Foundation package**, and does so at
  `BsftFoundationVersion`.
- **The target frameworks follow the tier.** A Foundation package targets
  `net8.0` and `net10.0` (`BsftFoundationTargetFrameworks`), and so does a
  framework package (`BsftFrameworkTargetFrameworks`); every other project — a
  sample, `Bsft.Templates`, a scenario test project — targets the reference
  framework `net8.0` (`BsftReferenceTargetFramework`). The two tiers name their
  frameworks separately so that each can move on its own (the public Foundation
  may keep a framework longer than the internal packages), but each includes the
  reference framework, which the analysis and the coverage gate measure every
  package on. `Directory.Build.props` reads the tier from the `<Version>` line of
  `src/<name>/<name>.csproj` itself, so the version marker stays the only marker:
  no project file names its target frameworks. A test project takes the tier of
  the package it is named after (`tests/Bsft.X.Tests` tests `src/Bsft.X`), so a
  package is tested on every framework it ships for. `net10.0` restores the 10.0
  line of the Microsoft.Extensions.*, ASP.NET Core and EF Core packages, `net8.0`
  keeps its own (`Directory.Packages.props`): a `net10.0` host runs EF Core 10,
  whose `ExecuteUpdateAsync` an assembly compiled against EF Core 9 cannot call.
  Where an API differs between the two lines, the source says so with
  `#if NET10_0_OR_GREATER` at that call and nowhere else.
  Coverage is held at 100 % on each framework and never merged across them: a
  line covered on one framework says nothing about another (.NET 10 runs a hosted
  service's `ExecuteAsync` on the thread pool, and skips the key selector of an
  `OrderBy` over one element). `BsftOnlyTargetFramework` narrows a build to
  `net8.0` where a gate measures that one alone; the further frameworks are
  tested with `dotnet test --framework` after a full build, because narrowed to
  `net10.0` the `net8.0` samples lose the packages they reference. Mutation testing runs on
  `net8.0` only, the framework every project shares; a mutant is a change to the
  source and the same on both.

The two version lines and what publishes each are in
the release documentation. `BsftVersion` and the repository
URLs live in `Repository.props`, the one file that differs between this
repository and the public one; `Directory.Build.props` imports it first and holds
everything the two share, `BsftFoundationVersion` included.

`Bsft.Testing` is Foundation and carries the test doubles a Foundation package
needs: tenant, correlation, clock, the mediator and saga harnesses, the web
application factory, the fake OIDC authority and push service. The signed-in
test user, the permissions and the `BsftTest` authentication scheme build on
`Bsft.Auth` and are in `Bsft.Auth.Testing`, which is framework tier.

The tier rules are checked by `node .github/verify-structure.mjs`
(`tier-marker`, `tier-target`, `tier-reference`, `foundation-solution`; see
"The structure check" below).

## 2. Folder layout

Folders are **features, not roles** (ADR-0013 D1, after .NET's own guidance to
organize namespaces by feature rather than by type kind). A folder names the part
of the package's job its types do together — `Management`, `Delivery`,
`Subscriptions`, `Login`, `Retention` — never the kind of type they are. A
feature holds everything of one use case: the mediator message and its handler,
the service behind it, the endpoint and the request records only it uses, the
store seam only it needs, its exceptions and its internal helpers. "Rotate a key"
lives entirely in `Bsft.Auth.ApiKeys/Management/`.

| Folder | Holds |
| --- | --- |
| package root | The package's **shared vocabulary**: the seams several features use (the main store or service interface), the records, enums and value objects those seams speak in, the primary `Bsft<Feature>Options` and its validator, `<Feature>Defaults`, and `I<Feature>DbContext`. The root is a **sink**: no root type depends on a feature namespace of its own package. When one does, either the feature type it names is vocabulary too and moves to the root, or the root type belongs in that feature. |
| `<Feature>/` | One use case or concern, as above. Nested at most once (`Delivery/Retries/`). |
| `DependencyInjection/` | The DI entry points — `*ServiceCollectionExtensions`, `*EndpointRouteBuilderExtensions`, `*ApplicationBuilderExtensions` — and wiring-only helpers such as registration markers and the `IConfigureOptions<>` implementations that exist only for the registration. Nothing that runs at request time. It depends on the features it wires, never the reverse: when a feature needs what an entry point does (a harness or a factory registering the package's own doubles), the work lives in an internal helper of that feature and the entry point checks its arguments and delegates to it. |
| `Diagnostics/` | `ActivitySource`, meters, event ids, log-message definitions. Cross-cutting, and allowed as long as it stays a sink. |
| `Testing/` | Public test doubles and fixture builders a consumer writes its own tests against, shipped with the package rather than with `Bsft.Testing` (`Bsft.AI/Testing/TestChatClient.cs`, `Bsft.AI.Agents/Testing/AgentRunHarnessBuilder.cs`). Only for a double whose contract is the package's own and whose closure the shared testing package declines to carry, or for a fixture that can only be built through the package's own write path. |

Role and structural names are not folder names: `Services`, `Commands`,
`Queries`, `Endpoints`, `Stores`, `Models`, `Handlers`, `Abstractions`,
`Internal`, `Options`, `Validation`, `Validators`, `Middleware`, `Exceptions`,
`Entities`, `Configurations`, `Helpers`, `Utilities`, `Common`, `Shared`,
`Extensions`, `Interfaces`, `Infrastructure`, `Core`, `Types`, `Records`, `Dtos`.
Nor is a type name: the namespace would shadow the type for everything inside
it. A feature folder may carry the name of a satellite package when it holds the
seam that satellite implements (`Bsft.Chat/Compliance/` for
`Bsft.Chat.Compliance`, `Bsft.AI.Abstractions/Agents/` for `Bsft.AI.Agents`): the
seam and its implementation then share a namespace, as section 3 lets an
`.Abstractions` package share one with its implementation.

A feature folder takes the canonical word of the feature-folder vocabulary in
[naming.md](naming.md#feature-folder-vocabulary): `Auditing` not `Audit`,
`RateLimiting` not `Throttling`, `Execution` not `Running`, `MultiTenancy` not
`Tenancy`, and a family of peers in the plural (`Providers`, `Sandboxes`,
`Imports`). An activity is a gerund or a -tion noun (`Caching`, `Scheduling`,
`StartupValidation`); the bare noun names the types inside it.

**Do not over-split.** A package with about a dozen files or fewer besides its
entry point stays flat: root plus `DependencyInjection/`. A feature normally has
three files or more; a one-file namespace is merged into the root or a neighbour
unless that creates a cycle. Files are counted per namespace across an
`.Abstractions` assembly and its implementation, since the two share it;
`DependencyInjection`, `Diagnostics` and `Testing` are exempt. A package root may
hold a single file.

**An `.Abstractions` package uses the feature folders of its implementation.** A
contract goes into the feature it serves, or to the root when several features
share it; with the shared namespaces of section 3, a consumer finds the seam and
its implementation's types under one name.

**EFCore and other satellite packages** have their own namespace root. A small
one stays flat. A large one mirrors the core package's features: `Messages/`
holds `MessageEntity`, `MessageEntityConfiguration` and `EFCoreMessageStore`
together. Entities that `I<Feature>DbContext` names for several features stay in
the root beside it.

**The feature namespaces of a package form no cycle** (ADR-0013 D5). A feature
may depend on the root and on other features; the root depends on none, and two
features that need each other share a type that belongs in the root or in a small
third feature (`Bsft.Auth.ApiKeys.Secrets` holds the key format both `Management`
and `Authentication` use). A cycle is a finding, not a layout choice.

## 3. Namespaces

The namespace of a file is the package's namespace root followed by its folder
path, **without exceptions**: `src/Bsft.Storage/Downloads/FileDownloadEndpoints.cs`
declares `namespace Bsft.Storage.Downloads;`, and no folder is "structural" and
skipped. File-scoped namespaces only. IDE0130 checks this in the build:
`.editorconfig` raises it and `Directory.Build.props` sets
`EnforceCodeStyleInBuild`, so a namespace that does not match its folder fails
compilation. The same holds in test projects, whose root is their assembly name.

Package ids, assembly names and namespace roots are written by the naming rules
([naming.md](naming.md) N1.2, N3.6): a two-letter acronym is all caps and the
activity form names a cross-cutting concern, so the roots are `Bsft.AI`,
`Bsft.Auditing` and `Bsft.<Feature>.EFCore`, not `Bsft.AI`, `Bsft.Audit` and
`Bsft.<Feature>.EfCore`.

The namespace root is the assembly name, except for an `.Abstractions`
package, whose root is the name of the package it abstracts:
`Bsft.Storage.Abstractions` declares `namespace Bsft.Storage;`, and its
`Downloads/` folder `namespace Bsft.Storage.Downloads;`. An `.Abstractions`
assembly and its implementation therefore **share their namespaces**: a consumer
writes one `using Bsft.Storage;` and sees the seam and the implementation's
vocabulary together, and moving a type between the two assemblies is not a
source break. The package and assembly names keep `.Abstractions`; only the
namespace drops it, and `Directory.Build.props` sets the project's
`RootNamespace` to match. Two consequences follow. A type name is declared at
most once across the pair, in the same namespace, and a folder must not produce a
namespace that equals a type name in either assembly, since the namespace would
shadow the type.

`.Contracts` packages use their own assembly name as root
(`Bsft.Data.Contracts.Filtering`): a contract is shared between producers that
do not implement one another, so there is no implementation to share a
namespace with. `docs/migration-2.0.0.md` lists what a consuming repository
changes across the two 2.0.0 namespace moves; the conventions rule
`bsft-namespace-follows-assembly` checks the root.

A consumer imports the package root for its vocabulary, a feature namespace for
that feature, and `<Package>.DependencyInjection` to wire the package:
`using Bsft.Storage.DependencyInjection;` and then `services.AddBsftStorage()`
(ADR-0013 D2). The entry points do not declare
`Microsoft.Extensions.DependencyInjection`, which .NET's guidance for library
authors advises against; a namespace that names the package keeps a host's
IntelliSense honest about where each `AddBsft*` comes from.

## 4. Naming

[naming.md](naming.md) is the binding vocabulary and the rules for every
identifier: capitalization, word choice, type and member names, parameters, wire
names and tests, with a glossary of the framework's terms. This section keeps the
rows that decide how a package is wired and where its types live, and applies
naming.md to them; where the two disagree, naming.md wins.

One top-level type per file, file named after the type. `Bsft` is a **prefix on
the surface a host touches**, and nowhere else:

| Kind | Rule | Example |
| --- | --- | --- |
| DI entry point | `<Feature>ServiceCollectionExtensions` in `<Package>.DependencyInjection`, methods `AddBsft<Feature>[<Aspect>]`. One such class per package, plus one per sub-feature a host registers on its own (see below); an `.Abstractions` package has none. | `StorageServiceCollectionExtensions.AddBsftStorage` |
| Pipeline / route wiring | `<Feature>ApplicationBuilderExtensions` (`UseBsft<Feature>[<Aspect>]`), `<Feature>EndpointRouteBuilderExtensions` (`MapBsft<Feature>[<Aspect>]`), both in `<Package>.DependencyInjection`. The aspect suffix works as it does on `Add`: a package that exposes several separately-mappable surfaces names each after the surface rather than inventing a second class. | `WebhookEndpointRouteBuilderExtensions.MapBsftWebhooks`, `OrganizationEndpointRouteBuilderExtensions.MapBsftPermissions` |
| EFCore registration | `AddBsft<Feature>EFCore<TContext>` on `IServiceCollection`, and nothing else: no `UseDbProvider` on an options object. | `AddBsftSchedulingEFCore<TContext>` |
| Options | Primary options type of a package: `Bsft<Feature>Options`. Nested option groups it exposes as properties: `<Concept>Options` without prefix — no `Bsft`, and no `SectionName` either, because a nested group is bound as part of its parent's section and a second section name for it would be a section nothing reads. Every options type **a host binds** — the ones registered through `AddOptions<T>()` — has a `public const string SectionName` and is validated (`ValidateDataAnnotations` for simple shapes, an `IValidateOptions<>` named `<Options>Validator` beside the options type for cross-member rules), and is registered with `ValidateOnStart()`. Options are also bindable from configuration: the entry point accepts `Action<TOptions>` **and** binds `SectionName` when an `IConfiguration` is present. | `BsftStorageOptions.SectionName = "Bsft:Storage"` |
| Defaults | Constants a host may need (route prefixes, policy names, header names) live in `<Feature>Defaults`, one static class per package. Options default to those constants. | `OrganizationDefaults.RoutePrefix` |
| Endpoints | One static class per resource or protocol surface, `<Resource>Endpoints`, in the feature folder of the use case it serves; the `Map*` method on the family's `EndpointRouteBuilderExtensions` calls them. The route prefix comes from the primary options type, never from a policy record. | `SelfService/SettingsSelfEndpoints.cs` |
| Handlers | `<Message>CommandHandler`, `<Message>QueryHandler`, `<Message>NotificationHandler` implement the matching `Bsft.Mediator` interface, always. Any other `*Handler` implements a named `I*Handler` interface or a framework base class (`AuthenticationHandler<>`, `AuthorizationHandler<>`, `HttpMessageHandler` and its `DelegatingHandler` subclass, `IIntegrationEventHandler<>`). A class that handles nothing declared is not a handler; a class holding several unrelated operations is not a handler either (no `*Handlers` bags). | `AuthorizationCodeGrantHandler : ITokenGrantHandler` |
| Messages | `*Command` implements `ICommand`/`ICommand<T>`, `*Query` implements `IQuery<T>`, `*Notification` implements `INotification`. A parameter object that is not dispatched through the mediator is a `*Request`, and the object an endpoint binds its query string to (`[AsParameters]`) is a `*QueryParameters`; a service that answers questions is `I*Reader` or `I*Lookup`, never `I*Query`. | `ListApiKeysRequest`, `AuditLogListQueryParameters`, `ISettingsAdminReader` |
| Roles | `I*Store` persists and loads, `I*Reader` answers questions from stored data, `I*Lookup` resolves one key to one value, `I*Writer` writes a projection or a file, `I*Service` orchestrates, `I*Provider` supplies a value or instance on demand, `I*Factory` builds a new instance per call, `I*Resolver` picks among candidates, `*Validator` implements `IValidateOptions<T>` or validates a value, `*StartupValidator` implements `IStartupValidator`, `*Builder` is fluent configuration. Do not use two of these for the same role within a family. `Manager` is not a role; `IFeatureManager`, which mirrors `Microsoft.FeatureManagement`, is the one exception. | `ISettingStore`, `ISettingsResolver`, `BsftChatOptionsValidator` |
| EFCore | `<Name>Entity` and `<Name>EntityConfiguration` beside the store that uses them (section 2), `I<Feature>DbContext` at the root. A persistence-only shape (an `*Entity` mapped from the family's records) lives in the EFCore package. When the persisted type *is* the family's domain model, consumed by the core package's own store interfaces and services (`Bsft.Organization`'s `OrganizationUnit`, `Role`, `OrganizationMembership`), it lives in the core package, in its root or in the feature it belongs to, and the EFCore package holds only the configurations; the core package never references EF Core for it. | `AuditLogEntity`, `AuditLogEntityConfiguration` |
| Data types | Data is a `record`, logic is a `class`, with one qualification the analyzer enforces: a record's name has to end in a role suffix (`Result`, `Response`, `View`, `Summary`, `Snapshot`, `Info`, `Item`, `Request`, `QueryParameters`, `Specification`, `Event`, `Query`, `Command`, `Filter`, `Options`, `Descriptor`, …; never `Details`, which is `Info`). A data type named for the thing itself rather than for its role keeps the class form with init-only properties, because renaming it to earn the suffix would cost the vocabulary the family is read in. Where both exist, the domain type is the class and the projection beside it is the record. | `App` and `Installation` are classes, `AppView` and `InstallationView` records |
| Hosted services | Named for what they do with a role noun: `*Worker` (a loop), `*Sweeper` (a loop that purges expired rows), `*Bootstrapper` (runs once at start), and the others naming.md N4.7 lists. Never `*Job`, which is a scheduled `IJob`, and never `*Service`, which is the orchestration role, except a `*HostedService` adapter. | `InboxSweeper`, `IdleShutdownWorker` |
| Events | An integration, domain or audit event contract ends in `Event`, with a past-tense stem (naming.md N4.8). | `CredentialChangedEvent` |
| Visibility | `internal sealed` unless a consumer needs the type; `public` types are `sealed` unless designed for inheritance (then documented as such). Public interfaces exist only where a second implementation is a realistic requirement; helpers are `internal`. | |

### Documented exceptions

Two rules above have cases where following the letter makes the package worse.
Both are closed lists: a package not named here follows the rule, and adding to
either list means arguing the case in the pull request that adds it.

**An options type that no configuration provider can supply carries no
`SectionName` and no `IConfiguration` overload.** The Options-pattern
registration still applies — `AddOptions<T>().Configure(...)`, `IOptions<T>` at
every consumer, never a registered instance — but a configuration section is not
invented for values a section cannot hold. Two kinds qualify:

- The value is an object chosen in code: an assembly to scan, a delegate, an
  implementation instance. `Bsft.Calendar`'s `BsftCalendarOptions` (every member
  is an `IBusinessCalendar`), `Bsft.Mapping`'s `MappingConfiguration`,
  `Bsft.Modules`' `ModulesConfiguration`, and `Bsft.Auth`'s `InMemoryApiKeyOptions`
  (the key set is reached through `AddKey`, a method, so there is no property for a
  provider to write).
- The value states something a migration already built into the schema, so a
  second source for it would let a deployment contradict its own database.
  `BsftDataPostgresOptions`, `BsftDataSqlServerOptions` and
  `BsftDataSqliteOptions` declare which full-text index exists, which
  `regconfig` was baked into a stored generated column, and which tokenizer the
  FTS5 table was created with. A host able to state those in `appsettings.json`
  is a host able to state them wrongly, and the failure is silent: a search that
  stems by one configuration against an index built with another. The declaring
  methods validate their arguments, so `ValidateOnStart` has nothing left to
  check and is omitted rather than registered empty.

`Bsft.Calendar` keeps `ValidateOnStart` because "a default calendar is present"
is checkable; the three provider packages do not, because nothing is.

Two further shapes sit beside that list rather than inside it, because each keeps
half the rule:

- `Bsft.Auth.Oidc.Organization`'s `BsftOidcOrganizationOptions` keeps its
  `SectionName` and its `IConfiguration` overload for `MembershipRoles`, which a
  section can hold, while the key-to-unit mapping stays in the action because
  `Map` is a method. A partially bindable options type binds the part it can.
- `Bsft.Auth.Idp.Organization`'s `BsftAuthIdpOrganizationOptions` keeps its `SectionName` and its
  `IConfiguration` overload but registers no validator and no `ValidateOnStart`, because it is two
  independent booleans whose four combinations are all deployments somebody makes on purpose: a host
  that keeps activation state outside the directory turns the first off, a single-tenant host has no
  tenant to match and turns the second off. A validator there would be a rule that never fires plus a
  claim that the configuration was checked.
- `Bsft.AI`'s `BsftAIOptions` keeps its `SectionName` and its `IConfiguration` overload for the
  values a section can hold — the profile table's model ids and limits, the resilience bounds, the
  default profile name — while the inner chat client is a delegate, a prompt body is a literal that an
  author wrote and a tool is an object, so those three stay code-only behind `UseChatClient`,
  `AddPrompt` and `AddTool`. The binding is written out member by member rather than left to
  `section.Bind(options)`, because a blanket bind would walk the whole public surface and quietly do
  nothing for the code-only members, which reads as though a section could supply them. Wave 2's
  `BsftAIAgentsOptions` follows the same shape for the same reason.
- `Bsft.Speech`'s `BsftSpeechOptions` follows `BsftAIOptions`: it keeps its `SectionName` and its
  `IConfiguration` overload for the provider table a section can hold — endpoint, model allowlist,
  default voice, caps — while a provider's `ISpeechProviderBuilder` is an object chosen in code and is
  reached only through `AddProvider`. Because the two halves are configured separately they can name
  different providers, so the registered validator checks both directions and start-up fails on either
  half naming a provider the other does not.
- `Bsft.Resilience`'s `BsftResilienceOptions` is not registered through
  `AddOptions<T>` at all: it configures one `IHttpClientBuilder` and is bound and
  validated per client at registration, so a single container-wide `IOptions<T>`
  would be the wrong shape. It keeps `SectionName` — each call binds it — and
  validates imperatively.
- `Bsft.Resilience`'s `BsftResilienceStreamOptions` goes one step further and is
  the only options type in the tree with **no** `SectionName`: it configures one
  `ResilientStreamExtensions.WithRetry` call, half of it is objects chosen in code
  (a classifier delegate and a `TimeProvider`), and what it configures is a static
  call rather than a registration — so there is no host-wide instance a section
  could bind and no `IConfiguration` overload to put it on. It validates
  imperatively on the call, like its sibling, and it is the reason the package has
  two `Bsft*Options` types: the HTTP one describes one named client, this one
  describes one stream, and neither is a group of the other.
- **The `Bsft.Chat` family binds its options per named instance.** Every options type in the family
  keeps its `SectionName` and its `IConfiguration` overload but binds named rather than singly: the
  registration call takes the instance name and binds `{SectionName}:{instanceName}`. That covers
  `Bsft.Chat`'s `BsftChatOptions` (`Bsft:Chat:Instances:{instanceName}`, `AddBsftChat`) and every
  satellite whose behaviour is a per-instance decision — `Bsft.Chat.Notifications`'
  `BsftChatNotificationsOptions` (`Bsft:Chat:Notifications:Instances:{instanceName}`, what an instance
  notifies about, ADR-0008 D8) and `Bsft.Chat.Storage`'s `BsftChatStorageOptions` (where an instance's
  attachments live). A satellite of a named-instance package inherits that package's shape rather than
  inventing a second one, and a satellite cannot have fewer instances than the pipeline it answers
  for. The rest of the rule holds unchanged for all of them: one `IValidateOptions<T>` per type,
  dispatching on the `name` argument `AddOptions<T>(name)` passes it, `ValidateOnStart()` once per
  registered instance, and `IOptionsMonitor<T>` — never a registered instance — at the consumers.
  Taking `BsftChatOptions` as the worked example (ADR-0008 D3): the section name
  itself has no instance in it, and the instance is the sub-key a runtime call supplies, not a value
  in the file a section name alone could point at. This is a genuinely new shape rather than a
  restatement of the "no section" exceptions above: the type is fully bindable, it is the *fan-out*
  across instances that a single fixed section name cannot express, because named instances are a
  runtime decision (how many, and what they are called) that the framework has no other precedent for.

**A package whose sub-features are registered independently has one
`<Aspect>ServiceCollectionExtensions` per such sub-feature**, all of them in its
`DependencyInjection` namespace. `Bsft.Hosting` is the main case: a host calls
`AddBsftCors` or `AddBsftSwagger` on its own as readily as it calls
`AddBsftDefaults`, and each aspect owns its own options type, section and
pipeline step. `Bsft.Data` (sandbox tenants), `Bsft.Chat` (display names) and
`Bsft.Auth.TokenServer` (the scope catalog) each have one such second class. The
one-class rule holds everywhere else, including for a package that merely
exposes several methods — `Bsft.Testing` keeps its four `AddBsftTest*` methods
on one `TestingServiceCollectionExtensions` (and `Bsft.Auth.Testing` its three on
`AuthTestingServiceCollectionExtensions`), because none of them is a separable
feature with its own configuration.

### CodeCharter and the conventions

Two findings are structural consequences of this layout and are exempted per
package in `.codecharter/config.yml` with a one-line rationale:
`namespace-distance` on a package root namespace that holds only stable
vocabulary (records, enums and options every feature namespace depends on, so it
has almost no abstractness at maximum afferent coupling; since 2.0.0 each such
root is reviewed on its own before it is exempted) and `high-afferent-coupling` on the
primary `Bsft<Feature>Options` type. The same `high-afferent-coupling` exemption
extends to a family's shared identity value types — the validated value object
every seam in the family is written in terms of, such as `SecretPurpose` or
chat's `ChatActor` and `ChatInstance`. The fan-in is the design: the type exists
so that identity or scope is a checked value rather than a loose pair of
arguments, and every contract naming it is a contract that cannot be called
without one. The rule's prescribed fix, an interface consumers depend on
instead, would hand back exactly what the value object removed, because an
interface a caller can implement is an identity a caller can forge. The
exemption covers only such types: one per concern, living in the family's
`.Abstractions`, named by most of that family's contracts, and either
constructible only inside the package (`ChatInstance`) or a value whose parts
are meaningless apart (`ChatActor`'s kind and id). A type that is merely used a
lot is not one of them. A third exemption follows from
section 4 itself:
`type-name-length` and `method-name-too-long` on the mandated wiring names
(`<Feature>ServiceCollectionExtensions`, `<Feature>EndpointRouteBuilderExtensions`,
`<Feature>ApplicationBuilderExtensions`, `AddBsft<Feature><Aspect>`), whose
suffix alone spends 26 to 30 of the 40 characters; the shortest conforming
name is chosen (drop the family prefix the namespace already carries), and
what still exceeds the limit is exempted by exact name under the shared
"convention-mandated wiring names" block in the config. A fourth follows from
section 2's split between contracts and behaviour: `namespace-distance` on a
family's **contract namespaces** — the ones holding the domain records, the
value objects and the store or service seams written in terms of them, such as
the `Bsft.Chat` root and `Bsft.Chat.Sync`. The
metric moves the moment a persistence or service package starts depending on
them, and it measures the layout the conventions require: records are concrete
because they exist to be constructed, pattern-matched and serialized, and the
abstractions the rule asks for are already there, one namespace over, as the
seams. Its only remaining lever is an interface in front of a record nobody
would implement twice. Exempted per namespace, never per rule, and only for a
namespace that is genuinely contracts-and-records: one that grows behaviour
loses its line and takes the finding seriously.

A fifth follows from the framework having seams that are not any one
family's: `high-afferent-coupling` on a **framework-wide seam** — an interface
in an abstractions seam that answers one cross-cutting question every
feature package has to ask, such as `Bsft.Auth.Users.ICurrentUser`,
whether that seam comes from a separate `.Abstractions` package or from the
package that owns the concern. Its
fan-in is the point rather than a symptom: an endpoint that renders anything
about the calling principal has to ask who is calling, and the alternative to
one interface is every package inventing its own. The rule's prescribed fix
does not apply at all here, because it asks for an interface consumers depend
on instead and the finding already names one. What separates this from "a type
that is merely used a lot" is the same test the identity value types get: one
per cross-cutting concern, already an interface with an implementation the
framework owns, and answering a question that has exactly one correct answer
per request — not a service that happens to be convenient. Exempted by exact
type name beside its family's other entries, never by rule and never by
namespace.

A sixth and last covers the one record a family exists to carry:
`high-afferent-coupling` on a **family aggregate record** — the single domain
record a family's architecture decision names as the unit its stores, services,
projections and exports all read and write, such as chat's
`Bsft.Chat.ChatMessage` under ADR-0008 D4. Its fan-in
counts the packages of one family, and it grows with every package the family
gains rather than with any misuse: a transcript store, a search index, a
notification projection and an eDiscovery export are four readers of the same
transcript, and the number of them is a statement about the family's scope, not
about the record. The rule's prescribed fix is an interface over the record the
whole family is written to carry, which would make the family's central value
forgeable and buy nothing, since no consumer would implement it twice. The test
that separates this from "a type that is merely used a lot" is narrow: exactly
one per family, named as the unit by that family's ADR, living in the family's
`.Abstractions` beside the identity value types, and depended on only by
packages of its own family. Exempted by exact type name beside its family's
other entries, never by rule and never by namespace. Nothing else in these
conventions justifies a new exemption.

The same "per signature, never per rule" discipline governs `record-input-naming`
and `record-output-naming`, which ask a record crossing a method boundary to
announce its role. Every record that can be renamed to comply is renamed, and
the rename usually improves the name. What is exempted is the record that
travels in *both* directions — a family's domain model, its identity value type
or a keyset cursor a page hands out and the next call hands back — because the
only suffix present in both rules' lists is `Dto`, and calling a family's own
aggregate root a transfer shape describes it less accurately, not more. Each
exemption names the exact signature, so a new seam taking a new record is
reported normally and must earn its own name.

### The conventions rules

Eight of the rules above are checked mechanically rather than by review. They live
in `.codecharter/rules` as `.ccr` files with a `.spec.md` beside each:

| Rule | Enforces |
| --- | --- |
| `bsft-options-no-singleton` | No `AddSingleton`/`TryAddSingleton` of an options type or a `*Options` instance (section 4, "Options"). |
| `bsft-di-entry-point-namespace` | A `*ServiceCollectionExtensions`, `*EndpointRouteBuilderExtensions` or `*ApplicationBuilderExtensions` class lives in a `.DependencyInjection` namespace (section 3, ADR-0013 D2). It replaced `bsft-single-service-collection-extensions` in 2.0.0, whose premise — one entry point per feature namespace — ended when every entry point of a package moved into one namespace. |
| `bsft-service-collection-extensions-name` | No bare `ServiceCollectionExtensions`; the entry point is named after its feature. |
| `bsft-behavior-in-mediator-package` | A `*Behavior` class lives in the mediator or in a `.Mediator` satellite, never in a concern package (section 1, the `Bsft.<X>.Mediator` rule). |
| `bsft-mediator-handler-interface` | A `*CommandHandler`, `*QueryHandler` or `*NotificationHandler` implements the matching contract from `Bsft.Mediator.Abstractions`, directly or through its base class. |
| `bsft-handler-implements-contract` | Any other `*Handler` implements an `I*Handler` or derives from a handler base such as `AuthenticationHandler<>` or `DelegatingHandler`. |
| `bsft-mediator-message-marker` | A `*Command` implements `ICommand` or `ICommand<TResult>`; an argument shape is named `*Request` instead. |
| `bsft-namespace-follows-assembly` | A type's namespace starts with its assembly name, for an `.Abstractions` assembly with the trailing `.Abstractions` removed (section 3). Its spec can exercise only the plain branch, because the spec engine compiles every case into an assembly named `TestAssembly`; the `.Abstractions` branch is checked by the solution itself, which must report 0 hits. |

`bsft-behavior-in-mediator-package` is a name-and-namespace approximation of what
the rule says, and deliberately so: it matches `*Behavior` rather than
"implements `IPipelineBehavior<,>`", because that interface is generic and the
model's interface set is matched on the open name. That costs it a behavior
somebody names differently, and it gains a check that fires the moment a
behavior is written in the wrong package. `TypeModel.Namespace` is a second small
dead end — it answers with a `NamespaceModel`, which carries no `Contains` — so
the namespace is read out of `FullName`, whose `.Mediator.` segment cannot occur
in a type name.

The three interface rules read `ImplementedInterfaces`, and each carries one hop
up the base chain so a handler inheriting its contract from a shared base is not
flagged. Two limits shaped them and are worth knowing before writing a fourth:

- **The set holds only what the analysed solution declares.** A type implementing
  an interface from ASP.NET Core reads as implementing nothing, which is why
  `bsft-handler-implements-contract` falls back to `DeclaredBaseTypeName` for
  bases — that one *does* survive an external type — and why the single
  `IAuthorizationMiddlewareResultHandler` implementation in the tree is exempted
  by name in `.codecharter/config.yml`. There is no `DeclaredInterfaceNames`;
  asking for one is an open request upstream.
- **It is also empty across an indirect project reference.** The samples
  implemented `ICommandHandler<,>` while referencing only `Bsft.Mediator`, and
  read as implementing nothing until they were given a direct reference to
  `Bsft.Mediator.Abstractions` — which they should have had anyway, since they
  use its types.
- **It is also empty across a multi-targeted reference.** Since the packages build for
  `net8.0` and `net10.0`, a project's interfaces from a referenced package
  (`ICommand`, `ICommandHandler<,>` and the rest of `Bsft.Mediator.Abstractions`) are missing from
  the set, and these rules flag handlers that do implement their contract. Analyse with the
  packages narrowed to the reference framework, as `ci.yml` does:
  `BsftOnlyTargetFramework=net8.0` for both the build and `codecharter analyze`.

`bsft-mediator-message-marker` checks `*Command` and deliberately not `*Query`.
`Query` had a second, older meaning here: the object an endpoint binds its HTTP
query string to, and an argument shape. 2.0.0 renamed the 36 types that used it
that way to `*QueryParameters` (the 24 `[AsParameters]` binders) and `*Request`
(the 12 argument shapes), so a `*Query` is now a mediator query throughout. The
rule still checks only the `*Command` half; extending it to `*Query` is a rule
change of its own, with its spec and a run over the tree, and until then the
`*Query` half is a review item.

They run in the same `codecharter analyze` pass as the profile: `.codecharter/config.yml`
declares the directory under `rules:`, which CLI 1.6.4 runs alongside the profile (up to 1.6.3 a
profile and `--rules` excluded each other, so they were a second pass). The first line of the run
states the reach, `Rules: 247 from codeguard/csharp-all@1.2.0, 8 from .codecharter/rules`; a run
that names fewer rule sources than the configuration is not a verdict, and exit code 2 means the
run was inconclusive rather than clean. Locally:

```bash
export BsftOnlyTargetFramework=net8.0         # the reference framework, as ci.yml analyses
dotnet build Bsft.sln -m:1
codecharter restore
codecharter analyze Bsft.sln --fail-on info   # the profile and these rules
codecharter test .codecharter/rules           # their specs
```

Two further checks stay a review item, and for a different reason than the three
rules above once did. The DSL gaps that blocked them are gone — CLI 1.6.3 exposes
`ImplementedInterfaces`, `Fields` and `Constructors`, all under names the earlier
authoring docs did not use — so what remains is that the conventions themselves
have no mechanical form:

- *An options type declares `public const string SectionName`.* The field is
  readable now (`t.Fields.Any(f => f.Name == "SectionName" && f.IsConst)`), but
  the rule would be wrong: six options types in the tree deliberately carry no
  section, each saying so in its own XML doc, because they are not bound from
  configuration — `BsftCalendarOptions`, the three `Bsft.Data.<Provider>Options`,
  `BsftResilienceStreamOptions`, and the two that derive from an ASP.NET options
  base. "Is bound from configuration" is not something the model holds, so the
  rule would be six exemptions guarding fifty-odd types.
- *No constructor takes a raw `*Options`.* Constructors are readable now
  (`t.Constructors`), and the parameter type matches as a string. What the rule
  needs and cannot have is "is this constructor a DI constructor": a dozen
  internal records and state shapes take an already-resolved options instance as
  an ordinary argument, which is correct, and no expression separates them from a
  service that should have taken `IOptions<T>`.

One note for anyone writing a rule here, which cost a day when the first three
were attempted: an unknown member fails at *evaluation*, not at parse time, so
`codecharter validate` passes and the run reports zero findings. A rule that can
never fire looks exactly like a clean tree. `codecharter test` with a hit case is
the only thing that tells them apart, and it is worth writing the spec first.

### The structure check

What the rule DSL cannot see — folders, files that are not compiled, the
solution file, `docs/packages.md` and the namespace graph — is checked by
`.github/verify-structure.mjs`, the `structure` job in `ci.yml`, over the whole
repository on every run: moving a file changes no line, so a changed-lines
analysis never sees it. It checks sections 2 and 3 and the checklist below:
every project in `Bsft.sln`; namespace root and namespace = folder; no role or
type name as a folder; nesting at most once; no one-file feature namespace; a
small package flat; the root and `Diagnostics/` sinks; `DependencyInjection/`
holding wiring only and depended on by no feature; `AddBsft*`/`MapBsft*`/`UseBsft*`
in `<Package>.DependencyInjection` in a class named for its receiver; no
namespace cycle; `.Abstractions` folders that are feature folders of the
implementation; a mirroring test project per package; large satellites' entities
beside their feature; a `docs/packages.md` row per package; one top-level type
per file, named after it; and the tiers of section 1 — every package names one of
the two version properties, no project names its target frameworks, each tier's frameworks include the reference
framework, a Foundation package references only Foundation packages, and `Bsft.Foundation.sln` lists exactly the Foundation packages, their
test projects and the Foundation samples, closed under references. Each violation
names the sentence it breaks. Locally:

```bash
node .github/verify-structure.mjs                     # source checks, seconds
codecharter graph overview Bsft.sln --top 100000 --format json > graph.json
node .github/verify-structure.mjs --graph graph.json  # plus the four graph checks
node --test '.github/structure/*.test.mjs'            # self-test, one case per rule
```

## 5. Documentation

Every package has a `README.md` in its folder, written from
[templates/PACKAGE_README.md](templates/PACKAGE_README.md): what the package
gives a host, install, register, configure (options table), extend (replaceable
seams), and links to the topic page. That file is packed as the NuGet
`PackageReadmeFile` (`Directory.Build.props` packs `$(MSBuildProjectDirectory)/README.md`),
so a package without a README does not pack. The README answers "do I want this
and how do I wire it"; the topic page under `docs/` explains how it works.

Every package also appears in [packages.md](packages.md), which maps each
package to the topic page that documents it and to its entry point. A satellite
package (`Bsft.Auditing.Oidc`, `Bsft.Auth.ApiKeys.Notifications`) is documented as
a section on its family's topic page, never left undocumented. Every public type
carries an XML `<summary>`; the doc explains intent and constraints, not the
signature.

**One package is not a library, and the rules above bend for it exactly once.**
`Bsft.Templates` is a `dotnet new` template pack: content only, no assembly. It
therefore has no `ServiceCollectionExtensions`, no options type and no mirroring
test project — its tests are the generation matrix in
the template documentation, which holds every *generated* tree to all of these
rules plus the two gates. It keeps its README and its row in
[packages.md](packages.md), where the entry-point column reads
`dotnet new install Bsft.Templates` rather than an `AddBsft*` method. That is a
closed exception for this one package; a second content-only package would have
to argue its case in the pull request that adds it.

## 5a. Reads are graded: metadata is not content

A family that persists something a person wrote, uploaded or received grades its
reads into two permissions rather than one, and the split is by *what is
returned*, not by which endpoint returns it:

- `<family>.read` covers metadata — identifiers, names, timestamps, status,
  counts, sizes, the sender and the recipient. This is what a listing needs and
  what an operator console renders by default.
- `<family>.read-content` covers the payload itself — the message body, the
  transcript, the object bytes, the transferred rows, the audit entry's changes.

A content read is **audited**. A metadata read is not, because auditing every
listing produces a trail nobody can read, and because the metadata is what the
audit entry would repeat anyway.

The reason for the split is that the two reads carry different risk and are
needed by different people at different times. Support answers most questions
from metadata; the moment it needs the payload, that is a decision worth
recording. Collapsing them into one permission means the person who may see that
a message exists may also read it, forever, silently.

`Bsft.AI` is the shipped precedent: `ai.agents.read-content`,
`ai.assistants.read-content` and `ai.skills.read-content` sit beside the
corresponding metadata permissions. ADR-0008 makes this binding for new families
and calls for the retrofit of the existing ones; the inventory of what still
owes it is in
the operator-plane read-surface survey.

A family that holds no payload — a scheduler, a feature-flag store, a
registry — has nothing to grade and keeps one read permission.

### Two forms that satisfy this without a second permission

The rule is about **reading a payload one is not otherwise entitled to**. Two
shapes meet that intent by other means, and reading the letter of the rule onto
them makes the family worse rather than safer.

**A per-object capability with a pre-disclosure audit hook.** `Bsft.Storage` is
the worked example. It declares no read permissions at all and has no listing
endpoint, so there is no metadata read for an object read to be graded against.
What it has instead is stricter than a permission: a signed, expiring token that
names one object, verified by `IDownloadTokenVerifier`, and an
`IStorageAuditSink` told before the disclosure — before any bytes leave the
process on the route the application serves, and at the mint on a provider that
presigns natively, where the mint is the last moment the application sees the
request. A permission names a class of objects and does not expire; a capability
names one and does. If the family ever gains a paged object-metadata listing,
that listing ships graded like any other.

**A read already scoped to the person who caused the thing.**
`Bsft.DataTransfer` is the worked example. Every one of its read routes goes
through `ITransferJobReader`, which answers only for jobs the caller submitted —
another tenant's job, or another user's, is absent rather than refused. Splitting
the job from its own result there would read "you may watch your own import
finish but not download its own per-row report", which separates nobody from
anything. `Bsft.Chat` is the same shape on its member path: a participant reading
the conversation they are in is not an operator read, and grading that endpoint
would mean a member needs a second grant to see their own chat. In a family like
this the **operator** path is what gets graded — moderation, compliance,
anything that crosses into a conversation or a job the caller is not party
to — and the self-service path stays as it is.

Either way the reasoning belongs in the package README, not only in the pull
request: the next survey reads the package, and a gap that was a decision has to
say so where it will be found.

## 6. Adding a package (checklist)

1. Name and family per section 1; decide whether an `.Abstractions` sibling is
   warranted (default: no).
2. `<Feature>ServiceCollectionExtensions` with `AddBsft<Feature>` in
   `DependencyInjection/`; `Bsft<Feature>Options` at the root with `SectionName`,
   validation and `ValidateOnStart`; `<Feature>Defaults` if the host needs
   constants.
3. Feature folders per section 2, namespace = folder per section 3.
4. `tests/<Package>.Tests` mirroring the tree, added to the coverage runsettings.
5. `README.md` from the template; row in `docs/packages.md`; topic page or
   section.
5a. If the package persists a payload, grade its reads per section 5a: a
   metadata permission and an audited `read-content` permission, never one.
6. `dotnet build -m:1`, `codecharter analyze --fail-on info`,
   `codecharter coverage --min-coverage 100`.
7. Both projects in `Bsft.sln`, and `node .github/verify-structure.mjs` exits 0
   (section 4, "The structure check").
8. Decide the tier (section 1, "Tiers: Foundation and framework"):
   `<Version>$(BsftVersion)</Version>` for a framework package,
   `<Version>$(BsftFoundationVersion)</Version>` for a Foundation one, which then
   also goes into `Bsft.Foundation.sln` with its test project.

## Renames applied in 1.3.5

The framework has no consumers that cannot be moved with it, so the naming
rules were applied in full in 1.3.5 rather than staged. Every consumer-visible
rename and namespace change of that consolidation is listed in
the 1.3.5 migration notes as an
old → new table; the compiler finds the rest.

## Renames applied in 2.0.0

2.0.0 applied [naming.md](naming.md) to the whole tree the same way: packages
(`Bsft.Audit.*` → `Bsft.Auditing.*`, `AI` → `AI`, `EfCore` → `EFCore`,
`RabbitMq` → `RabbitMQ`), namespaces, types, members, parameters and test
classes. [the 2.0.0 migration notes](migration-2.0.0.md#naming) list the renames, the
wire and persisted names that followed them and the step each needs.
