# Bsft.Messaging.RabbitMQ

The RabbitMQ transport of the `Bsft.Messaging` family: both halves of the wire, the topology the adapter
declares for itself on every start, publisher confirms on the way out and a dead-letter park on the way
in.

## Install

```bash
dotnet add package Bsft.Messaging.RabbitMQ
```

Requires `Bsft.Messaging.Abstractions` and `RabbitMQ.Client`, and no further Bsft packages — the adapter
is written against the two transport contracts alone and knows nothing about the inbox, the type registry
or a handler. A host that consumes also references `Bsft.Messaging` for the pipeline that reads the
source.

## Register

```csharp
services.AddBsftRabbitMQMessaging(configuration, options => options.Subscribe("orders.placed.v1"));
services.AddBsftMessaging(configuration, options => options.AddEvent<OrderPlaced>("orders.placed.v1"));
services.AddBsftConsumerHost();

// or entirely in code, credentials included — which is what the configuration overload exists to avoid
services.AddBsftRabbitMQMessaging(options =>
{
    options.ApplicationName = "orders";
    options.UserName = "...";
    options.Password = "...";
    options.Subscribe("orders.placed.v1");
});
```

Call it **before** `AddBsftMessaging`. The messaging core registers its in-process bus and the loopback
its source with `TryAdd`, so whichever ran first wins; calling this afterwards leaves the application
silently delivering its integration events to itself. Every consumed event is declared twice on purpose:
`Subscribe` binds the queue on the broker, `AddEvent` resolves the name in this process. This registers
the transport and not the consuming pipeline — `AddBsftConsumerHost` adds the host that enumerates the
source.

## Configure

| Option | Default | Effect |
| --- | --- | --- |
| `BsftRabbitMQOptions.ApplicationName` | `""` | Identifies the connection on the broker and is the stem every unset name below is derived from. Required. |
| `BsftRabbitMQOptions.HostName` | `localhost` | The broker host name. |
| `BsftRabbitMQOptions.Port` | `5672` | The broker port. TLS does not move it; state both. |
| `BsftRabbitMQOptions.VirtualHost` | `/` | The virtual host to connect to. |
| `BsftRabbitMQOptions.UserName` | `null` | The user to connect as. Required; there is deliberately no fallback to the broker's development account. |
| `BsftRabbitMQOptions.Password` | `null` | That user's password. Required, and it belongs in configuration or a secret store rather than in code. |
| `BsftRabbitMQOptions.UseTls` | `false` | Whether the connection is made over TLS. Everything else about the handshake is left to the host's own .NET and OS trust configuration. |
| `BsftRabbitMQOptions.TlsServerName` | `null` | The name the broker's certificate is validated against; `null` uses `HostName`. It selects which name is verified, never switches verification off. |
| `BsftRabbitMQOptions.NetworkRecoveryInterval` | `00:00:05` | How long the client waits between attempts to re-establish a dropped connection. |
| `BsftRabbitMQOptions.Heartbeat` | `00:01:00` | The heartbeat interval; roughly half of it is how long a consumer can be subscribed to a socket that no longer exists. |
| `BsftRabbitMQOptions.ContinuationTimeout` | `00:00:20` | How long one of the client's own protocol calls may take. Not the confirm deadline. |
| `BsftRabbitMQOptions.ExchangeName` | `{ApplicationName}.events` | The exchange this application publishes to. |
| `BsftRabbitMQOptions.ExchangeType` | `topic` | The AMQP type that exchange is declared as. Only `topic` and `direct` are accepted, because the routing key is the event name. |
| `BsftRabbitMQOptions.DeadLetterExchangeName` | `{ExchangeName}.dlx` | The exchange undeliverable messages are routed to. |
| `BsftRabbitMQOptions.QueueName` | `{ApplicationName}.queue` | This application's queue. Every instance consumes from it, so the instances share the load. |
| `BsftRabbitMQOptions.DeadLetterQueueName` | `{QueueName}.dlq` | The queue behind the dead-letter exchange. |
| `BsftRabbitMQOptions.DeadLetterQueueMessageTtl` | `null` | How long a message may sit in the dead-letter queue. Maps to `x-message-ttl`; what it discards is the last copy of an event. |
| `BsftRabbitMQOptions.DeadLetterQueueMaxLength` | `null` | How many messages that queue holds. Maps to `x-max-length`, dropping from the head. |
| `BsftRabbitMQOptions.QueueType` | `Quorum` | Quorum because it is the only queue type that stamps `x-delivery-count`, which is where `IMessageDelivery.DeliveryAttempt` comes from. |
| `BsftRabbitMQOptions.PrefetchCount` | `20` | How many unacknowledged messages the broker may have outstanding per consumer. The only backpressure the consuming side has. |
| `BsftRabbitMQOptions.PublishChannelCount` | `1` | How many channels the publishing side spreads its publishes over, round-robin. |
| `BsftRabbitMQOptions.PublisherConfirmTimeout` | `00:00:30` | How long a publish waits for the broker's confirmation before it is treated as failed. |
| `BsftRabbitMQOptions.ContentType` | `application/json` | What published messages are labelled with and consumed ones are checked against. |

`Subscribe(eventName)` adds a binding of the queue to an event name; it is a method rather than a
property because declaring the same name twice has to be ignored rather than doubled.

Validation: `ValidateOnStart()` resolves the connection settings and the topology and reports everything
they refuse in one go — an absent application name, host, virtual host, content type, user or password;
a port outside the TCP range; a prefetch of zero, which AMQP reads as an unlimited prefetch; a publish
channel count below one; a non-positive confirm timeout, recovery interval, heartbeat or continuation
timeout; a name that cannot be derived or exceeds the 255 characters AMQP allows; a dead-letter object
that is the object it protects; a non-positive dead-letter bound; and an exchange type that does not
route on the routing key.

## Extend

None. Every seam this adapter has toward the framework is declared in
[`Bsft.Messaging.Abstractions`](../Bsft.Messaging.Abstractions/README.md) — it implements `IMessageBus`,
`IMessageSource` and `IMessageDelivery` and consumes nothing else. Its own interfaces under
`Abstractions/` (`IRabbitMQChannel`, `IRabbitMQConnection`, `IRabbitMQTopologyDeclarer`, and the rest) are
`internal`: they exist so the property mapping, the confirm handling, the topology declaration and the
acknowledgement decisions are testable without a broker, not as a public extension point. A host that
wants different behaviour replaces the whole adapter by registering its own `IMessageBus` and
`IMessageSource`.

The two public types beside the options are `RabbitMQQueueType`, which is what `QueueType` is set from,
and `RabbitMQMessageFormatException`, which reports a delivery that never became an envelope — parked
immediately rather than retried, because the same bytes would be exactly as unreadable next time.

## Documentation

Topic page: [docs/messaging.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/messaging.md).
Conventions: [docs/package-conventions.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/package-conventions.md).
