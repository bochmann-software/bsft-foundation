# CRUD conventions

A resource that a Bsft application exposes for editing does so through the same five endpoints,
with the same shapes and the same failures, every time. The point is not tidiness: a generic client
— a table bound to a resource, a form that saves it, a confirm-delete dialog — can only be written
once if it can predict the server. This page is that prediction, and
`samples/Bsft.Sample.Inventory` implements it end to end for the `products` resource.

## The five endpoints

`{resource}` is the plural resource name; the controller route is `api/[controller]`, so
`ProductsController` serves `api/products`. Matching is case-insensitive, but the `[controller]`
token keeps the controller's own casing, so the `Location` header a create returns reads
`/api/Products/{id}`. A client must treat the returned URL as opaque rather than string-compare it
against the path it requested.

| Operation | Route | Success | Response body |
| --- | --- | --- | --- |
| List | `GET /api/{resource}` | 200 | `PagedResult<TItem>` |
| Read | `GET /api/{resource}/{id}` | 200 | `TItem` |
| Create | `POST /api/{resource}` | 201 + `Location` | `{ "id": … }` |
| Update | `PUT /api/{resource}/{id}` | 200 | `TItem`, as it stands after the update |
| Delete | `DELETE /api/{resource}/{id}` | 204 | empty |

Four decisions inside that table are worth stating outright, because each has a plausible
alternative that was not chosen:

**The update answers with the updated resource, not 204.** A list screen that has just saved a row
needs to redraw it. Returning the new state costs the server a mapping it has already done and
saves the client a second round trip; a 204 would force every client to re-read the resource to
learn what its own write produced — including any field the server computed.

**The identifier lives in the route, never in the body.** A body that disagreed with the route
would leave the server to pick a winner and the client to guess which. The update request record
therefore carries the editable state only.

**`PUT` replaces the editable state; there is no `PATCH`.** The update command names every editable
field, and a field left out of the request is not "unchanged", it is empty. Partial update is a
separate convention and is not defined here — do not half-implement it by making fields nullable.

**A delete of something that is not there is a 404, not a 204.** The idempotent reading is
defensible in the abstract, but a confirm-delete dialog that reports success for a row somebody
else already removed hides a conflict the user should see.

## Status codes

Every failure is signalled by throwing, never by returning a status from the controller.
`BsftExceptionMiddleware` (in `Bsft.Hosting`) catches the exception and writes an RFC 9457
`application/problem+json` document. The table below is the middleware's actual mapping, read off
`BsftExceptionMiddleware.MapException`:

| Exception (`Bsft.Core.Exceptions`) | Status | `title` | Extra members |
| --- | --- | --- | --- |
| `ValidationException` | 400 | `Validation Error` | `errors`, `errorCode`, `correlationId` |
| `ForbiddenException` | 403 | `Forbidden` | `errorCode`, `correlationId` |
| `NotFoundException` | 404 | `Not Found` | `entityType`, `entityId`, `errorCode`, `correlationId` |
| `PaymentRequiredException` | 402 | `Payment Required` | `errorCode`, `correlationId` |
| `PreconditionFailedException` | 412 | `Precondition Failed` | `errorCode`, `correlationId` |
| `ConflictException` | 409 | `Conflict` | `errorCode`, `correlationId` |
| `PayloadTooLargeException` | 413 | `Payload Too Large` | `errorCode`, `correlationId` |
| `BusinessRuleException` | 422 | `Business Rule Violation` | `errorCode`, `correlationId` |
| `PreconditionRequiredException` | 428 | `Precondition Required` | `errorCode`, `correlationId` |
| any other `BsftException`, or any unhandled exception | 500 | `Internal Server Error` | `correlationId` |

Note the 403, which the CRUD discussion usually forgets: an authorization refusal is a
`ForbiddenException` and is part of the same contract. Note also that the 500 arm deliberately
replaces the exception message with a fixed sentence — a client cannot learn anything from a 500
except the correlation id, and that is the point.

Every document carries `type`, `title`, `status`, `detail` and the members above. `errors` is a
dictionary keyed by field name with an array of messages per field, which is directly what a form
needs to place messages next to inputs.

