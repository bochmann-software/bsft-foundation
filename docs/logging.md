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
returning entries; `MinimumLevel`, `IncludedLevels` and `SearchText` are result filters, not access controls.

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
