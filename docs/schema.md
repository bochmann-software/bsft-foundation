# Schema

`Bsft.Schema` validates a document against a schema that arrived at runtime from a party the platform
does not trust: an application manifest's config schema, a webhook payload contract, a settings blob
whose shape is not known at compile time.

It is not `Bsft.Settings`. `.Constrain(x => x.Prop, predicate, message)` validates a typed record whose
type the compiler knows; here both the shape and the document are input, and the schema is itself
something a stranger submitted.

## The subset

`type: "object"` at the root. Six types, and per type the keywords that apply to it:

| Where | Keywords |
| --- | --- |
| Any node | `type`, `enum`, plus the annotations `$schema`, `title`, `description`, `default` |
| `string` | `format`, `pattern`, `minLength`, `maxLength` |
| `integer`, `number` | `minimum`, `maximum` |
| `array` | `items`, `minItems`, `maxItems` |
| `object` | `properties`, `required`, `additionalProperties` (only `false`) |

`format` is `email`, `uri`, `date`, `date-time` or `uuid`, plus the two presentational values
`textarea` and `markdown`, which say how a string is edited and constrain nothing.

`uri` means an absolute URI that carries its own scheme, and the scheme has to be longer than one
character. Both rules exist so the same manifest gets the same answer on every host: `.NET` reads
`/etc/passwd` as an absolute `file:` URI on Unix and refuses it on Windows, and `C:\share` parses as a
drive path on one and as a one-letter scheme on the other.

That is the whole of it. No `$ref`, no `allOf`/`anyOf`/`oneOf`/`not`, no union type, no conditional
schema, nothing that can recurse without a bound. The list is the one twenty shipped manifests in the
app store surveyed in a design survey actually used, so
it is a floor drawn from practice rather than from taste.

**UI metadata belongs beside the schema, not in it.** A sibling object keyed by property name,
carrying a label and a widget, duplicating nothing: `required` lives only in `required` and the
choices only in `enum`, so the two cannot disagree. Nothing here reads such an object, and nothing
here should.

## Reading a schema, then validating a document

```csharp
services.AddBsftSchema();          // or AddBsftSchema(builder.Configuration)
```

```csharp
var read = reader.Read(submittedSchema);

if (!read.IsUsable)
    return Refuse(read.Violations);      // the developer's problem: a review queue

var check = validator.Validate(read.Schema!, tenantConfiguration);

if (!check.IsValid)
    return Refuse(check.Violations);     // the tenant's problem: the install screen
```

The two calls are the two failures, and which call refused is what tells them apart. A schema outside
the subset never reaches the validator, so every refusal the validator produces is about the document.
Read a schema once, at submission, and keep the `SchemaNode`; reading is where the bounds are checked
and where a pattern is compiled.

## Three decisions worth arguing about

Each of these refuses documents a general-purpose validator would accept.

**An unknown keyword is refused, not ignored.** A schema writing `maxLenght` declares a bound the
platform then does not enforce, on an install screen that tells a tenant admin the field is validated.
Ignoring it is the silent failure; refusing it puts the typo in front of the developer at submission,
where it costs nothing. The survey records this exact class of defect being caught by a strict mode
that CI turned on, and the same one reaching production where the mode was off.

**`additionalProperties` may only be false.** An object with room in it for anything cannot be read
back by anything downstream, and in a store it is where a tenant's own data ends up inside a document
the application's developer can read.

**A `pattern` is compiled with backtracking off** (`RegexOptions.NonBacktracking`) and a timeout. That
removes the catastrophic case outright rather than budgeting for it. The cost is that backreferences
and lookaround are refused — a narrower pattern language than JSON Schema's — and that is the
intended trade for an expression that arrived with somebody's application.

**A `$` in a pattern also matches before one trailing newline.** JSON Schema's patterns are ECMA-262,
where `$` without the multiline flag anchors at the end of the input and nowhere else; .NET's `$`
anchors there too but additionally just before a single final newline. So `^[a-z]+$` accepts a value
that ends in one here and would not in a browser. The divergence is left in place rather than papered
over by rewriting the expression: a schema's pattern is run exactly as it was written, and silently
editing an anchor would make the refusals of this framework impossible to reproduce against any other
JSON Schema implementation. A publisher who needs the stricter reading writes `\z` instead of `$`,
and a value whose trailing whitespace matters is better bounded by a `format` than by an anchor.

## The validator is itself an attack surface

Both documents are untrusted, so neither is read without a ceiling. Every bound is an option and every
one of them must be greater than zero, because a zero here is not a permissive setting but an absent
one.

| Option | Default | Effect |
| --- | --- | --- |
| `MaxDepth` | 8 | How deeply a schema or a document may nest. |
| `MaxProperties` | 100 | How many properties one object may declare or carry. |
| `MaxSchemaNodes` | 500 | How many nodes one schema may contain in total. |
| `MaxArrayItems` | 1000 | How many items one array in a document may carry, above whatever the schema's own `maxItems` says. |
| `PatternTimeout` | 100 ms | How long one pattern match may run. |

`MaxArrayItems` is the host's ceiling rather than the developer's: it applies to an array that declared
no bound of its own.

## Refusals

Every refusal, not the first: a form with four wrong fields that reports one of them is a form somebody
submits four times.

A `SchemaViolation` carries three things. The **path** is an RFC 6901 JSON Pointer (`/smtp/port`,
`/hosts/2`, `/a~1b` for a name containing a slash) — a pointer rather than a dotted path, because a
property name may itself contain a dot and a path that cannot be parsed back is a path a form cannot
highlight the right field from. The **code** is what a caller branches on. The **message** is the only
part a person reads, and it says what the value should have been rather than which keyword failed.

The codes split in two, and the split is about who is looking at them:

| Prefix | Whose problem | Where it belongs |
| --- | --- | --- |
| `SCHEMA_*` | The developer who submitted the application | A review queue, at submission |
| `VALUE_*` | The tenant admin configuring an install | The install screen, immediately |

## Extend

`ISchemaReader` and `ISchemaValidator` are registered with `TryAdd`. A host with a schema language of
its own registers its own reader; the validator then works against whatever `SchemaNode` that reader
produced. `SchemaNode` is the boundary between the two, and reaching a validator means the subset and
the bounds were already checked.

## Packages

| Package | Entry point |
| --- | --- |
| `Bsft.Schema` | `SchemaServiceCollectionExtensions.AddBsftSchema` |