Every mapped arm also carries `errorCode`, the machine-readable discriminator a client branches on
within a status. `BsftException` carries an `ErrorCode` — the built-in subtypes set a stable one
(`VALIDATION_FAILED`, `FORBIDDEN`, `NOT_FOUND`, `CONFLICT`, `PRECONDITION_FAILED`,
`PRECONDITION_REQUIRED`), and `BusinessRuleException` takes its own as a second argument
(`PRODUCT_HAS_STOCK` in the sample). Two 422s, or two 409s, that a human reads apart by their
`detail` are told apart in code by their `errorCode`. Three of the built-in subtypes take a
caller-supplied code as a second argument for exactly that reason — `ConflictException`,
`PreconditionRequiredException` and `PayloadTooLargeException` — because a family with more than one
refusal at a status has to be able to name each of them; `Bsft.Idempotency` is the first user, and
publishes `IDEMPOTENCY_KEY_REUSED` and `IDEMPOTENCY_IN_PROGRESS` within one 409. The one exception is the 500 arm, which is
deliberately opaque: an unmapped framework error publishes no code, only the correlation id, so an
internal error taxonomy never leaks to the client.

### Which refusal is which

The three client-fault codes are easy to blur, so the split is fixed:

- **400** — the request is malformed *on its own terms*: a missing name, a negative quantity, a
  price of zero. Decidable without looking at anything else in the database.
- **409** — the request is well-formed but collides with another row: a name that is already taken.
- **422** — the request is well-formed and collides with nothing, but the resource's own state
  forbids it: deleting a product that still holds stock.

A client can act on that split. A 400 belongs next to a field, a 409 usually means "reload and look
again", and a 422 is a sentence for the user about the thing itself.

**402 is the fourth, and it is about the caller rather than the request.** A quota refusal or a
missing entitlement is not a defect in the request and not a collision with anything: the identical
request succeeds on a larger plan, or after the period rolls over. `Bsft.Billing` is its user, with
`QUOTA_EXCEEDED` and `ENTITLEMENT_MISSING`. The reason it is not folded into 422 is what a client
does with it — a 422 is a sentence for the user about the thing itself, while a 402 is the one status
a client can turn into an upgrade path without parsing prose.

## Filtering: the `filter` query parameter

The list endpoint binds `[FromQuery] PagedRequest`. ASP.NET's own query binding reaches everything on
that request except one thing: `Page`, `PageSize`, `Sort[i].Field`/`Sort[i].Direction` and `Search.Group`/
`Search.Term` all bind from their own query keys, but `PagedRequest.Filter` is a **polymorphic
`FilterNode` AST** (`Bsft.Data.Filtering`) that query keys cannot express. A generic client — a table
bound to a resource, the UI framework's `CrudApiClient` — therefore has a transport for paging,
sorting and search but none for the filter, and that is the gap this section closes.

**The filter travels as one query parameter carrying the JSON-serialized AST:**
`GET /api/{resource}?filter=<url-encoded JSON>`. The decision to keep it a query parameter on `GET`,
rather than move the list to a `POST` body, is deliberate: the list stays a `GET`, which keeps it
idempotent and cacheable and keeps the whole list convention one shape. A custom model binder
(`FilterNodeModelBinder`, registered by `AddBsftDefaults`) deserializes the `filter` value over the
AST's own `[JsonDerivedType]` vocabulary and hangs the result on `PagedRequest.Filter`; the controller
signature does not change and `request.Filter` is simply populated.

### The AST vocabulary

The wire format is the polymorphic AST, discriminated by `$type`:

| `$type` | Shape | Meaning |
| --- | --- | --- |
| `comparison` | `{ "$type": "comparison", "field": <name>, "op": <operator>, "value": <json> }` | one field compared to a value |
| `present` | `{ "$type": "present", "field": <name> }` | the field has a value (is not null) — unary, no value |
| `and` | `{ "$type": "and", "and": [ <node>, … ] }` | all child nodes must hold |
| `or` | `{ "$type": "or", "or": [ <node>, … ] }` | any child node holds |

`op` is one of `eq`, `neq`, `gt`, `gte`, `lt`, `lte`, `contains`, `!contains`, `startsWith`,
`!startsWith`, `endsWith`, `!endsWith`. Presence (`pr`) is not an `op` on a `comparison`; because it
compares against nothing it is its own unary `present` node, so a comparison's `value` never has to be
faked. A minimal example, before url-encoding:

```json
{ "$type": "and", "and": [
    { "$type": "comparison", "field": "name",  "op": "contains", "value": "widget" },
    { "$type": "present",    "field": "name" },
    { "$type": "comparison", "field": "price", "op": "lte",      "value": 9.99 }
]}
```

### Where the operator vocabulary comes from

The operator set is a documented choice, not an accident. There is no single RFC a REST filter language
must obey, so the vocabulary borrows deliberately:

- **Operator set and semantics: SCIM (RFC 7644 §3.4.2.2).** SCIM is the only IETF RFC with a formal
  filter grammar, and its operator set is the reference this vocabulary is audited against — the
  comparison, string and presence operators, plus `and`/`or` combination. It is referenced for the
  *operators and their meaning*, not its wire format: SCIM filters are a string expression language
  (`userName sw "J"`), whereas this transport is the JSON AST above.
- **`filter` as the reserved query-param name: JSON:API.** JSON:API standardizes only that `filter`
  is the reserved parameter for filtering, which is exactly how this convention
  transports the AST — the JSON grammar inside it is left to the application, and this is that grammar.
- **OData `$filter` (OASIS): deliberately not adopted.** OData specifies a full expression language
  with functions, arithmetic and lambda operators. That is far more surface than a whitelist-checked
  list convention needs, and reimplementing an expression parser would re-open exactly the injection
  surface the JSON AST closes. It is rejected on purpose.
- **The JSON-AST family (MongoDB query operators, JsonLogic, GraphQL `where` inputs).** A discriminated
  JSON tree of typed nodes is the shape this family shares; `FilterNode` sits squarely in it.

| Node / `op` | SCIM operator | Meaning | Applies to |
| --- | --- | --- | --- |
| `eq` | `eq` | equals | any field |
| `neq` | `ne` | not equals | any field |
| `gt` | `gt` | greater than | comparable field |
| `gte` | `ge` | greater than or equal | comparable field |
| `lt` | `lt` | less than | comparable field |
| `lte` | `le` | less than or equal | comparable field |
| `contains` | `co` | substring match | string field only |
| `!contains` | (negated `co`) | not a substring | string field only |
| `startsWith` | `sw` | prefix match | string field only |
| `!startsWith` | (negated `sw`) | not a prefix | string field only |
| `endsWith` | `ew` | suffix match | string field only |
| `!endsWith` | (negated `ew`) | not a suffix | string field only |
| `present` node | `pr` | field has a value (not null) | any field |
| `and` / `or` node | `and` / `or` | logical combination | — |

SCIM's `not` has no dedicated node: single-condition negation is expressed by the negated operators
(`neq`, `!contains`, `!startsWith`, `!endsWith`), which keeps the AST a positive tree the whitelist
compiler can reason about without a general negation wrapper.

The string operators (`contains`, `startsWith`, `endsWith` and their negations) are honored **only
against string-typed whitelisted fields**. A string operator aimed at a non-string field is a **400**
keyed on `filter`, not a `500` — the type mismatch is caught before the expression is built. The value
a client sends is always data, never a `LIKE` pattern: the compiler emits `string.Contains` /
`StartsWith` / `EndsWith`, which EF Core translates to a parameterized, wildcard-escaped `LIKE`, so a
`%` or `_` in the value matches literally and can never become an injected wildcard.

### The filter is client-controlled, so it is bounded and whitelisted

The `filter` value is attacker-reachable input, and it is treated as such on three axes, none of which
ever produces a `500`:

- **Whitelist, not reflection.** The AST is compiled only against the resource's `QueryFieldMap` — the
  fields and operators a `QueryConfigurationBase<TEntity, TDto>` declares (for `products`,
  `ProductQueryConfiguration`). A field that is not declared, or an operator not allowed on it, is rejected by
  the compiler as a `ValidationException` → **400** keyed on `filter`. There is no path from a filter
  to an arbitrary property or expression: an undeclared name never resolves to anything.
