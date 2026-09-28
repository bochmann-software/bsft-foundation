# Bsft.Http

The HTTP primitives the framework's endpoints share: strong entity tags built from a content digest
or a concurrency token, the canonical hash those digests come from, conditional-GET handling on
`If-None-Match`, the strong `If-Match` comparison a conditional write is gated on, and
`RequiredPermissionsMetadata`, the one endpoint-metadata record every package that maps routes uses to
declare the permissions a route stands for.

## Install

```bash
dotnet add package Bsft.Http
```

No further Bsft packages. It takes a framework reference on `Microsoft.AspNetCore.App`, because two
of its types are extension methods on `HttpRequest` and `HttpResponse`.

## Register

Nothing to register. `Bsft.Http` contributes no services and therefore has no `AddBsftHttp` — every
type it ships is a static class of pure functions an endpoint or a handler calls directly, or a
metadata record a route carries.

## Configure

None. The package binds no configuration section and exposes no options type.

| Option | Default | Effect |
| --- | --- | --- |
| — | — | — |

Validation: none. `ConditionalWrite.IsMatchedBy` rejects a blank current tag with an `ArgumentException`,
and `EntityTag` sanitizes the digest it is handed, but neither is configuration.

`ToVersionEntityTag` digests the concurrency token so the storage value is not published verbatim.
That is obfuscation rather than confidentiality: the digest is unkeyed, so a token drawn from a small
range — a low monotonic counter — can be recovered by hashing the candidates. Use a high-entropy
token where the write count is sensitive.

## Extend

None. The package is deliberately made of static functions rather than seams: a tag that two hosts
compute differently is a tag that pins every client to a revalidation loop, so the comparison and the
digest are one implementation and are not replaceable. A caller that needs a different tag shape
builds it itself and does not route through `EntityTag`.

## Documentation

Topic page: [docs/http.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/http.md).
Conventions: [docs/package-conventions.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/package-conventions.md).
