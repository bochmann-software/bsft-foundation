# Bsft.Http

The HTTP primitives more than one framework endpoint has to agree on: a strong entity tag, a
deterministic content digest to build one out of, the reading of `If-None-Match`, the reading of
`If-Match` a conditional write is gated on, and the caching policy that belongs on a response a
client is meant to revalidate rather than reuse blindly.

`Bsft.Http` registers no services and has no dependency beyond ASP.NET Core itself. Everything in
it is a pure function of its arguments.

## Why a package rather than two copies

Conditional GET is four decisions — how the tag is built, what counts as a match, what a malformed
validator does, and what `Cache-Control` goes out beside it — and every one of them is wrong in a
way that is invisible until a client is serving a stale body. Two endpoints spelling them
separately drift, and the drift shows up as a caching bug in production rather than as a failing
test.

`Bsft.Storage` predates this package and answers the same questions itself, in
`StorageEntityTags` and `DownloadRequestHeaders`. The tag grammar here is deliberately the one it
already serves, so a client needs one implementation to revalidate a download, a translation
bundle and a permission map. Converging the storage helpers onto this package is a follow-up, not
part of the change that introduced it: those helpers also carry the range-request rules, where a
weak validator is a correctness problem rather than a wasted round-trip, and that is not a surface
to refactor in passing.

## The tag

```csharp
string?[] segments = ["en", tenantId, "2", "greeting", "Hello", "bye", "Goodbye"];
var tag = segments.ToCanonicalHash().ToContentHashEntityTag();   // "sha256-<64 hex chars>"
```

`ToContentHashEntityTag` produces a **strong**, quoted validator. It sanitises what it is given
to the characters RFC 9110 allows inside a tag, so a digest that somehow arrived with a carriage
return in it cannot become a second response header.

A resource that already carries a concurrency token of its own does not need the content digest
route — its token is the thing that changes when it changes:

```csharp
var tag = product.Version.ToVersionEntityTag();   // "sha256-<64 hex chars>"
```

Same grammar, so a client parses one shape whatever it is revalidating. The token is digested
rather than published as the tag body, which keeps it inside the server: a `rowversion` handed out
verbatim tells every caller how often the database has been written to. The `Guid` overload spells
the token `N` — 32 hexadecimal digits, no hyphens — because two spellings of one version would be
two tags for one resource; a token that is not a `Guid` is converted to text by its caller, which
is the only place that knows whether hexadecimal or base64 is the spelling the resource keeps to.

Deriving a version tag needs the version value and nothing else, which is what lets a list
projection tag every row it returns: one hash over one short string per item, no second read.

`ToCanonicalHash` is where the determinism comes from, and it hashes a sequence of segments rather
than a serialized document on purpose. A digest taken over a serializer's output moves when
property order, number formatting or whitespace move, so two nodes behind one load balancer would
hand out two tags for the same content and every client would revalidate for ever. Handing the
hash an explicit sequence puts that order in reviewable code instead.

Two rules the callers follow, both of which the helper cannot enforce for them:

- **Length-prefix, which `CanonicalHash` does per segment, and frame every variable-length list
  with its own count, which the caller does.** Without the per-segment prefix, `"ab" + "c"` and
  `"a" + "bc"` hash the same. Without the count, three lists hashed one after another are one flat
  sequence, and a permission that moved from one list to another leaves the digest unchanged.
- **Sort anything whose enumeration order is not a promise.** A dictionary's order follows
  insertion, which follows a hash set. Two nodes resolving the same content would otherwise hash it
  into two versions.

The consequence of hashing the content rather than the bytes, stated plainly: the tag identifies
the **logical representation**, not the exact octets on the wire. A host that reconfigures its JSON
serializer changes the bytes without changing the tag. That is a startup-time change to a process
whose caches do not survive it, and the alternative — serializing twice per request, once to hash
and once to send — buys nothing a restart does not already provide.

### What the tag does not expose

A 304 is decided by comparing the client's tag against the tag of the representation **being served
to that same client on that same request**, never against a stored tag belonging to somebody else.
A caller replaying a tag it guessed therefore learns only whether it guessed its own content, which
it already has. There is no confirmation oracle for another caller's representation, and that
property comes from where the comparison happens rather than from the hash being hard to invert.

## Reading the request

```csharp
if (httpContext.Request.IsUnchangedFrom(tag))
    return Results.StatusCode(StatusCodes.Status304NotModified);
```

Only `If-None-Match` is honoured. The tag is a digest of the exact content, so it answers "has this
changed" precisely; `If-Modified-Since` answers the same question to the nearest second out of a
clock the client and the server do not share, and supporting both means deciding which wins when
they disagree.

Comparison is weak, as the specification requires for this header: a client's `W/"x"` matches a
server's `"x"`, and `*` matches any representation that exists at all. A header the code cannot
make sense of is **ignored** and the full body is served. That is the only safe direction to fail
in — a malformed validator answered with a 304 hands a client a body it never received, while one
answered with a 200 costs bandwidth and nothing else.

