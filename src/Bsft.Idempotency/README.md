# Bsft.Idempotency

Makes an unsafe HTTP endpoint safe to retry: a client sends an `Idempotency-Key`, and a repeat of
that request replays the first response instead of running again.

## Install

```bash
dotnet add package Bsft.Idempotency
```

Requires `Bsft.Core`, `Bsft.Http`, `Bsft.Data` and `Bsft.Instrumentation` (pulled transitively) and
ASP.NET Core. `Bsft.Idempotency.EFCore` is the shared record store a host running more than one
replica needs.

## Register

```csharp
services.AddBsftIdempotency(options =>
{
    options.ShouldRequireKey = true;
    options.Retention = TimeSpan.FromHours(24);
});

// or bound from configuration, section "Bsft:Idempotency"
services.AddBsftIdempotency(configuration);
```

The pipeline half is a second call, and its position matters: `app.UseBsftIdempotency()` goes after
`UseBsftDefaults()`, after `UseRouting()` and after authentication. Then opt an endpoint in with
`[Idempotent]` on a controller action or `.RequireIdempotency()` on a minimal-API route group.

## Configure

| Option | Default | Effect |
| --- | --- | --- |
| `BsftIdempotencyOptions.HeaderName` | `Idempotency-Key` | The request header the key is read from. |
| `BsftIdempotencyOptions.Methods` | `POST` | Which methods the middleware considers. |
| `BsftIdempotencyOptions.ShouldRequireKey` | `false` | When true, an opted-in endpoint without a key answers 428. |
| `BsftIdempotencyOptions.Retention` | 24 h | How long a stored response is replayed. |
| `BsftIdempotencyOptions.InProgressTimeout` | 60 s | After which an unfinished reservation is retryable. |
| `BsftIdempotencyOptions.InProgressRetryAfter` | 1 s | The `Retry-After` on the in-progress 409, capped by the lease. |
| `BsftIdempotencyOptions.MaxBodyBytes` | 1 MiB | Above which the request is refused with 413 rather than left unprotected. |
| `BsftIdempotencyOptions.MaxResponseBytes` | 1 MiB | Above which the response is sent but not stored, so the key runs again. |
| `BsftIdempotencyOptions.MaxKeyLength` | 255 | Longest key accepted; at most 512, the shared store's column. |
| `BsftIdempotencyOptions.CachedResponseHeaders` | `Location`, `Content-Type` | The allowlist replayed with the body. |
| `BsftIdempotencyOptions.NonReplayableStatusCodes` | 401, 403, 408, 429 | Client errors that run again instead of being replayed. |
| `BsftIdempotencyOptions.Mode` | `Detached` | `Detached` or `Enlisted`; see the guarantee table on the topic page. |

Validation: `ValidateOnStart` rejects a blank header name, an empty method list, a non-positive
window, a `MaxKeyLength` outside 1–512, and an `InProgressTimeout` longer than `Retention` — the
last one because a reservation that outlived the replay window would be pruned while it was still
refusing concurrent requests.

## Extend

- `IIdempotencyStore` — where records live. The default is in-process and is not enough for two
  replicas; `Bsft.Idempotency.EFCore` replaces it with a shared table.
- `IIdempotencyFingerprintContributor` — adds or removes a segment of the request fingerprint.
- `IIdempotencyAuditSink` — receives replay, key-reuse and in-progress decisions. The default
  discards them.

## Documentation

Topic page: [docs/idempotency.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/idempotency.md).
Conventions: [docs/package-conventions.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/package-conventions.md).
