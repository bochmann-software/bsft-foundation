# Bsft.Http.Egress

The SSRF defence for outbound HTTP: one policy for where this application may
connect to, and the connect-time guard that makes it hold against DNS rebinding.
It stands on its own — the `Bsft.Http` prefix says which concern it belongs to,
not which package it depends on: it references no Bsft package at all, so a
console worker or a dependency-light client can take it by itself.

## Install

```bash
dotnet add package Bsft.Http.Egress
```

No further Bsft packages. It references `Microsoft.Extensions.Http`, the options
packages and nothing from ASP.NET Core, because it is the outbound half of an
HTTP call.

## Register

```csharp
services.AddBsftHttpEgress(options =>
{
    options.AllowedHosts.Add("mail-relay.internal");
});

// or bound from configuration, section "Bsft:Http:Egress"
services.AddBsftHttpEgress(configuration);
```

Registering the policy does not guard anything by itself. A client is guarded on
its own builder, which puts the request-time handler on the chain and the
connect-time guard on the primary handler:

```csharp
services.AddHttpClient("provider")
    .AddBsftHttpEgressGuard(handler => handler.AllowAutoRedirect = false);
```

Turning redirects off is worth doing on any client that calls a destination
somebody else chose: following a redirect means the address that was checked is
not the address that receives the request.

## Configure

| Option | Default | Effect |
| --- | --- | --- |
| `BsftHttpEgressOptions.AllowPrivateNetworks` | `false` | Lets outbound requests reach loopback, link-local and the private ranges. A development override. |
| `BsftHttpEgressOptions.AllowInsecureUrls` | `false` | Lets outbound requests use plain `http`. A development override. |
| `BsftHttpEgressOptions.AllowedHosts` | empty | Bare host names that are exempt from the single-label rule and the address rules together. |

Validation: an allow-list entry that is blank, or that carries a scheme, a port
or a path rather than being a bare host name, fails the host at start-up — such
an entry matches nothing and would read as a hole that was opened while it never
fires.

## Extend

- `IOutboundAddressPolicy` (decides whether a URL, a host or a resolved address
  may be called; the default applies the range rules, the two overrides and the
  allow list).
- `IOutboundConnectGuard` (resolves a host and connects to an address it judged;
  the default is what closes the DNS-rebinding race).
- `IEgressAddressResolver` (says where a host points right now; the default is
  the system resolver, and hands back a literal address unresolved).

## Documentation

Topic page: [docs/http-egress.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/http-egress.md).
Conventions: [docs/package-conventions.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/package-conventions.md).