## Gating a write

The write side reads the other validator, `If-Match`, and reads it under the opposite rules:

```csharp
if (!product.Version.ToVersionEntityTag().IsMatchedBy(command.IfMatch))
    throw new PreconditionFailedException($"Product '{command.ProductId}' has been modified since it was last read.");
```

| | `If-None-Match` (`IsUnchangedFrom`) | `If-Match` (`IsMatchedBy`) |
| --- | --- | --- |
| Comparison | Weak — `W/"x"` matches `"x"` | Strong — a weak validator never matches |
| Unquoted spelling of the tag | Matches | Does not match |
| `*` | Matches any representation that exists | Matches whatever version the resource has now |
| Header absent or unreadable | Ignored, full body served | No match, the write is refused |

The asymmetry is the point. The worst outcome of a generous `If-None-Match` is a body served that
need not have been; the worst outcome of a generous `If-Match` is an overwrite of a change the
writer never saw. So the read side fails towards serving and the write side fails closed.

`IsMatchedBy` takes the raw header value rather than the request, so the comparison can sit
wherever the resource's current version is known — in this framework's shape the command handler,
which reads the row and decides. A blank or absent header answers `false` rather than throwing,
because the only way past the check should be a header that matches.

Telling the caller it *forgot* the header is a different answer — `428 Precondition Required`, not
`412` — and it stays with the endpoint, which asks the question before anything has been read and
owns the wording of the refusal:

```csharp
var ifMatch = Request.Headers.IfMatch.ToString();
if (string.IsNullOrWhiteSpace(ifMatch))
    throw new PreconditionRequiredException("This operation requires an If-Match header carrying the version being edited.");
```

Both exceptions live in `Bsft.Core` and are mapped by `BsftExceptionMiddleware`; see
[crud-conventions.md](crud-conventions.md#concurrency-optimistic-over-etag-and-if-match). They are
deliberately not thrown from `Bsft.Http`, which keeps it free of any dependency beyond ASP.NET Core
and free of any opinion about what a given resource's refusal should say.

## Writing the response

```csharp
httpContext.Response.UseRevalidation(tag);   // ETag + Cache-Control: private, no-cache
```

Called on the 304 as well as on the 200. A 304 stands in for the response it replaces and has to
carry the same validator, or the client cannot tell which of its stored representations was just
confirmed.

`private, no-cache` is the policy, and the naming of the second directive invites the wrong
reading, so it is worth stating what each half buys:

| Directive | What it does | Why it is here |
| --- | --- | --- |
| `private` | No shared cache — proxy, CDN, corporate middlebox — may store the response. | These representations depend on the authenticated principal and the tenant of the request, and the URL names neither. A shared cache keyed on the URL would serve one caller's answer to the next. |
| `no-cache` | The client may keep its copy but must revalidate before reusing it. | This is the bargain the entity tag is for: the client keeps the body, and the round-trip that follows is a conditional request the server answers with an empty 304 whenever nothing changed. |

`no-store` is deliberately **not** used. It would forbid the client from keeping the body at all,
which turns every revalidation back into a full download and throws the tag away with it. It is the
right choice for a file download served against a bearer token — which is why
`Bsft.Storage.Downloads` uses it — and the wrong one for a document the client has already rendered
and will ask for again on every page load.

Shared caching is therefore off, permanently and by construction. There is no option to turn it on:
a host that wants one of these responses in a CDN wants a different endpoint.

## Declaring a route's permissions

`RequiredPermissionsMetadata` names the permissions a route stands for. It is metadata and not a
check: the check is the authorization policy the host configured, and this record only says what
that policy is expected to mean, so `GET /api/auth/permissions` or a console can tell a caller what
it is missing before it makes the call.

```csharp
group.MapGet("/", ListAsync)
    .RequireAuthorization(policy)
    .WithMetadata(new RequiredPermissionsMetadata([JobsHttpDefaults.ReadPermission]));
```

Until 2.0 each of fifteen packages declared its own copy of this record in its own namespace, so a
reader had to know every one of them. There is now one type, here, because this is the one package
every route-mapping package can reference without taking on anything but the ASP.NET Core shared
framework it already carries.

## Who uses it

| Endpoint | Package | What the tag covers |
| --- | --- | --- |
| `GET /api/localization/{locale}` | `Bsft.Localization` | The locale, the tenant, and every resolved key/value pair. See [localization.md](localization.md#conditional-requests). |
| `GET /api/auth/permissions` | `Bsft.Auth` | The principal, the tenant, the route map and both permission lists. See `Bsft.Auth`. |
| `GET/PUT/DELETE /api/products/{id}` | `Bsft.Sample.Inventory` | The product's concurrency token, served as `ETag` and required back as `If-Match`. See [crud-conventions.md](crud-conventions.md#concurrency-optimistic-over-etag-and-if-match). |
