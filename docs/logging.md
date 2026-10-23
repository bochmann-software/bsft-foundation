# Bsft.Logging

A structured file logger with daily rotation, size-based rolling and retention
cleanup. It is a plain `ILoggerProvider`, so application code keeps using
`ILogger<T>` and never references this package.

`BsftLoggingOptions` is the one type that describes file logging. `Bsft.Hosting`
does not reimplement any of this: it references this package, and its
`BsftHostLoggingOptions` nests these options behind `FileLogging(...)` — see
[hosting.md](hosting.md).

## Setup

Two entry points, depending on whether you want the whole logging stack or just
the file provider.

```csharp
// The stack: clears providers, applies levels and filters, adds console + file.
services.AddBsftLogging(options =>
{
    options.LogDirectory = "logs";
    options.RetainDays = 14;
});

// Just the file provider, alongside whatever else is configured.
services.AddLogging(builder => builder.AddBsftFileLogger());
```

`AddBsftLogging` is opinionated: it calls `ClearProviders()`, sets the minimum
level from the options, filters the `Microsoft` and `System` categories to
`Warning`, adds the console provider, and adds the file provider when
`EnableFileLogging` is true. Reach for `AddBsftFileLogger` on an `ILoggingBuilder`
when you want the file output without the rest of the opinion.

Both take an **optional** configure delegate.

## Options

`Bsft.Logging.BsftLoggingOptions`:

| Property | Type | Default | Meaning |
| --- | --- | --- | --- |
| `EnableFileLogging` | `bool` | `true` | Whether `AddBsftLogging` adds the file provider. |
| `LogDirectory` | `string` | `"logs"` | Directory; a relative path is resolved from the process working directory. |
| `MaxFileSizeMb` | `int` | `10` | Size at which the current file rolls. Between 1 and 1024. |
| `RetainDays` | `int` | `30` | Files older than this are deleted. Between 1 and 3650. |
| `EnableJsonFormat` | `bool` | `false` | One JSON object per line instead of text. |
| `MinimumLevel` | `LogLevel` | `Information` | Minimum level for the file logger. |
| `LogReaderPollInterval` | `TimeSpan` | `250 ms` | Poll interval for the live file tail. Must be positive. |
| `MaxEntryBytes` | `int` | `1048576` (1 MiB) | Maximum UTF-8 bytes retained for one physical line or multiline entry. Must be positive. |

`MinimumLevel` is the file sink's level, not the console's. Raising it quietens the
file and leaves the console where it was. Lowering it below `Information` does reach
the console as well, because the logging factory drops a record before it is offered
to any provider, so the pipeline has to carry what the file asks for.

## Files and format

The current file is `app-{yyyy-MM-dd}.log`. When it exceeds `MaxFileSizeMb` it
rolls to `app-{yyyy-MM-dd}_001.log`, `_002` and so on, so a single noisy day
never produces one unreadable multi-gigabyte file. Retention is applied at startup
and again whenever the day turns over, which is the one moment a running process
has to prune without polling for it.

A text line looks like this:

```
[2026-03-24 14:30:00.123 INF] [abc-123-def] OrdersController: Order ORD-42 created
```

The bracketed field after the level is the correlation id. The logger does not
resolve it from a service — it picks up a log **scope** entry whose key is
`CorrelationId`, and writes `-` when no such scope is active. Pushing that scope
is what `CorrelationIdMiddleware` does, so a correlated log file only requires
`app.UseBsftCorrelation()` in the pipeline; see [correlation.md](correlation.md).
Levels are abbreviated `TRC`, `DBG`, `INF`, `WRN`, `ERR`, `CRT`.

Line breaks are stripped from the message and the correlation id before they are
written, so a CRLF in either cannot forge an extra log line.

The scope key is the public constant `Bsft.Correlation.LogScopeKeys.CorrelationId`. `Bsft.Correlation` (the
producer), the file logger and reader in `Bsft.Logging` and the console formatter in `Bsft.Hosting` all use it, so the
field name cannot drift. It lives in `Bsft.Correlation.Abstractions` because that package has no dependencies at
all: the producer does not have to take a log sink as a package dependency, and `Bsft.Logging` takes in no ASP.NET
Core. Both packages are Foundation, so the tier rule allows the edge; the abstractions are simply the lightest place.

The correlation slot of a text line is escaped on write (a backslash and `]` get a leading backslash, line breaks
become a space) and unescaped on read, so an id such as `a] {RunId=x}` cannot end the slot early and pass the rest
off as a property block. Ordinary ids contain neither character and are written unchanged. Other sinks write the same line format through
`Bsft.Logging.Sinks.IScopeLineEncoder` (`ScopeLineEncoder`): the console formatter in `Bsft.Hosting` uses it, and
`BsftConsoleFormatterOptions.PersistedScopeKeys` is validated by the file logger's rules (null list, blank key and the
reserved marker key are refused when the options are read).

### Persisting more scope keys

