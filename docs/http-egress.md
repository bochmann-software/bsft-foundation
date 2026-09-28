# Bsft.Http.Egress

The SSRF defence for outbound HTTP, as one seam: which addresses this application
may open a connection to, and the connect-time guard that makes that answer hold
against a name whose answer changes between the check and the connection.

It references no Bsft package and carries no ASP.NET Core reference, because it
is the outbound half of an HTTP call: a console worker or a dependency-light
client references it without acquiring the server stack. The `Bsft.Http` prefix
names the concern rather than a dependency — `Bsft.Http` itself is entity tags
and conditional requests on the inbound edge, and nothing here builds on it.

## Why a package rather than a copy per caller

Every feature that lets somebody else nominate a URL has the same problem. A
webhook subscription names an endpoint the subscriber controls; a connector
profile names an authority an administrator supplied; a host's own integration
takes a callback URL from a tenant. In each case the application makes a request,
from inside its own network, to an address it did not choose — which is the whole
of server-side request forgery.

The rule that answers it is not hard to write once and very easy to write badly
twice. It is a deny list over address ranges — loopback, link-local and the cloud
metadata address at `169.254.169.254`, the private ranges, carrier-grade NAT, the
documentation and benchmarking ranges, multicast, and the IPv6 forms of all of
them including the ones that carry an IPv4 address inside a translation prefix.
A second copy that forgets `::ffff:127.0.0.1`, or `2002:a9fe:a9fe::1`, is a copy
that looks identical in review and is not. The rule lives here so there is one.

## The two layers

```csharp
services.AddBsftHttpEgress();

services.AddHttpClient("provider")
    .AddBsftHttpEgressGuard(handler => handler.AllowAutoRedirect = false);
```

`AddBsftHttpEgressGuard` puts two things on the client, and they are not
interchangeable:

1. **`EgressGuardHandler`, on the handler chain.** It judges the request URL
   before the request is sent: the scheme, a single-label host name, and an
   address the URL names outright. It resolves nothing. What it buys is a
   refusal that names the request rather than a connection that mysteriously
   never opens.
2. **The connect callback, on the primary handler.** It resolves the host
   itself, judges the addresses, and connects to *an address it judged* — never
   handing the name back to the socket to resolve a second time.

The second layer is the one that matters. Between a destination being accepted
and a request going out, whoever supplied it controls what their name resolves
to: a name that answered with a public address while the form was being
validated can answer with `169.254.169.254` an hour later. That is DNS
rebinding, and no amount of validating the URL prevents it. A check followed by a
re-resolution is the same race one layer down, which is why the guard connects by
address.

Turning redirects off belongs beside both, for the same reason: following a
redirect means the address that was checked is not the address that receives the
request.

The handler lifetime is left at its default. A pooled handler that is recycled
periodically is what keeps a DNS change from being pinned by a live connection,
which matters more here than anywhere else in an application.

## Configuration

| Option | Default | Effect |
| --- | --- | --- |
| `AllowPrivateNetworks` | `false` | Lets outbound requests reach loopback, link-local and the private ranges. |
| `AllowInsecureUrls` | `false` | Lets outbound requests use plain `http`. |
| `AllowedHosts` | empty | Bare host names exempt from the single-label rule and the address rules together. |

Section `Bsft:Http:Egress`. The first two are development overrides and belong in
`appsettings.Development.json` and nowhere else. The third is the deliberate
hole: an internal service the application is *meant* to call, named by the
operator rather than by whoever supplied the destination. It is matched on the
name and not on what the name resolves to, so a listed name is trusted for as
long as it is listed — which is why an entry that is not a bare host name (a URL
pasted in, a name with a port) fails the host at start-up rather than silently
matching nothing.

## Seams

- `IOutboundAddressPolicy` — decides whether a URL, a host or a resolved address
  may be called, and says why not. Replace it when your network has ranges the
  shipped rules do not know about.
- `IOutboundConnectGuard` — resolves and connects. Replacing it means taking on
  the rebinding problem yourself.
- `IEgressAddressResolver` — says where a host points right now. The default is
  the system resolver, and hands a literal address straight back rather than
  resolving it.

`OutboundAddressRules` is public for the caller that reports its refusals in its
own words: a create-time validator that has to answer a person filling in a form
asks these two predicates rather than writing the ranges out again.

## Who uses it

**`Bsft.Webhooks`** consumes the rules and the connect guard, under its own
`AllowInsecureUrls` and `AllowPrivateNetworks` and speaking its own
`WebhookAddressRejectedException` — the sender recognises that exception and
gives a delivery up for good rather than retrying an address that will not become
acceptable. Its options are not read from `Bsft:Http:Egress` on purpose: a
webhook destination is chosen by a subscriber, and widening the policy for other
outbound traffic must not widen it for deliveries. See `Bsft.Webhooks`.

**`Bsft.Connectors`** guards both of its named clients — the authority client and
the pooled API chain every tenant's provider call runs through — with redirects
off. It is on by default and switched with
`BsftConnectorsOptions.EnableEgressGuard`; the better answer for a provider
that genuinely lives on your own network is an `AllowedHosts` entry rather than
the switch. See `Bsft.Connectors`.