- **Size bounds against denial of service.** `BsftFilterTransportOptions` caps the raw `filter`
  length (`MaxFilterLength`, default 8192 characters), the AST's nesting depth (`MaxDepth`, default 24)
  and its total node count (`MaxNodeCount`, default 200), and hands the JSON reader a hard recursion
  limit (`MaxJsonReaderDepth`, default 64). A filter that breaches any of these is a **400** raised
  *before* the AST is compiled — an oversized or deeply nested payload never reaches the expression
  builder, and a payload nested past the reader limit is a clean 400 rather than a stack overflow.
  The options are validated eagerly at startup and configurable through
  `AddBsftDefaults(d => d.FilterTransport(…))`.
- **Malformed JSON is a 400, not a 500.** A `filter` that is not well-formed JSON over this vocabulary
  — a broken document, an unknown `$type`, a missing required member — is a `ValidationException` →
  **400** through `BsftExceptionMiddleware`, the same problem+json shape as any other validation
  failure. The parser echoes no fragment of the input back.

### The honest limit, and the fallback that is named but not built

A `GET` carries the filter in the URL, and a URL has a length ceiling: proxies and servers commonly
refuse a request line beyond ~8 KB, which is why `MaxFilterLength` defaults there rather than pretending
the ceiling is not real. For all ordinary filters — a handful of conditions a user builds in a table —
this is ample. A host that genuinely needs filters larger than a URL can hold wants a **`POST`-based
query variant** (`POST /api/{resource}/query` with the `PagedRequest` in the body), which trades the
idempotent, cacheable `GET` for unbounded body size. That variant is **named here as the documented
escape hatch but is deliberately not implemented in v1**: it is a second transport for the same
operation, and adding it before a host actually needs it would fork the convention and the generic
client for a case none of the samples hit. It stays a documented extension point until a real host
justifies it.

## Behind the endpoint

The controller does no work beyond translating HTTP into a command or query and back:

```
PUT /api/products/{id}  →  UpdateProductCommand  →  UpdateProductCommandHandler  →  ProductUpdatedNotification
```

- **One command per mutation**, marked `[AuditableRequest]` so handling it is recorded as an
  activity. Create returns the new identifier, update returns the resource DTO, delete returns
  nothing (`ICommand`).
- **Every mutation publishes a notification** — `Product{Created,Updated,Deleted}Notification` —
  per [architecture rule 1](architecture-rules.md): the handler answers with an event and does not
  care whether anything listens. The deleted notification carries the name as well as the id,
  because the row is gone by the time a subscriber runs.
- **Validation is field-level and lives in an `IValidator<TCommand>`**, which the handler invokes
  and turns into a `ValidationException`. Resource-level rules — uniqueness, state rules — stay in
  the handler, where the database is, and throw `ConflictException` or `BusinessRuleException`.
  Commands that share an editable field set share one validator, so a create and an update of the
  same values can never disagree about what is valid.
- **Load the row with a query, not `FindAsync`.** A primary-key lookup may be answered from the
  change tracker without reaching the database, and that path does not apply the global query
  filters — the tenant filter among them. `FirstOrDefaultAsync(p => p.Id == id)` is the form that
  makes another tenant's row a 404 instead of an edit.

> **Why the handler calls the validator itself.** `ValidationBehavior<,>` exists, but the dispatcher
> resolves behaviors — and the `IValidator<T>` instances they ask for — by the *static* type it
> dispatches under, `ICommand<TResult>`, never by the concrete command type. A validator registered
> as `IValidator<UpdateProductCommand>` is therefore never reached from the pipeline. See "Which
> behaviors apply to a request" in [mediator.md](mediator.md). Injecting the validator into the
> handler is explicit, testable, and does not depend on that distinction.

## Concurrency: optimistic, over `ETag` and `If-Match`

A resource that is edited directly needs a way to refuse a write that would silently overwrite a
change the writer never saw. The convention is optimistic concurrency carried in the standard HTTP
validators, and `products` implements it end to end:

- **A concurrency token on the entity, projected into the resource DTO.** `Product` carries a
  `Version` (a `Guid`) marked `[ConcurrencyCheck]`, so EF Core puts it into the `WHERE` clause of
  every update and delete; `ProductDto` carries the same `Version`. A `Guid` the handler rolls on
  every write rather than a provider `rowversion`, because the samples run on SQLite, which has no
  automatically maintained row-version column — a token the application owns is the
  provider-independent form of the same idea.
