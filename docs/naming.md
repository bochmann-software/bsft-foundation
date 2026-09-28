# Naming

Binding from 2.0.0 for every identifier under `src/`, `samples/`, `tests/` and `templates/`.
It extends section 4 of `docs/package-conventions.md` and wins where the two disagree.
The rules follow Microsoft's Framework Design Guidelines (FDG). Each rule quotes its source.
A rule without a Microsoft source says so and names its precedent.
Most rules are checked mechanically; the [Checks](#checks) section says which, and where.

`FDG` = https://learn.microsoft.com/dotnet/standard/design-guidelines
`CA` = https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules

Scope. The FDG are "only required to apply them to APIs that are publicly exposed" (FDG/naming-guidelines).
This framework applies them to internal code too, so a type keeps its name when its visibility changes.

Names this document never changes:
- Names fixed by a standard (OAuth 2.0, OIDC, WebAuthn, SCIM, HTTP, RFC field names).
- Names that implement or override a Microsoft member, and names that mirror one (`GetStreamingResponseAsync` beside `IChatClient`'s).

Wire and persisted names (JSON property names, configuration keys, table and column names, enum values stored as strings, logger categories, stored type names) change only in a major release, and then they follow the C# rename: no pins that keep the previous major's name alive (N7). Names written out as contracts of their own (routes, query keys, endpoint names, table names, permission strings, error codes, log properties, configuration sections) follow the glossary in a major release as well (N7.4). 2.0.0 is such a release; [migration-2.0.0.md](migration-2.0.0.md#naming) lists every name it changed and the step each one needs.

## N1 Capitalization

- N1.1 PascalCase for namespaces, types, and every member including constants and enum values. camelCase for parameters and locals. `_camelCase` for private fields. FDG: "DO use PascalCasing for all public member, type, and namespace names consisting of multiple words." (FDG/capitalization-conventions). Fields: "Private and internal non-constant instance fields start with an underscore" (https://learn.microsoft.com/dotnet/csharp/fundamentals/coding-style/identifier-names).
- N1.2 A two-letter acronym is all caps (`AI`, `UI`, `IP`, `EF`, `IO`, `MQ`). Longer acronyms are Pascal (`Http`, `Json`, `Oidc`, `Scim`, `Jwt`). In camelCase an initial acronym is all lower case (`aiClient`, `ipAddress`). FDG: "A special case is made for two-letter acronyms in which both letters are capitalized" (`IOStream`, `ioStream`) (FDG/capitalization-conventions).
  - `Id` and `Ok` are not acronyms and stay `Id` and `Ok`.
  - Product names keep their own spelling: `EFCore` from `EF`; `RabbitMQ`, `SignalR` and `OAuth` as the vendor writes them.
- N1.3 No underscores in identifiers. Test method names are the one exception (N8). CA1707: "By convention, identifier names do not contain the underscore (_) character." It also says "it's safe to suppress this warning for test code" (CA/ca1707).
- N1.4 Closed compounds are written as the FDG table writes them: `Canceled`, `UserName`, `FileName`, `WhiteSpace`, `SignIn`/`SignOut`, `Endpoint`, `Callback`, `Metadata`, `Writable`, `Id`, `Ok`. FDG: "DO NOT capitalize each word in so-called closed-form compound words." (FDG/capitalization-conventions#capitalizing-compound-words-and-common-terms).
  - `Login`/`Logout` stay one word where they name the OIDC/Identity pages and flows (`LoginPath`, `EndSession`/`Logout`).
  - `SignIn`/`SignOut` name the cookie-session operations (`HttpContext.SignInAsync`). `LogIn` and `LogOut` as two words are banned.
- N1.5 American English spelling (`Behavior`, `Canceler`, `Canceling`, `Honored`, `Labeled`, `Normalize`, `Synthesize`, `Serializer`). No quotable FDG rule states this. The precedent is the BCL spelling itself (`CancellationToken.IsCancellationRequested`, `Canceled`, `Behavior` in `Microsoft.Extensions.*`).
- N1.6 Names do not differ by case alone. FDG: "Names cannot differ by case alone." (FDG/capitalization-conventions#case-sensitivity).

## N2 Word choice

- N2.1 No abbreviations or contractions. FDG: "DO NOT use abbreviations or contractions as part of identifier names." (FDG/general-naming-conventions#using-abbreviations-and-acronyms).
  - Banned, with the replacement: `Org`→`Organization`, `Config`/`Cfg`→`Configuration`, `Dedup`→`Deduplication`, `Seq`→`Sequence`, `Ref`→`Reference`, `Stats`→`Statistics`, `Dest`→`Destination`, `Src`→`Source`, `Param`→`Parameter`, `Arg`/`Args`→`Argument`/`Arguments`, `Expr`→`Expression`, `Ctor`→`Constructor`, `Dir`→`Directory` or `Direction`, by meaning, `Cred`→`Credential`, `Msg`→`Message`, `Ctx`→`Context`, `Mgr`→`Manager`, `Impl`→(see N4.2), `Spec`→`Specification` or `Request` (N4.6).
  - Allowed, because they are widely accepted or appear in .NET's own API: `Id`, `Ok`, `Info`, `Admin`, `Min`, `Max`, `Utc`, `Ttl`, `Db` (`DbContext`), `Dto`, `Auth` (package and namespace segment only, J03), `Kib`/`Mib`, `Jwt`, `Http`, `Json`, `Oidc`, `Scim`, `Totp`, `Mfa`, `Sms`, `Url`/`Uri`, `Api`.
  - C# identifier guidance: "Avoid using abbreviations or acronyms in names, except for widely known and accepted abbreviations." (https://learn.microsoft.com/dotnet/csharp/fundamentals/coding-style/identifier-names).
- N2.2 Name by meaning, not by type. No Hungarian notation. FDG: "DO NOT use Hungarian notation." (FDG/general-naming-conventions#word-choice). Also: "CONSIDER using names based on a parameter's meaning rather than the parameter's type." (FDG/naming-parameters).
- N2.3 A name fixed by an external standard or a Microsoft base type is kept verbatim: `WebAuthn`, `p256dh`, `acr`, `amr`, `alg`, `ack`/`nack`, `IKeyRing`, `IFeatureManager`. The glossary lists them under "fixed".
- N2.4 One term per concept, across the whole framework. The glossary below is the vocabulary. A banned synonym in any identifier is a finding. FDG: "DO choose easily readable identifier names." (FDG/general-naming-conventions#word-choice).
- N2.5 No two public types share a simple name within the framework, and no type uses a generic name (`Node`, `Log`, `Message`, `Item`) without a qualifier. FDG: "DO NOT assign type names that would conflict with other types within a single technology." and "DO NOT introduce generic type names such as `Element`, `Node`, `Log`, and `Message`." (FDG/names-of-namespaces#namespaces-and-type-name-conflicts).
  - Provider packages prefix their provider (`PostgresCreateFullTextIndexRequest`).
  - A type several packages need is declared once, in the lowest package all of them already reach, not copied into each: `RequiredPermissionsMetadata` lives in `Bsft.Http`, which the fifteen HTTP packages that attach it reference without it adding a dependency.
  - A satellite that would need a second type of a family's name uses the family's type instead (2.0.0 removed Billing's `SandboxTenant` copies in favour of `Bsft.Data`'s).

## N3 Namespaces and feature folders

- N3.1 Namespace = folder (ADR-0013, IDE0130). Folders are feature folders. The role folder names of package-conventions section 2 stay banned. FDG: "`<Company>.(<Product>|<Technology>)[.<Feature>][.<Subnamespace>]`" (FDG/names-of-namespaces).
- N3.2 A folder that groups peers of one kind is plural (`Providers`, `Sandboxes`, `Sagas`, `Imports`, `HealthChecks`). FDG: "CONSIDER using plural namespace names where appropriate." (FDG/names-of-namespaces). Brand names, acronyms and mass nouns stay singular: "use `System.IO` instead of `System.IOs`" (same page). Mass-noun examples: `Security`, `Resilience`, `Storage`.
- N3.3 A folder that names an activity or cross-cutting behavior is a gerund, or a -tion noun where English has one: `Auditing`, `RateLimiting`, `Logging`, `Caching`, `Scheduling`, `Messaging`, `MultiTenancy`, `Execution`, `Reading`, `Localization`, `StartupValidation`.
  - This is a derived rule, not a quoted one. Microsoft ships `Microsoft.Extensions.Logging` and `Caching`, `System.Diagnostics.Tracing` and `Microsoft.AspNetCore.RateLimiting`. There is no `.Log`, `.Cache`, `.Trace` or `.Audit` namespace.
  - The bare noun (`AuditLog`, `Cache`, `RateLimiter`) names the types inside.
- N3.4 No namespace segment equals a type name inside it, and no type name equals a namespace segment that the framework references. FDG: "DO NOT use the same name for a namespace and a type in that namespace." (FDG/names-of-namespaces). CA1724: "User-created type names should not match the names of referenced namespaces" (CA/ca1724).
- N3.5 A feature folder uses the same word as the types it contains (`Members` for `ChatMember*`, not `Membership`).
- N3.6 Package ids and assembly names follow the namespace root, including N1.2 (`Bsft.AI`, `Bsft.Auditing`, `*.EFCore`). FDG: "name the DLL based on the common prefix of the namespaces contained in the assembly." (FDG/names-of-assemblies-and-dlls).

## N4 Types

- N4.1 Classes, structs and records are nouns. Interfaces start with `I`, and a standard implementation differs only by the `I`. FDG: "DO ensure that the names differ only by the "I" prefix on the interface name when you are defining a class–interface pair where the class is a standard implementation" (FDG/names-of-classes-structs-and-interfaces).
- N4.2 Reserved suffixes are used only for their base type: `Attribute`, `Collection`, `Dictionary`, `EventArgs`, `EventHandler`, `Exception`, `Permission`, `Queue`, `Stack`, `Stream` (CA/ca1711: "Other type names should not use these reserved suffixes."). `Delegate`, `Enum`, `Ex`, `Flag`/`Flags`, `Impl` and `New` are never used. Documented exceptions: the domain `Permission` types (`IPermission` is obsolete CAS), `IWorkQueue`, `IIntegrationEventHandler<>` and `IChatGenerationStream`, whose base types are unrelated to the reserved suffix.
- N4.3 Enums are singular, and flags enums are plural. No `Enum` or `Flags` suffix, and no value prefix. Simple enums have a zero value, usually `None`. FDG: "DO use a singular type name for an enumeration unless its values are bit fields." (FDG/names-of-classes-structs-and-interfaces#naming-enumerations).
- N4.4 A derived type ends with its base name where that reads well (`...Exception`, `...Attribute`, `...Options`, `...Handler`). FDG: "CONSIDER ending the name of derived classes with the name of the base class." (same page).
- N4.5 Role suffixes. Package-conventions section 4 lists some; this rule extends the list. Each role has exactly one suffix, and no family uses two suffixes for the same role:

  | Suffix | Role |
  | --- | --- |
  | `Store` | persists and loads |
  | `Reader` | answers questions from stored data |
  | `Lookup` | resolves one key to one value |
  | `Writer` | writes a projection or a file |
  | `Service` | orchestrates a use case |
  | `Provider` | supplies a value or instance on demand |
  | `Factory` | builds a new instance per call |
  | `Resolver` | picks among candidates |
  | `Validator` | implements `IValidateOptions<T>` or validates a value |
  | `StartupValidator` | an `IStartupValidator` that checks wiring at startup |
  | `Builder` | fluent configuration |

  - `Manager` is not a role. The one exception is `IFeatureManager`, which mirrors `Microsoft.FeatureManagement`.
  - `Helper`, `Util` and `Utils` are banned.
- N4.6 Data-shape suffixes:

  | Suffix | Use |
  | --- | --- |
  | `Command`, `Query`, `Notification` | mediator messages only |
  | `Request` | an argument shape not dispatched through the mediator |
  | `QueryParameters` | an HTTP query-string binder (`[AsParameters]`) |
  | `Result` / `Response` | outputs |
  | `View`, `Summary`, `Info`, `Snapshot`, `Item`, `Page` | projections |
  | `Filter`, `Order` | list controls |
  | `Descriptor` | a static description |
  | `Specification` | a declarative description that code interprets |

  - `Details` is banned (use `Info`), and so is `Spec`.
  - A question-answering service is an `I*Reader`, never an `I*QueryService`.
- N4.7 Hosted services (`BackgroundService`, `IHostedService`, `IHostedLifecycleService`) are named for what they do, with a role noun:

  | Suffix | Use |
  | --- | --- |
  | `Worker` | a loop that does work |
  | `Sweeper` | a loop that deletes expired rows (purges) |
  | `Bootstrapper` | runs once at start |
  | `Seeder` | writes initial rows once at start |
  | `StartupValidator` | checks wiring at start and refuses to start |
  | `Dispatcher` | drains an outbox or a timeout table and hands each entry on |
  | `Subscriber` | listens on the event aggregator or a message bus |
  | `Publisher` | announces a capability or state once |
  | `Driver` | drives runs forward (agent runs) |
  | `HostedService` | a hosted adapter around a service of the same stem (`TranslationCacheInvalidatorHostedService`) |

  - A hosted service is never named `*Job`, nor `*Service` unless it is a `*HostedService`: `Service` is the orchestration role (N4.5) and `Job` is a scheduled `IJob`. The precedent is the .NET Worker Service template's `Worker : BackgroundService` (https://learn.microsoft.com/dotnet/core/extensions/workers).
  - A few other role nouns name one hosted service each (`Bridge`, `Watcher`, `Flusher`, `Coordinator`, `Host`); a new one is reviewed, not banned.
- N4.8 Event contracts (integration events, domain events, audit events) end in `Event`, and their stem is in the past tense (`CredentialChangedEvent`). A record that is one case of a result union is not an event and gets no suffix. There is no FDG rule for message types. The precedent is the in-tree majority (58 of 78) and FDG's past-tense rule for events: "DO give events names with a concept of before and after, using the present and past tenses." (FDG/names-of-type-members#names-of-events).

## N5 Members

- N5.1 Methods are verbs, properties are nouns or adjectives, and collection properties are plural. FDG: "DO give methods names that are verbs or verb phrases." and "DO name collection properties with a plural phrase" (FDG/names-of-type-members).
- N5.2 A method that returns an awaitable, or `IAsyncEnumerable<T>`, ends in `Async`. A synchronous method never does. Combinators (`WithRetry`, `WhenAll`) are exempt. TAP: "Use the `Async` suffix for methods that return awaitable types." and "Don't append `Async` to synchronous methods." (https://learn.microsoft.com/dotnet/standard/asynchronous-programming-patterns/task-based-asynchronous-pattern-tap).
- N5.3 `Try*` returns `bool` and hands its result out through an `out` parameter. A method that returns null on failure is named `*OrDefault` or `Find*`, not `Try*`. FDG: "DO use the prefix "Try" and Boolean return type for methods implementing this pattern." (FDG/exceptions-and-performance#try-parse-pattern).
- N5.4 Boolean properties are affirmative. `Is`/`Can`/`Has` is used only where it adds meaning. FDG: "DO name Boolean properties with an affirmative phrase" (FDG/names-of-type-members#names-of-properties).
- N5.5 Events are present or past tense, never `Before*` or `After*` (FDG/names-of-type-members#names-of-events).
- N5.6 Retrieval verbs:

  | Verb | Returns |
  | --- | --- |
  | `Get` | exactly one value, and throws or is total when there is none |
  | `Find` | one value or `null` |
  | `List` | a sequence or page |
  | `Count` / `Exists` | a number / a boolean |

  - A dictionary or set that is one value keeps `Get`. `Read`/`Write` are kept for I/O (streams, files).
  - A path or chain is one value that happens to be a sequence, and keeps `Get`: `IHierarchyReader.GetChainAsync`, `ISettingNodeAncestry.GetChainAsync`, `IOrganizationUnitPathSource.GetPathAsync`. The order of its elements is the value.
  - The `Find` precedent is EF Core's `DbSet.Find`: "If no entity is found, then null is returned." (https://learn.microsoft.com/dotnet/api/microsoft.entityframeworkcore.dbset-1.find).
  - The `List` precedent is Microsoft's Azure SDK .NET guidelines (Get/List/Create/Delete), linked from https://learn.microsoft.com/dotnet/azure/sdk/protocol-convenience-methods.
- N5.7 Lifecycle verbs:

  | Verb | Meaning |
  | --- | --- |
  | `Create` | a new entity |
  | `Add` | into a collection or association |
  | `Update` | an existing entity |
  | `Delete` | destroys an entity |
  | `Remove` | detaches an association or a cache entry |
  | `Purge` | deletes expired rows in bulk |
  | `Revoke` | invalidates a credential or grant |

  - `Prune` and `Clean` are banned.
  - The precedent is ASP.NET Core Identity, where `UserManager.DeleteAsync` sits beside `UserManager.RemoveFromRoleAsync` (https://learn.microsoft.com/dotnet/api/microsoft.aspnetcore.identity.usermanager-1).
- N5.8 DI and pipeline entry points (package-conventions section 4, unchanged): `AddBsft<Feature>`, `UseBsft<Feature>`, `MapBsft<Feature>`. Classes are `<Feature>ServiceCollectionExtensions`, `<Feature>ApplicationBuilderExtensions` and `<Feature>EndpointRouteBuilderExtensions`, in `<Package>.DependencyInjection`, never in `Microsoft.Extensions.DependencyInjection`. Options library guidance: "DO NOT use the `Microsoft.Extensions.DependencyInjection` namespace for non-official Microsoft packages." (https://learn.microsoft.com/dotnet/core/extensions/options-library-authors#namespace-guidance).

## N6 Parameters

- N6.1 Canonical parameter names for framework types. FDG: "DO use descriptive parameter names." (FDG/naming-parameters). TAP overload shape: `MethodNameAsync(…, CancellationToken cancellationToken)` (TAP page).

  | Type | Name | Banned |
  | --- | --- | --- |
  | `CancellationToken` | `cancellationToken` | `ct`, `token`, `cancel` |
  | `IConfiguration` | `configuration` | `config`, `cfg` |
  | `TimeProvider` | `timeProvider` | `time`, `clock` |
  | `ClaimsPrincipal` | `principal` | `user` |
  | `HttpContext` | `httpContext` | `http`, `ctx` |
  | `IServiceCollection` | `services` | |
  | `IServiceProvider` | `serviceProvider` (`services` inside a factory lambda) | `sp` |
  | `Action<TOptions>` | `configure` | |

  - Lambda parameters are exempt, except `sp`.
- N6.2 An override or implementation uses the parameter names of its base declaration. CA1725: "A parameter name in a derived method that differs from the name in the base declaration can cause confusion" (CA/ca1725).

## N7 Wire and persisted names

- N7.1 In a major release a wire or persisted name follows its C# name. The framework does not carry the previous major's names forward: no `[JsonStringEnumMemberName]`, `[JsonPropertyName]`, `HasColumnName`/`ToTable` or explicit event, queue or job name whose only purpose is to keep an old name. The migration guide lists each changed name with the step it needs (drain a queue or outbox, an `UPDATE`, an EF migration, a configuration key).
- N7.2 In a minor or patch release a rename must not change a wire or persisted name. Where a C# rename is needed there, the old name is written out explicitly (`[JsonPropertyName]`, `[FromQuery(Name = ...)]`, `ToTable`/`HasColumnName`, `[JsonStringEnumMemberName]`, an explicit job name). Options: "The key of `GetSection` does not have to match the name of the type that represents it." (https://learn.microsoft.com/dotnet/core/extensions/options#bind-hierarchical-configuration).
- N7.3 A name that is a contract in its own right rather than derived from a C# name (a route, a query-string key, an endpoint name, a table name, a permission string, an error code, a log property, a telemetry source, a configuration section) is written out explicitly anyway. In a minor or patch release renaming it is a decision of its own, not a side effect of a C# rename.
- N7.4 In a major release a written-out contract follows the glossary like any identifier: its words are the canonical terms (N2), with no banned abbreviation, spelling or acronym form (N1), in the casing its kind uses. Endpoint names, table and index names, log properties, audit entity types and actions are PascalCase; route parameters and query-string keys are camelCase; route segments and permission strings are lowercase kebab-case (`/api/organization-units`, `organization-units.manage`). A lowercase word writes an acronym in lowercase, so `/api/ai/` and `bsft.ai.*` already conform. 2.0.0 applied N7.4 to every written-out contract; [migration-2.0.0.md](migration-2.0.0.md#wire-and-persisted-names) lists each one it changed with the step its consumers take.

## N8 Tests

- N8.1 A test class is `<TypeUnderTest>Tests`, or `<TypeUnderTest><Aspect>Tests` when one type's tests are split by aspect (`OrganizationEFCoreServiceCollectionExtensionsLifetimeTests`), or `<Feature>Tests` for a test that exercises several types together. It has no underscore. A test method is `<Member>_<Scenario>_<Expectation>`, which is the only place underscores are allowed (CA1707 test exemption). Fakes are `Fake<Seam>`, and in-memory doubles are `InMemory<Seam>`.

## Glossary

"Banned" terms are findings in any identifier; the ones the checks see are listed under [Checks](#checks). "Fixed" terms are exempt from N1/N2 because a standard or Microsoft defines them.

| Canonical term | Meaning | Banned synonyms | Examples |
| --- | --- | --- | --- |
| Tenant | The isolation boundary; every row carries `TenantId`. | Customer, Account (as tenant), Workspace | `ITenantContext`, `TenantId` |
| Organization | The org tree and RBAC inside a tenant: units, memberships, roles. | Org | `OrganizationUnit`, `OrganizationMembership`, `IOrganizationService` |
| OrganizationUnit | A node of the organization tree. | OrgUnit, Department, Node (alone) | `OrganizationUnitId` |
| Partition | A fixed partition key of a store, not a tenant. | Shard, Bucket | chat `Partition` |
| Principal | The authenticated `ClaimsPrincipal` of a call. | User (for a principal) | `principal` parameter |
| User | A stored user record (`Bsft.Auth.Users`). | Account (for a user), Member (outside memberships) | `ICurrentUser`, `UserId` |
| Account | The self-service surface of a user's own credentials and factors. | Profile | `AccountFactorActionService` |
| Actor | Who performed an action, as recorded in audit and chat. | Performer, Initiator | `ChatActor` |
| Subject | The OIDC `sub` of a token. | fixed | `SubjectId` |
| Caller | Whoever invokes an endpoint, before authentication has classified it. | Requester | `AccountCaller`, `AgentWakeUpCallerInfo` |
| Member | Someone belonging to a chat, a unit or a role. | Participant | `ChatMember`, `Members` folder |
| Membership | The association record itself. | | `OrganizationMembership` |
| Credential | A password, passkey or external secret of a user. | Cred, Secret (for a user credential) | `ICredentialService` |
| Auditing | The activity and the namespace. The artifact is `AuditLog`/`AuditEntry`. | Audit (as folder or namespace) | `Bsft.Auditing`, `Billing/Auditing` |
| RateLimiting | Limiting how many requests a caller may make. Types are `*RateLimiter` and `RateLimitResult`. | Throttling (for request rate limiting) | `IDeviceEndpointRateLimiter` |
| Throttle | Slowing or coalescing work the framework itself sends or emits: a provider's back-off (`ConnectorThrottledException`, `SmsThrottledException`), typing indicators coalesced per member (`ChatTypingThrottle`). Kept because it is the provider's own word and not a rate limit on a caller. | | `IConnectorThrottleGate` |
| Retention | Keeping rows for a time. Deletion of expired rows is `Purge`, and the loop is a `Sweeper`. | Pruning, Prune, Cleanup | `IRetentionPass.PurgeBatchAsync`, `InboxSweeper` (was `InboxPruner`) |
| Execution | Running a unit of work (skills, agents). | Running, Run (as folder) | `Skills/Execution` |
| Runner / Executor | Runner drives work to settlement. Executor invokes one unit. These are two concepts. | | `JobRunner`, `JobExecutor` |
| WorkItem | A human task in `Bsft.Workflow`. | Task (for a human task) | `IWorkItemStore` |
| WorkQueueItem | A leased entry of the `Bsft.Jobs` work queue. | WorkItem (in Jobs) | `WorkQueueItem<TPayload>` |
| Job | A scheduled `IJob` (`Bsft.Scheduling`) or a transfer job (`Bsft.DataTransfer`). Never a hosted service. | | `IJob`, `TransferJob` |
| Worker | A hosted loop. | Service/Job as hosted-service suffix | `IdleShutdownWorker` |
| Validator | An options validator or a value validator. `StartupValidator` checks wiring at startup. | Validation (as type suffix), StartupCheck, Checker | `BsftChatOptionsValidator`, `MediatorStartupValidator` |
| Request | An argument shape. | Spec, Query (non-mediator), Args, Params | `ListApiKeysRequest` |
| QueryParameters | An HTTP query-string binder. | Query (non-mediator) | `AuditLogListQueryParameters` |
| Specification | A declarative description interpreted by code. | Spec | `SkillBindingSpecification` |
| Info | A small read projection. | Details | `ChatInboxInfo` |
| Event | An integration or domain event contract. Past-tense stem. | (no suffix) | `CredentialChangedEvent` |
| Configuration | Configuration values or a configuration object. | Config, Cfg, Settings (for configuration)¹ | `SpeechProviderConfiguration` |
| Deduplication | Idempotent-receive bookkeeping. | Dedup, Dedupe | `InboundWebhookDeduplicationSweeper` |
| Sequence | A monotonically increasing position. | Seq | `Sequence`, `LastReadSequence` |
| Management / Administration | Management = resource lifecycle. Administration = an administrator acting on others, paired with `SelfService`. Two concepts. | Admin (as folder) | `Management/`, `Administration/` |
| Scoping / Scopes | Scoping = tenant or owner scoping of data. Scopes = OAuth scopes (fixed). Two concepts. | | `Scoping/`, `Scopes/` |
| Health / HealthChecks | Health = liveness state. HealthChecks = `IHealthCheck` implementations. Two concepts. | | |
| Canceled / Canceling / Canceler | Cancellation state. | Cancelled, Cancelling, Canceller | `SubscriptionStatus.Canceled`, `TransferJobStatus.Canceled` |
| SignIn / Login | SignIn = cookie session operation. Login = OIDC/Identity page and flow. | LogIn, LogOut, Signin | `SignInAsync`, `LoginPath` |
| AI | The AI package family and token. | Ai | `Bsft.AI`, `AIDefaults` |
| EFCore | EF Core provider packages and types. | EfCore, Ef | `Bsft.Chat.EFCore`, `EFCoreSagaStore` |

¹ `Settings` stays as the name of `Bsft.Settings` (stored runtime settings, a distinct concept from configuration).

Fixed (standards and Microsoft names, never renamed): `OAuth`, `Oidc`, `WebAuthn`, `Scim`, `Jwt`, `Totp`, `p256dh`, `acr`, `amr`, `alg`, `sub`, `ack`/`nack`, `IKeyRing`, `IFeatureManager`, `KeyRing`, `Usernameless` (the WebAuthn usernameless ceremony), `RabbitMQ`, `SignalR`.

## Feature-folder vocabulary

A feature folder takes the canonical form in this table. A new folder whose concept is listed must use the form given here.

| Canonical | Replaces | Class (N3) |
| --- | --- | --- |
| Auditing | Audit | activity |
| RateLimiting | Throttling | activity |
| Reading | Querying | activity |
| Execution | Running | activity (-tion) |
| MultiTenancy | Tenancy | activity (matches `Bsft.Data/MultiTenancy`) |
| StartupValidation | StartupChecks | activity (-tion) |
| ValueValidation | Validating | activity (-tion; `Validation` alone is a banned role folder) |
| PasswordReset | Reset | domain noun (bare verb is ambiguous) |
| Imports | Import | family of import sources |
| Providers | Provider | family |
| Sandboxes | Sandbox | family |
| Members | Membership (Chat.AI) | same word as the types |
| Retention, Persistence, Management, Administration, SelfService, Scoping, Scopes, Health, HealthChecks, Sagas, Retry | (unchanged) | already canonical |

## Checks

`node .github/verify-structure.mjs` (the `structure` job of CI) checks the rules below over the
whole repository on every run, beside the structure checks of package-conventions section 4. Each
finding names the rule id and quotes the sentence of this document it breaks. The checks read
project names, folders, top-level type declarations, for N6.1 method parameters, and for N7.4 the
string literals that hold a written-out contract; they do not read member names, so N5 and the member
half of N2 remain review items, checked once by the 2.0.0 rename and by review since. The self-test (`node --test '.github/structure/*.test.mjs'`) has a
case that fires and a case that must not fire for each rule.

| Rule id | Rule | Scope |
| --- | --- | --- |
| `name-acronym` | N1.2: no `Ai`, `Ui`, `Ip`, `Ef`, `Mq`, `Io` token | project names, folders, top-level types in `src/`, `samples/`, `tests/` |
| `name-abbreviation` | N2.1: none of the banned abbreviations as a token (`EventArgs` is the BCL's and exempt) | as above |
| `name-spelling` | N1.4, N1.5: `Cancelled`, `Canceller`, `Username`, `Whitespace`, `Signin`, `Behaviour`, `LogIn`/`LogOut`, ... | as above |
| `name-type-suffix` | N4.2, N4.5, N4.6, N5.7: no `Impl`, `Helper(s)`, `Util(s)`, `Utility`, `Manager`, `StartupCheck`, `Details`, `Ex`, `New` suffix; no `Enum`/`Flag(s)` on an enum, no `Delegate` on a delegate; no `Prune` token. `IFeatureManager`, `FeatureManager` and `AuthenticatorDataFlags` (WebAuthn's name) are exempt | top-level types in `src/`, `samples/` |
| `name-options-validator` | N4.5: an `IValidateOptions<T>` implementation ends in `Validator` | as above |
| `name-hosted-service` | N4.7: a `BackgroundService`/`IHostedService`/`IHostedLifecycleService` is not `*Job`, and not `*Service` unless `*HostedService` | as above |
| `name-duplicate` | N2.5: no two public types of `src/` share a simple name and arity | public top-level types in `src/` |
| `name-namespace-type` | N3.4: no type is named like a segment of its own namespace | top-level types in `src/`, `samples/`, `tests/` |
| `name-parameter` | N6.1: the banned parameter names for `CancellationToken`, `IConfiguration`, `TimeProvider`, `ClaimsPrincipal`, `HttpContext`, `IServiceProvider` | parameters of declared methods, constructors and local functions in `src/`, `samples/` (lambdas exempt) |
| `name-test-class` | N8.1: a class declaring `[Fact]`/`[Theory]` tests ends in `Tests` and has no underscore | `tests/` |
| `name-endpoint-name` | N7.4: the literal of `WithName(...)` has no banned token | string literals in `src/`, `samples/` |
| `name-route` | N7.4: the route of a `Map*` call, of `[Route]`/`[Http*]`, and of a `const string` named `*Route`, `*RoutePrefix`, `*Prefix`, `*Path`, `*Pattern` or `*Template`: no banned word in a segment, no banned token in a `{parameter}`. Microsoft's `signin-oidc` callback paths are exempt | as above |
| `name-table` | N7.4: `ToTable(...)`, `HasDatabaseName(...)` and `const string` fields named `*TableName`/`*IndexName` | as above |
| `name-query-key` | N7.4: the `Name` of `[FromQuery]`, `[FromRoute]`, `[FromHeader]` and `[FromForm]` | as above |
| `name-log-placeholder` | N7.4: each `{Placeholder}` of a message template passed to `Log*`, `BeginScope`, `LoggerMessage.Define*` or `[LoggerMessage]` | as above |
| `name-contract-constant` | N7.4: the value of a `const string` named `*Permission`, `*ErrorCode`, `*ErrorKind`, `*SectionName`, `*ClaimType`, `*EntityType`, `*EntityName`, `*Entity`, `*AuditAction`, `*ScopeKind`, `*RouteName`, `*MetadataKey` or `*Metadata` | as above |

The N7.4 rules split a value into words at `.`, `:`, `_`, `-`, `/` and white space, and check each word
the way the token rules check a type name, with two differences: a lowercase word may write an acronym
in lowercase (`ai`), and closed lowercase forms (`orgunits`) are findings of their own. They read a
literal only where it is the direct argument of one of the calls above or the value of such a constant;
a contract that reaches its call through a variable or a helper of the package's own (the settings
administration routes are built by a private `MapLayer`) is a review item.

Names that no rule has reached yet, left for a change of their own because each is either a
member (which the checks do not read) or a call this document does not settle:

- `Prune` in private members and test names: `OperationsPerPrune` in `InMemoryTokenRedemptionStore` and the `Prune*` test methods (N5.7 would make them `Purge*`). The configuration keys that said `Prune` are `Purge*` since 2.0.0.
- `Dest` in `Bsft.Mapping`'s generic parameter `TDest` and the private `DestProp`, `Ui` in the private field `UiFields`, `Spec` in the tuple element `RangeSpec`, and `Details` in member names.
- `LeasedWorkerHostedService`, `StartupValidationHostedService` and `TranslationCacheInvalidatorHostedService` keep `HostedService`, which N4.7 allows for an adapter; whether the first two are adapters or workers is open.

Written-out contracts in 2.0.0 (N7.4): endpoint names, routes and route keys, query-string keys, table
and index names, the settings layer name, permission strings, claim types, audit entity types and
actions, log properties and configuration sections follow the glossary like the identifiers around them
(`ListOrganizationUnits`, `/api/organization-units/{organizationUnitId}`, `deduplicationKey`,
`OrganizationUnits`, `organization-units.manage`, `{ChatAIErrorCode}`, `Bsft:Auditing:EFCore`). In a
minor or patch release N7.3 holds again: renaming one of them is a decision of its own.
