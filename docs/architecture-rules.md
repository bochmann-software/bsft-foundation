# Architecture Rules

The rules a Bsft application is expected to hold to. They are conventions, not compiler-enforced
constraints, but the framework is built so that following them is the path of least resistance.

## Rule 1 — one direction of flow

```
Event → Saga Step → Command → Handler → Event
```

A saga step reacts to an event and dispatches commands. Business logic lives in the handler behind
the command, never in the step and never in the event. A saga is an orchestrator: it decides *what
happens next*, not *how it happens*.

Consequences:

- A step must not call a repository, write to the database or talk to an external system directly.
- A handler must not know that a saga exists. It answers with an event; whether a saga listens is
  none of its business.
- **A durable timeout publishes an event and does nothing else.** The timer is a scheduling
  mechanism, not a place for logic — see [saga-timeouts.md](saga-timeouts.md).

## Rule 2 — state belongs to its owner

Saga state describes the progress of the orchestration, not the business entity. Order totals,
stock levels and customer data live in the module that owns them; the saga only keeps what it needs
to route the next step.

## Rule 3 — a module owns its tables

A module sees its own tables through its own DbContext interface. The application's DbContext
implements those interfaces; a module never reaches into another module's tables, it asks through
mediator or events.

## Rule 4 — time comes from `TimeProvider`

No `DateTime.UtcNow` anywhere in framework or application code. Anything time-dependent must be
testable by advancing a clock rather than by waiting.

## Rule 5 — at-least-once means idempotent

Anything delivered by the outbox dispatcher or the saga timeout dispatcher can arrive twice. The
handling of such an event must be idempotent. The framework guarantees that a duplicate does not
corrupt the saga; it cannot guarantee that a duplicate e-mail is not sent.

## Rule 6 — a CRUD resource looks the same everywhere

A resource that is edited directly is exposed as `GET`/`POST /api/{resource}` and
`GET`/`PUT`/`DELETE /api/{resource}/{id}`, and every failure is thrown rather than returned, so
`BsftExceptionMiddleware` maps it to the documented status code — 400 validation, 403 forbidden,
404 not found, 409 conflict, 422 business rule. The routes, the bodies, the status codes and the
placement of validation are one convention rather than a per-module choice, because a generic
client can only be written once. See [crud-conventions.md](crud-conventions.md); the reference
implementation is the `products` resource in `samples/Bsft.Sample.Inventory`.