`BsftLoggingOptions.PersistedScopeKeys` lists the scope keys written on every line. The default is
`CorrelationId` alone, which keeps its dedicated slot and leaves the output byte-identical to earlier releases.
Any further key (`"RunId"`, `"Tenant"`, ...) is written after the correlation id, in the configured order, with
the innermost value of that key that was in scope when the line was logged. Keys opened by different scopes are
all persisted: a request scope carrying `CorrelationId` and a nested run scope carrying `RunId` yield a line with
both, and a nested scope that repeats a key shadows the outer value only for the lines logged inside it. A key
with no value in scope is left out, and a line with no such key has neither block nor object. Configuration
binding adds to the default list, so `Bsft:Logging:PersistedScopeKeys:0 = RunId` persists the correlation id
and `RunId`; the correlation slot cannot be removed through configuration, only by assigning a new list in code.
`AddBsftLogging(configuration)` binds the `Bsft:Logging` section. `AddBsftHostLogging` builds the file logger
options only through `FileLogging(options => ...)`, so there the keys are set in code, and the host copies the list
to the console formatter. The scope state has to be a list or a sequence of `KeyValuePair<string, object>`, which is
what `BeginScope(Dictionary<string, object>)` and `BeginScope("Run {RunId}", id)` pass; other state objects carry no
persisted key. The scope chain is process-wide, so two file providers in one process share it.

Text lines carry a `{Key=value;Key=value}` block between the correlation id and the category:

```
[2026-03-24 14:30:00.123 INF] [abc-123] {RunId=run-7;Tenant=acme} OrdersController: Order ORD-42 created
```

Inside the block a backslash escapes the next character; the writer escapes the backslash, `;`, `=` and `}` in
keys and values, so a block ends at the first unescaped `}`, a pair at the first unescaped `;` and a key at the
first unescaped `=`. JSON lines carry the same properties as a `scope` object after `correlationId`
(`"scope":{"RunId":"run-7","Tenant":"acme"}`). Line breaks in values are replaced by a space in both formats.

The properties of one line are bounded by `MaxScopePropertyChars` (default 1024), counted as key plus value
characters over all extra properties; the correlation id is not counted. A property that does not fit whole is
dropped, never written cut, so an exact filter can miss such a line but never match a shortened value. Later,
smaller properties may still fit. A line that lost a property carries the reserved marker `~truncated=1` (only the value `1` counts; in the
block, or in the JSON `scope` object), which the reader lifts into `LogFileEntry.IsScopeTruncated` and keeps out
of `ScopeProperties`; that key cannot be configured. Lines written before this existed have no block or object and
decode unchanged. An old line whose category itself looks like `{K=v}` followed by a space would read as a block.

With `EnableJsonFormat = true` each line is a JSON object instead, which is what
you want when a collector ingests the files.

## Provider

`BsftFileLoggerProvider` is public and can be constructed directly, which is
occasionally useful in tests:

```csharp
public BsftFileLoggerProvider(BsftLoggingOptions options, TimeProvider? timeProvider = null)
```

Passing a `TimeProvider` is what lets a test drive rotation without waiting for
midnight. The logger itself (`BsftFileLogger`) is internal — it is reached only
through `ILogger<T>`.

Writes are queued and drained by a background thread, so a log call never waits
on the disk. The consequence is that entries are on disk within about half a
second rather than immediately, and entries still queued when the process is
killed are lost. `Dispose` gives the flush thread and final drain a shared five-second
budget. It normally drains the queue, but returns rather than hanging shutdown if
an in-flight file write holds the writer lock past that budget; such an abandoned
drain is reported separately from entries confirmed lost.

The registered writer and reader receive the same internal file-naming singleton,
so formatting and parsing `app-{yyyy-MM-dd}[_NNN].log` cannot drift. The public
provider and reader constructors cannot expose an internal DI seam; direct construction
therefore uses the package's default naming instance while the registration path uses
constructor injection.

## Reading and tailing files

Both `AddBsftLogging` and `AddBsftFileLogger` register `ILogFileAccess` and
`Bsft.Logging.LogFiles.ILogFileReader` as singletons. Calling both entry points still contributes one
reader and one file-access service. The contract is owned by the `Bsft.Logging` package and has no UI
dependency, so a local host can adapt it to its own viewer:

```csharp
var page = await reader.ReadAsync(new LogReadRequest(PageSize: 100));
var older = await reader.ReadAsync(new LogReadRequest(100, page.NextBefore));

await foreach (var entry in reader.TailAsync(new LogReadRequest(Before: page.Entries[0].Sequence), ct))
{
    // append entry to the viewer
}
```

Pages are **newest first**. A next cursor is exclusive and points to the next older page. The tail is
the opposite, yielding new entries in chronological order. Passing no tail cursor starts after the
entry that is newest when enumeration begins; it does not replay the existing file.

