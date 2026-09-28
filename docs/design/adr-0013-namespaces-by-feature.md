# ADR-0013 — Namespaces follow features, not roles

- **Status:** Accepted (2026-09-27), implemented in 2.0.0.
- **Deciders:** framework maintainers.
- **Supersedes:** sections 2 and 3 of `docs/package-conventions.md` as they stood in 1.x,
  and the `Bsft.Hosting` exception of section 4. The conventions now describe this layout.

## Context

### What was measured

A CodeCharter measurement on 2026-09-26 (`graph_cycles`, level namespaces, `Bsft.sln`)
found 74 namespace cycles with 238 of 894 namespaces in one of them. Every cycle sits
inside a single package, and none crosses a project boundary. The first attempt to
remove them (an earlier pull request on `Bsft.Auth.ApiKeys`) treated the house conventions as
given and fixed the code around them. Looking at the edges type by type showed
that most cycles are not defects in the code. The conventions produce them:

- **`Bsft.Auth.ApiKeys`:** 6 edges close the cycle. One is a real design issue
  (`ApiKeyService`, a facade that dispatches the commands, filed in `Services/`
  below the handlers it calls). The other five come from the layout.
- **`Bsft.Auth.Idp`:** 15 namespaces are in the cycle. Ignore the wiring at the
  root and the contracts that `Bsft.Auth.Idp.Abstractions` shares a namespace
  with, and **no cycle is left between implementation types**. Idp already cuts
  by feature (`Login`, `Challenge`, `Enrollment`), and that cut is sound.

So the question is not how to remove 74 cycles. It is whether the assumptions that
produce them hold up against the conventions the .NET ecosystem follows.

### The assumptions, tested against .NET guidance

