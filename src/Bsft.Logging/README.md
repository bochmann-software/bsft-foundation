# Bsft.Logging

The framework's logging defaults and its file sink: structured console output, `Microsoft.*` and
`System.*` filtered down to warnings, and a file logger that rotates daily, rolls on size and deletes
what has outlived its retention.

## Install

```bash
dotnet add package Bsft.Logging
```

It depends on `Microsoft.Extensions.Logging` and its console provider, and on `Bsft.Correlation.Abstractions` (no dependencies of its own) for the `LogScopeKeys.CorrelationId` scope key.

## Register

```csharp
services.AddBsftLogging(options =>
{
    options.LogDirectory = "logs";
    options.RetainDays = 30;
});

// or bound from configuration, section "Bsft:Logging"
services.AddBsftLogging(configuration);
```

Nothing else is required. `AddBsftLogging` clears the providers the host had and installs this set,
so it replaces the host's logging configuration rather than adding to it. A host that wants only the
file sink alongside its own providers calls `builder.AddBsftFileLogger(...)` on `ILoggingBuilder`
instead — that is a separate extension family and does not touch the rest of the pipeline.

## Configure

`BsftLoggingOptions.SectionName` is `"Bsft:Logging"`.

| Option | Default | Effect |
| --- | --- | --- |
| `BsftLoggingOptions.EnableFileLogging` | `true` | Whether `AddBsftLogging` installs the file sink at all. |
| `BsftLoggingOptions.LogDirectory` | `logs` | Directory the log files are written to; a relative path is resolved from the process working directory. |
| `BsftLoggingOptions.MaxFileSizeMb` | `10` | Size at which the current file rolls to `app-{date}_001.log`. Capped at 1024. |
| `BsftLoggingOptions.RetainDays` | `30` | How many days of files are kept; older ones are deleted at startup and on each day rollover. Between 1 and 3650. |
| `BsftLoggingOptions.EnableJsonFormat` | `false` | One JSON object per line instead of structured text lines. |
| `BsftLoggingOptions.MinimumLevel` | `Information` | Minimum level for the file sink. It lowers the pipeline floor when it asks for more than `Information`, and never raises it. |
| `BsftLoggingOptions.LogReaderPollInterval` | `250 ms` | Poll interval used by `ILogFileReader.TailAsync`. Must be positive. |
| `BsftLoggingOptions.MaxEntryBytes` | `1 MiB` | Maximum UTF-8 bytes retained for one physical line or multiline entry. Oversized entries set `IsTruncated`. Must be positive. |

Validation: while file logging is on, the directory must be named, the file size must be between 1
and 1024 MB and the retention must be between one day and ten years. A failure names the member and the offending
value, and it is thrown from `AddBsftLogging` as well as on start. With file logging off the file
rules are not applied, because a host that turned the sink off has no use for a directory. Going
straight to `AddBsftFileLogger` or to `BsftFileLoggerProvider` is itself the decision to write to a
file, so the file rules are checked there whatever the flag says.

## Extend

`ILoggerProvider` is the seam: `AddBsftFileLogger` registers `BsftFileLoggerProvider` through a
factory so the container owns it and drains its queue on shutdown. A host that wants a different sink
adds its own provider on `ILoggingBuilder` and leaves `EnableFileLogging` off. The line format itself
is not replaceable; `EnableJsonFormat` is the one choice offered.

Both `AddBsftLogging` and `AddBsftFileLogger` register `ILogFileAccess` and `ILogFileReader` as
singletons for local viewers. Calling both entry points still contributes one reader and one file-access
service. `ReadAsync` returns cursor pages in
newest-first order; `TailAsync` yields newly appended entries oldest-first. It parses this package's
text and JSON formats, keeps text exception continuations with their entry, and exposes malformed
lines as fallback entries rather than dropping them. Cursors are stable monotonic sequences derived
from the file date, roll index and byte offset. Reads scan bounded 64 KiB chunks backwards and open
files with asynchronous read/write/delete sharing, so paging does not load whole files, block a
caller thread, or block rolling. Physical lines and multiline continuations are bounded by
`MaxEntryBytes`; retained oversized entries carry `IsTruncated = true`, and partial UTF-8 code points at
either retained boundary are removed. An unterminated active-file tail is deferred until its newline arrives.

Sequences use an explicit positive 63-bit layout: 22 bits for the `DateOnly` day number, 11 bits for
roll indexes `0..2047`, and 30 bits for byte offsets. The 30-bit offset is why `MaxFileSizeMb` is
capped at 1024. A manually supplied file whose roll index or entry offset is outside that layout is
rejected with `InvalidDataException` rather than producing a colliding cursor.

`ILogFileAccess` is the replaceable seam for directory discovery and shared file opening. The default
registration delegates to the local file system; `TryAddSingleton` leaves a host-provided
implementation in place.

File naming itself is deliberately internal. DI gives the writer and reader one singleton that both formats
and parses `app-{yyyy-MM-dd}[_NNN].log`. Public direct construction cannot accept that internal service, so
the public provider and reader constructors use the same package default instead; this keeps direct tests
simple without exposing arbitrary implementation DI as public API.

The package does not redact messages or exceptions, encrypt files, harden directory permissions, or
authorize reads. Treat `LogDirectory` and a replacement `ILogFileAccess` as trusted host configuration,
secure the directory for the process identity, and put authentication/authorization in front of every
viewer that exposes `ILogFileReader`; its query filters are not access controls.

## Documentation

Topic page: [docs/logging.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/logging.md).
Conventions: [docs/package-conventions.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/package-conventions.md).