- **The token is a strong `ETag` on `GET`.** `GET /api/products/{id}` returns the resource and sets
  `ETag` to `product.Version.ToVersionEntityTag()` — the shared `Bsft.Http` primitive, which digests
  the token into the `"sha256-<hex>"` shape the framework's conditional-GET endpoints already serve.
  The update response carries the new `ETag` too, so a client can chain a second write without a
  read in between. A resource adopting this convention derives its tags with that primitive rather
  than with a spelling of its own: the value a read hands out and the value a write is checked
  against have to be computed one way, and the token is digested rather than published so a
  monotonic counter does not become a public write count.
- **`If-Match` is required on `PUT` and `DELETE`.** A conditional write made without an `If-Match`
  is a `PreconditionRequiredException` → **428**: the server will not guess which version the client
  meant to replace. An `If-Match` that no longer names the current version is a
  `PreconditionFailedException` → **412**: the writer's view is stale, and the answer is "read it
  again", not a last-write-wins overwrite. A matching `If-Match` proceeds and answers **200** (update)
  or **204** (delete).

The comparison itself is `Bsft.Http`'s `IsMatchedBy`, which is strong as RFC 9110 requires for
`If-Match`: `*` matches whatever version the resource has now, an exact tag matches, and a weak
validator or an unquoted spelling does not — a write is gated on the exact version the client last
saw, not on one that is merely equivalent for caching. The `428` stays in the endpoint, which is
where the header's absence is noticed and where the wording of the refusal belongs; see
[http.md](http.md#gating-a-write) for the two headers side by side.

The handler fails closed: it compares the `If-Match` against the version it just read and throws the
**412** before touching anything, so a stale write changes nothing. The `[ConcurrencyCheck]` token
closes the remaining window — where another writer commits between the read and the save — by
turning EF Core's `DbUpdateConcurrencyException` into the same **412**. `BsftExceptionMiddleware` maps
both `PreconditionRequiredException` and `PreconditionFailedException`, so neither ever surfaces as a
500, and the sample keeps `Bsft.Hosting` free of any EF Core dependency by throwing the framework's
own exceptions rather than letting a provider exception reach the middleware.

A client's loop is therefore: read the resource, keep its `ETag`, send it back as `If-Match` on the
next write; on a **412**, re-read and reconcile; on a **428**, it forgot the header. `Bsft.Sample.Orders`
still opts out of all of this, for the same reason it opts out of `PUT` and `DELETE` — an order is a
workflow aggregate, not a directly edited resource.

### Every list row carries its own validator

Read literally, the loop above costs a list screen one `GET` by id per row before every edit or
delete: the row is on screen, the user presses save, and the client has to fetch the resource it is
already showing purely to learn the tag its `If-Match` needs. That read exists only to move a value
the server already had when it built the list, so the list carries it instead.

**A list item publishes its own entity tag, under `etag`.** The contract is
`IEntityTaggedItem` in `Bsft.Data.Contracts` (`Bsft.Data.Pagination`), a single nullable `ETag`
member that a resource's list DTO implements; `PagedResult<T>` itself is unchanged, because the tag
belongs to the row rather than to the page. `ProductDto` implements it, so a page of products is a
page of writable rows:

```json
{ "items": [ { "id": "…", "name": "Widget", "version": "…", "etag": "\"sha256-…\"" } ], "totalCount": 1 }
```

Four decisions inside that:

**The tag is the same value the detail endpoint serves, not merely one like it.** `GET /api/products/{id}`
and the row for that product at that version publish one string. That is not a convenience, it is the
whole contract: a list tag that differed in any byte would make every write issued from a list row a
**412** — the failure would look like a concurrency conflict and be a spelling mistake. The sample
guarantees it structurally rather than by discipline, by deriving the tag in exactly one place —
`ProductMappingProfile`, which the list projection, the read by id and the update response body all
pass through — with `Bsft.Http`'s `ToVersionEntityTag`. The endpoint then writes the tag the DTO
already carries into the `ETag` header instead of deriving a second one from the version.

**The tag is derived, not the version republished.** The version is in the list DTO too, and a client
could in principle hash it itself — but the derivation is a canonical SHA-256, and a client
reimplementing it is a second spelling waiting to drift. The server does the one hash per row it is
already able to do, over one short string, and the client treats the result as opaque.

**It is optional, and null means "read me first".** The member is nullable so the contract is purely
additive: a resource with no concurrency token, or a host that has not adopted conditional writes,
publishes no tag and behaves exactly as before. A client that finds no tag on a row falls back to the
read it used to make. A blank tag is not a third state — `ToVersionEntityTag` refuses a blank version
rather than minting a validator that matches nothing.

**The page itself carries no `ETag`.** A page is an assembled view of many resources; a validator over
the whole page would be invalidated by a write to any row on it, and a client holding it could not
tell which row moved. Validators sit on the items, where the writes are.

None of this weakens the check. Skipping the read does not skip the comparison: the tag a client took
from a list row is compared strongly against the version the row actually has when the write arrives,
so a row that went stale between the list and the write is still refused with a **412** — the client
just learns it one round trip later than it used to, instead of one round trip earlier for every row
that had not gone stale at all.

One caveat inherited from the derivation: the tag is a hash, so it has to be computed on materialized
rows. A resource that projects to its DTO inside the query — `ProjectTo` over `IQueryable` — derives
its tags in a `Select` after the page comes back, because no database provider translates a SHA-256.

## Idempotency on unsafe endpoints

Concurrency answers "did somebody else change this since I read it". The other half of the same
bargain is "is this the same request I already ran", and the five endpoints answer it very
unevenly — which is the point of stating it here rather than leaving each resource to work it out.

**`PUT` and `DELETE` are already idempotent by construction, and need no key.** A `PUT` replaces the
editable state of a named resource, so sending it twice leaves the same state; a `DELETE` removes a
named row, so the second attempt is the 404 the section above insists on. `GET` changes nothing.
Sending an `Idempotency-Key` on any of them is harmless and is simply ignored.

**`POST /api/{resource}` is the one that needs it.** It creates a row whose identifier the server
mints, so a retry that the client cannot tell from a timeout creates a second row — two orders, two
invoices, two charges. `Bsft.Idempotency` is the framework's answer: `[Idempotent]` on the action, or
`.RequireIdempotency()` on a minimal-API group, and a repeat under the same key replays the first
response instead of running again. See [idempotency.md](idempotency.md).

Two consequences for this convention specifically:

- **A replay of a create returns exactly what the create returned**, 201 with its `Location` header
  and the same `{ "id": … }` body, byte for byte. That is why the record stores the status, an
  allowlist of headers and the body rather than merely "already done": a client that retried has to
  be able to find what it created, and a replay that dropped the `Location` would leave it unable to.
  The replay is marked with `Idempotency-Replayed: true`, so the two are distinguishable without
  comparing bodies.
- **There is no `PATCH` in this convention, and that is not why `PATCH` is excluded.** The one
  `PATCH` the framework serves — `PATCH /api/settings/me/{key}`, `application/merge-patch+json` — is
  not idempotent in general, so it is opt-in-able rather than excluded by design: a host adds it to
  `BsftIdempotencyOptions.Methods`. The default considers `POST` alone.

The refusals join the table above rather than inventing a vocabulary: a missing key is the same 428
as a missing `If-Match`, told apart by its `errorCode`; a key reused for a different request and a
key still in flight are both 409s, likewise told apart by their codes; a body too large to
fingerprint is the 413.

## Reference implementation

`samples/Bsft.Sample.Inventory`, resource `products`:

| Piece | File |
| --- | --- |
| Endpoints | `Controllers/ProductsController.cs` |
| Commands | `Commands/{Create,Update,Delete}ProductCommand.cs` |
| Handlers | `Commands/{Create,Update,Delete}ProductCommandHandler.cs` |
| Validation | `Commands/ProductCommandValidator.cs` |
| Entity-tag derivation | `Mapping/ProductMappingProfile.cs` |
| Notifications | `../Bsft.Sample.Inventory.Core/Events/Product{Created,Updated,Deleted}Notification.cs` |

`Bsft.Sample.Orders` is deliberately *not* a CRUD resource: an order is a workflow aggregate driven
by a saga, and neither a blanket `PUT` over its state nor a `DELETE` of a placed order would be
meaningful. A resource is a CRUD resource when its state is edited directly; when it is advanced by
a process, model the transitions as their own commands and endpoints instead of forcing this shape
onto it.
