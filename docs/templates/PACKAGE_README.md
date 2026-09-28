# Bsft.<Feature>[.<Suffix>]

<!--
Template for the README.md that lives in every package folder under src/ and is
packed as the NuGet package readme. Keep every section; write "None." rather than
deleting a heading. One paragraph per section unless a table is called for.
Everything a host needs to decide whether to reference the package and how to
wire it goes here; deeper explanation stays on the topic page in docs/.
Remove this comment in the real file.
-->

One sentence: what this package gives a host and which family it belongs to.

## Install

```bash
dotnet add package Bsft.<Feature>
```

Requires `Bsft.<Feature>.Abstractions` (pulled transitively) and `<other Bsft
packages or third-party dependencies worth knowing about, or "no further Bsft
packages">`.

## Register

```csharp
services.AddBsft<Feature>(options =>
{
    options.<Property> = ...;
});

// or bound from configuration, section "Bsft:<Feature>"
services.AddBsft<Feature>(configuration);
```

`<Second sentence if the package also needs `UseBsft<Feature>` / `MapBsft<Feature>`
or a DbContext interface; otherwise "Nothing else is required.">`

## Configure

| Option | Default | Effect |
| --- | --- | --- |
| `Bsft<Feature>Options.<Property>` | `<value>` | <one line> |

Validation: `<what ValidateOnStart rejects, in one line>`.

## Extend

The seams a host may replace, one line each: `I<Something>` (what it decides,
what the default does). Write "None." if the package exposes no replaceable seam.

## Documentation

Topic page: [docs/<topic>.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/<topic>.md).
Conventions: [docs/package-conventions.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/package-conventions.md).
