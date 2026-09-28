# Bsft.Schema

A bounded subset of JSON Schema for documents a party the platform does not trust submitted: an
application manifest's config schema, a webhook payload contract, a settings blob whose shape is not
known at compile time. Reads a schema, refuses everything outside the subset by name, and checks a
document against it with refusals a person can act on.

## Install

```bash
dotnet add package Bsft.Schema
```

No third-party schema library and no framework dependency: the whole package is the subset below.

## Register

```csharp
services.AddBsftSchema();
```

or, to move the bounds into configuration:

```csharp
services.AddBsftSchema(builder.Configuration);
```

Registers `ISchemaReader` and `ISchemaValidator` as singletons. Both hold no state between calls.

## The subset

`type: "object"` at the root. Six types — `string`, `integer`, `number`, `boolean`, `array`,
`object` — and per type: `enum` and `default` anywhere; `format`, `pattern`, `minLength` and
`maxLength` on a string; `minimum` and `maximum` on a number; `items`, `minItems` and `maxItems` on an
array; `properties`, `required` and `additionalProperties: false` on an object. `$schema`, `title` and
`description` are carried as annotations. `format` is one of `email`, `uri`, `date`, `date-time`,
`uuid`, or the two presentational values `textarea` and `markdown`, which constrain nothing.

That is the whole of it. There is no `$ref`, no `allOf`/`anyOf`/`oneOf`/`not`, no union type, no
conditional schema and nothing else that can recurse without a bound.

**Three decisions worth stating plainly**, because each of them refuses documents a general-purpose
validator would accept:

- **An unknown keyword is refused, not ignored.** A schema writing `maxLenght` declares a bound the
  platform then does not enforce, on an install screen that says the field is validated. The developer
  finds out at submission, where a typo is cheap.
- **`additionalProperties` may only be false.** An object with room in it for anything cannot be read
  back by anything downstream, and in a store it is where a tenant's own data ends up in a document
  the application's developer can read.
- **A `pattern` is compiled with backtracking off.** That removes the catastrophic case outright
  rather than budgeting for it. The cost is that backreferences and lookaround are refused, which is
  the intended trade for an expression that arrived with the application.

## Configure

`BsftSchemaOptions.SectionName` is `"Bsft:Schema"`.

| Option | Default | Effect |
| --- | --- | --- |
| `BsftSchemaOptions.MaxDepth` | 8 | How deeply a schema or a document may nest. |
| `BsftSchemaOptions.MaxProperties` | 100 | How many properties one object may declare or carry. |
| `BsftSchemaOptions.MaxSchemaNodes` | 500 | How many nodes one schema may contain in total. |
| `BsftSchemaOptions.MaxArrayItems` | 1000 | How many items one array in a document may carry, above whatever the schema's own `maxItems` says. |
| `BsftSchemaOptions.PatternTimeout` | 100 ms | How long one pattern match may run. |

Validation: every bound must be greater than zero. A zero is not a permissive setting but an absent
one, and the ceiling exists because both documents are untrusted input.

## Refusals

A `SchemaViolation` carries a JSON Pointer (`/smtp/port`), a code from `SchemaCodes`, and a sentence.
All three, because a refusal is shown on an install screen rather than written to a log: the pointer
is what makes it actionable in a document with forty fields, the code is what a caller branches on,
and the sentence is the only part a person reads.

The codes split in two on purpose. `SCHEMA_*` is a defect in a document a developer submitted and
belongs in a review queue; `VALUE_*` is a tenant's configuration not matching a schema that is itself
fine, and belongs on the install screen. The two are shown to different people.

## Extend

`ISchemaReader` and `ISchemaValidator` are the seams, registered with `TryAdd`. A host that has a
schema language of its own registers its own reader; the validator then works against whatever
`SchemaNode` that reader produced. `SchemaNode` is the boundary between the two, and reaching a
validator means the subset and the bounds were already checked.

## Documentation

Topic page: [docs/schema.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/schema.md).
Conventions: [docs/package-conventions.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/package-conventions.md).