`LogReadRequest.CorrelationId` (an `init` property) narrows a read or tail to the entries of one request. It is an exact,
case-sensitive match against `LogFileEntry.CorrelationId`, the id the logger wrote on the line: the innermost
`CorrelationId` scope that was active when the entry was logged (a nested scope shadows its parent for the lines
logged inside it). It combines with `MinimumLevel`, `IncludedLevels`, `SearchText` and the paging cursor. A null,
empty or whitespace value applies no correlation filter. An entry logged outside any scope, which the file marks
with `-`, and a malformed line have a null `CorrelationId`, so no filter value selects them.

```csharp
var page = await reader.ReadAsync(new LogReadRequest(PageSize: 100) { CorrelationId = "0HN7..." });
```

`LogFileEntry.ScopeProperties` (`IReadOnlyDictionary<string, string>`, empty when there are none) exposes every scope
value read from the line: the correlation id under `CorrelationId` plus the properties of the block or `scope`
object. `LogReadRequest.ScopeFilter` (an `init` property, not a constructor parameter) is the generic form of the
correlation filter: every pair must equal the entry's value for that key exactly (ordinal, AND semantics), and a
null or empty filter applies none (a whitespace value in `ScopeFilter` is an exact filter, unlike a whitespace
`CorrelationId`, which applies none). `CorrelationId` is matched against the same map, so both can be combined and a
disagreeing pair selects nothing. A `CorrelationId` key inside the property block or `scope` object of a line is
ignored on read, so only the line's own slot states the id. A line with a correlation id and no other property builds
its `ScopeProperties` map when it is first read, so a scan that never reads it allocates none. `LogFileEntry` still
compares by value, the scope properties by content. The filter runs while entries are collected, so paging and tailing keep the
properties described below.

```csharp
var page = await reader.ReadAsync(new LogReadRequest(PageSize: 100)
{
    CorrelationId = "0HN7...",
    ScopeFilter = new Dictionary<string, string> { ["RunId"] = "run-7" }
});
```

The files are not indexed. A read walks the files newest first until it has `PageSize` matches, so an id that
is rare, or absent, makes it read every retained file; use the filter to open the log of one request, not as a
per-request query engine over long retention. A tail with a filter scans only what was appended since its last
poll, whatever the filter selects.

The reader understands the exact structured-text and JSON formats above. A multiline text exception
is returned on the same `LogFileEntry`; an unparseable line is returned with `IsMalformed = true`
instead of disappearing. The unterminated final fragment of an actively written file is deferred until
its newline arrives, so the reader never publishes a half-written entry. A physical line or combined
multiline entry larger than `MaxEntryBytes` is bounded in memory and returned with `IsTruncated = true`;
truncation removes partial UTF-8 code points at either retained boundary rather than emitting replacement
characters.

Sequence values use a positive 63-bit layout: 22 bits for the `DateOnly` day number, 11 bits for roll
indexes `0..2047`, and 30 bits for the UTF-8 byte offset. That keeps them monotonic across rolls and
process restarts and is the reason `MaxFileSizeMb` is capped at 1024. A manually supplied file whose
roll index or entry offset is outside the layout makes the read fail with `InvalidDataException`
instead of returning a colliding cursor.

Files are traversed backwards in bounded 64 KiB chunks with cancellable asynchronous reads and opened
with `FileShare.ReadWrite | FileShare.Delete`, so a large active log is not loaded into memory, no
worker thread is occupied by disk I/O, and reading does not prevent appending, rolling or deleting.
`TailAsync` polls with the configured `LogReaderPollInterval` through `TimeProvider`, making the wait
cancellable and testable. Every burst is reordered before emission so its `Sequence` values are
strictly ascending.

`ILogFileAccess` is the replaceable local-file seam for directory discovery and shared file opening.
`AddBsftLogging` registers the literal file-system implementation with `TryAddSingleton`, so a host can
register its own implementation first without being replaced.

## Security boundary

This package is local logging infrastructure, not a disclosure policy. Messages, correlation ids and
exception text are written as supplied; there is no secret or personal-data redaction. Callers must avoid
logging sensitive values or redact them before they reach `ILogger`.

`LogDirectory` and any replacement `ILogFileAccess` are trusted host configuration, never request or tenant
input. Files inherit the operating system permissions of the configured directory and are not encrypted by
this package, so production deployments must choose a directory and process identity that exclude untrusted
local users.

`ILogFileReader` returns every matching local entry and deliberately performs no authentication,
authorization or tenant filtering. A viewer or endpoint adapting it must enforce those controls before
returning entries; `MinimumLevel`, `IncludedLevels`, `SearchText` and `CorrelationId` are result filters, not access controls.

## Example

```csharp
public sealed class OrderService(ILogger<OrderService> logger)
{
    public void Confirm(string orderNumber)
    {
        logger.LogInformation("Order {OrderNumber} confirmed", orderNumber);
    }
}
```

Nothing in the call site names this package. Swapping the provider out later is
a change in one registration.

## See also

- [hosting.md](hosting.md) — `AddBsftDefaults()` configures logging as part of the host defaults.
- [correlation.md](correlation.md) — where the id in each line comes from.