| House assumption | .NET guidance and practice | Assessment |
| --- | --- | --- |
| One namespace per **role** folder: `.Services`, `.Commands`, `.Endpoints`, `.Stores`, `.Models`, `.Handlers` (sections 2 and 3). | The C# guide says to "organize namespaces by feature, not by type kind: place an interface, its implementations, and supporting types together" ([Program organization](https://learn.microsoft.com/dotnet/csharp/fundamentals/program-structure/program-organization)). The Framework Design Guidelines template is `<Company>.(<Product>\|<Technology>)[.<Feature>][.<Subnamespace>]`. | **Contradicts.** A feature such as "rotate a key" is split across four namespaces, so those namespaces necessarily depend on one another in both directions. This is the main source of the cycles. |
| The DI entry point (`AddBsft<Feature>`, `MapBsft<Feature>`) lives in the package's **root** namespace, next to the options and contracts (sections 3 and 4). | For library authors Microsoft advises: "CONSIDER a namespace that clearly identifies your package offering" and "DO NOT use the `Microsoft.Extensions.DependencyInjection` namespace for non-official Microsoft packages". The examples declare `ExampleLibrary.Extensions.DependencyInjection` ([Options pattern guidance for library authors](https://learn.microsoft.com/dotnet/core/extensions/options-library-authors)). Microsoft's own packages keep the root namespace for the API and its options (`Microsoft.Extensions.Caching.Memory.MemoryCacheOptions`) and put `AddMemoryCache` in a separate DI namespace. | **Deviates.** Our root namespace is both the top layer (wiring) and the bottom layer (options, defaults, contracts). That alone gives every package with feature namespaces a root ↔ feature cycle, with nothing wrong in the code. |
| `Options/`, `Internal/`, `Diagnostics/`, `Configurations/` and `Entities/` are **structural** folders that add no namespace segment (section 3). | The namespace matches the folder: IDE0130 ("Namespace does not match folder structure") is on by default ([IDE0130](https://learn.microsoft.com/dotnet/fundamentals/code-analysis/style-rules/ide0130)). Microsoft's packages keep an options type in the folder of its namespace rather than in an `Options/` folder. | **Deviates**, and cheap to fix: a file can move to the folder that matches its namespace without changing any namespace. |
| An `.Abstractions` package shares its namespace with the implementation (section 3, 2.0.0). | This is exactly Microsoft's practice: `Microsoft.Extensions.Logging.Abstractions` declares `Microsoft.Extensions.Logging`. | **Conforms. Keep.** |
| Every namespace cycle inside one assembly is a defect. | The Framework Design Guidelines treat namespaces as grouping for the **consumer**, and the assembly is the unit of dependency. Tools such as NDepend report cycles between namespaces, but that is one school of design, not a .NET convention. | **Holds only partly.** A cycle between **features** is a real finding. A cycle that comes from role folders or from wiring placed at the root measures the layout, not the design. |

## Pilot: `Bsft.Auth.ApiKeys` laid out by feature

The pilot is only a simulation, not a code change. It maps every one of the 79 types of
`src/Bsft.Auth.ApiKeys` (as on `main`, a71bf1d9) to a feature namespace and recomputes the
namespace graph from textual type references. It is precise enough to judge the shape; a
build and a CodeCharter run would confirm it.

### Today

```text
src/Bsft.Auth.ApiKeys/
├── ApiKeysServiceCollectionExtensions.cs   Bsft.Auth.ApiKeys
├── ApiKeysEndpointRouteBuilderExtensions.cs Bsft.Auth.ApiKeys
├── ApiKeyDefaults.cs                        Bsft.Auth.ApiKeys
├── Options/        (2)                      Bsft.Auth.ApiKeys            (structural)
├── Internal/       (6)                      Bsft.Auth.ApiKeys            (structural)
├── Abstractions/   (30)                     Bsft.Auth.ApiKeys.Abstractions
├── Commands/       (13)                     Bsft.Auth.ApiKeys.Commands
├── Services/       (21)                     Bsft.Auth.ApiKeys.Services
└── Endpoints/      (4)                      Bsft.Auth.ApiKeys.Endpoints
```

The namespaces form 1 cycle over 4 of 5 namespaces: root, `.Commands`, `.Endpoints` and
`.Services`.

### By feature

```text
src/Bsft.Auth.ApiKeys/
├── IApiKeyStore.cs, ApiKeyRecord.cs, ApiKeyStatus.cs,     Bsft.Auth.ApiKeys
│   ApiKeyScopeKind.cs, ApiKeyActor.cs, ApiKeyView.cs,       (the package's vocabulary:
│   IssuedApiKeyView.cs, ApiKeySecrets.cs, ApiKeyLimits.cs,   the store seam and the
│   ApiKeyMatch.cs, ApiKeyListRequest.cs, ApiKeyRotation.cs,  records it speaks in,
│   ApiKeyUsage.cs, ApiKeyDenialReason.cs,                    options, defaults)
│   BsftApiKeysOptions.cs, BsftApiKeysOptionsValidator.cs,
│   ApiKeyDefaults.cs
├── Management/     (32)  Bsft.Auth.ApiKeys.Management      issue, rotate, revoke, rename, list:
│                                                           IApiKeyService + facade, the commands and
│                                                           their handlers, issue policy and authorizer,
│                                                           the HTTP endpoints and their request records
├── Authentication/ (5)   Bsft.Auth.ApiKeys.Authentication  resolving a presented key on a request
├── Secrets/        (3)   Bsft.Auth.ApiKeys.Secrets         key format, minting and hashing
├── Scopes/         (4)   Bsft.Auth.ApiKeys.Scopes          scope catalog and mapping
├── RateLimiting/   (4)   Bsft.Auth.ApiKeys.RateLimiting
├── Usage/          (5)   Bsft.Auth.ApiKeys.Usage           usage recording and flushing
├── Auditing/       (6)   Bsft.Auth.ApiKeys.Auditing
└── DependencyInjection/ (3) Bsft.Auth.ApiKeys.DependencyInjection  AddBsftApiKeys, MapBsftApiKeys
```

Every folder is its namespace, and there are no structural folders.

### Result

| | Today | By feature |
| --- | --- | --- |
| Namespaces | 5 | 9 |
| Namespace edges | 12 | 21 |
| Namespaces in a cycle | 4 | **0** |

The layering follows from the layout, and no rule has to impose it:
`DependencyInjection` → `Management`, `Authentication` → `Secrets`, `Scopes`, `Usage`,
`Auditing`, `RateLimiting` → root. The root is a pure sink.

A first, naive mapping still had a cycle. It kept the store's argument records in their
feature folders and the key format inside `Management`. Resolving it is what showed the
package's real shared vocabulary: `IApiKeyStore` speaks in `ApiKeyMatch`,
`ApiKeyListRequest`, `ApiKeyRotation` and `ApiKeyUsage`, and `Management` and
`Authentication` both depend on the key format. So cycles in a feature layout point
at real design questions. Cycles in a role layout do not.

### Cost for this package

46 of 48 public types change namespace:

| From | To | Types |
| --- | --- | --- |
| `.Abstractions` | root | 14 |
| `.Abstractions` | `.Management`, `.Auditing`, `.RateLimiting`, `.Scopes`, `.Authentication`, `.Usage` | 16 |
| `.Commands`, `.Endpoints` | `.Management` | 8 |
| `.Services` | `.Auditing`, `.Management`, `.RateLimiting`, `.Scopes`, `.Usage` | 6 |
| root | `.DependencyInjection` | 2 (the host-facing `AddBsftApiKeys` and `MapBsftApiKeys`) |

In the repository, 14 files in 6 projects import an ApiKeys namespace
(`Bsft.Auth.ApiKeys.EFCore`, `.Notifications`, `.Organization`, `Bsft.Apps.ApiKeys`,
`Bsft.Auth.DeviceFlow.ApiKeys`, `Bsft.Sample.WebApi`), plus every host that calls
`AddBsftApiKeys`.

## Size across the repository

The table counts the files in each role folder under `src/`, and how many declare a public
top-level type. For those public types a namespace change is a source break:

| Folder | Files | With a public type |
| --- | --- | --- |
| `Services/` | 1591 | 319 |
| `Endpoints/` | 358 | 158 |
| `Models/` | 224 | 130 |
| `Commands/`, `Queries/` | 220 | 97 |
| `Stores/` | 238 | 76 |
| `Handlers/` | 73 | 23 |
| `Internal/` | 577 | 0 |
| `Options/` | 365 | 228 (namespace unchanged, only the file moves) |

## Decisions

Each decision could have been taken on its own. They are listed with the choice that was taken.

**D1 — Namespaces follow features.** A package's sub-namespaces name what it
does (`Management`, `Authentication`, `Login`), not what kind of type they hold. The
role vocabulary of section 2 (`Services/`, `Commands/`, `Endpoints/`, `Stores/`,
`Models/`, `Handlers/`) is dropped as a namespace layer. A large feature may still sort
its files into sub-folders, and those follow D3. *Alternative:* keep the role folders and
accept their cycles as a property of the layout. Then the cycle measurement should be
dropped as a quality signal, because it cannot be met.

**D2 — Where the DI entry point lives.** Three options:
- (a) `<Package>.DependencyInjection`, as the Microsoft guidance for library
  authors shows. The root is then a pure sink. Every host needs one more `using`, which is a
  breaking change for every consumer of every package.
- (b) Keep it in the root namespace, as today. Nothing breaks for consumers. The
  wiring edges stay as the one accepted exception to "no cycles", and they are
  recognisable by name (`*ServiceCollectionExtensions`, `*EndpointRouteBuilderExtensions`,
  `*ApplicationBuilderExtensions`).
- (c) `Microsoft.Extensions.DependencyInjection`, as many third-party libraries do. The
  Microsoft guidance says not to.

The proposal was **(b) now and (a) at the next major version**, so consumers would not
rewrite their `using` directives for a change they gain nothing from. Because the feature
namespaces themselves ship in a major version (2.0.0) that already asks every consumer to
rewrite `using` directives, **(a) was taken in the same release**: one migration instead of
two. The conventions rule `bsft-di-entry-point-namespace` checks it, and
`bsft-single-service-collection-extensions` is withdrawn, because its premise — one entry
point per feature namespace — ended when every entry point of a package moved into one
namespace.

**D3 — The namespace matches the folder, without exceptions.** The structural
folders go: options, validators and defaults sit in the folder of their namespace, which is
usually the root folder. `Internal/` goes too: an internal helper lives in the feature that
uses it, and a helper shared across features is a feature of its own (as `Secrets` is in
the pilot). IDE0130 can then enforce the layout instead of a house rule. `Entities/`
and `Configurations/` in the EfCore packages become ordinary namespaces.

**D4 — `.Abstractions` keeps sharing its namespace** with the implementation, as section 3
has said since 2.0.0. Under D1 an `.Abstractions` package uses the same feature
folders as its implementation, so a contract and its implementation meet in one
namespace. This is what a consumer expects from Microsoft's packages.

**D5 — What counts as a finding.** Given D1 to D4, a namespace cycle between two
features of one package is a finding. It is reported by `graph_cycles` and fixed in the
code, as in the pilot. With D2 (a) there is no accepted exception. The rule
`bsft-feature-not-on-wiring` from that pull request is withdrawn: it enforces the role layering this
ADR replaces.

## Consequences

- Sections 2, 3 and 4 of `docs/package-conventions.md` are rewritten. The house rule
  `bsft-namespace-follows-assembly` stays, and IDE0130 runs in the build.
- The migration was not staged by visibility as first planned. The whole tree moved in one
  release, 2.0.0, with its own migration page (`docs/migration-2.0.0.md`) and a script
  driven by the generated type map.
- Idp needs little beyond D2 and D3, because it is already cut by feature. The role-folder
  packages (ApiKeys, Webhooks, Organization, …) carry most of the work.
- That pull request is not merged as it stands. Of its changes, the move of `ApiKeyService` beside the
  commands is the only one that survives D1 unchanged (in `Management`).

## Open questions

- Is the feature cut of the pilot the one the maintainers of ApiKeys would choose? In
  particular: should `Management` split into its HTTP surface and its commands, and should
  `Scopes` and `Secrets` stay separate namespaces or merge into the root?
- Resolved: the public namespace moves and D2 (a) shipped together in 2.0.0.
