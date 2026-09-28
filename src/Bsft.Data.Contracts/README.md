# Bsft.Data.Contracts

The wire vocabulary of the `Bsft.Data` query surface — the paged request and result, the filter AST,
the sort criteria and the search request — with no dependencies at all, so a client can speak it
without acquiring the query engine.

## Install

```bash
dotnet add package Bsft.Data.Contracts
```

Requires nothing. That is the point of the package: its dependency list is empty and a test pins it
empty, so referencing it from a Blazor WebAssembly front end or a console tool adds these types and
nothing else. A server that also needs to *execute* the queries references `Bsft.Data`, which brings
this package with it.

## Register

Nothing to register. This package contains only contract types; it has no DI entry point and no
services. The engine that consumes these types is registered by
`DataServiceCollectionExtensions.AddBsftDataQueryFiltering` in `Bsft.Data`.

## Configure

None. The package has no options type and reads no configuration.

| Option | Default | Effect |
| --- | --- | --- |
| — | — | — |

Validation: none. The types carry no invariants of their own; a filter is validated when
`FilterCompiler` in `Bsft.Data` binds it against a query configuration, and the bounds on a
caller-supplied filter are enforced by `BsftFilterTransportOptions` in `Bsft.Hosting`.

## Extend

`FilterNode` is a concrete base class with `[JsonDerivedType]` attributes naming `AndNode`, `OrNode`,
`ComparisonNode` and `PresenceNode`; the discriminators are the serialization contract, so a new node
type is a wire-format change and belongs in this package rather than in an application. `PagedResult<T>`
and `RankedItem<T>` are generic over the row type an application returns, and `IEntityTaggedItem` is
the one seam a DTO implements itself, so that a list response can carry a per-row entity tag for
conditional requests. Nothing here is replaceable at runtime.

## Documentation

Topic page: [docs/data.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/data.md).
Conventions: [docs/package-conventions.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/package-conventions.md).
