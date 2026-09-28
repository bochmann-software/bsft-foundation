# Business Calendar

`Bsft.Calendar` answers one question: what does "in two days" mean? By default it means 48 hours. In
a business process it usually means two working days, and a quote sent on Friday afternoon must not
be chased on Sunday morning.

The package is deliberately separate from the saga engine — working-day arithmetic is not saga
domain, and other components can use it too.

## Contract

```csharp
public interface IBusinessCalendar
{
    bool IsBusinessDay(DateOnly date);
    DateTimeOffset Add(DateTimeOffset from, TimeSpan delay);
}
```

`Add` treats the delay as *business time*: it only accrues on days for which `IsBusinessDay` returns
true. Friday noon plus two business days is Tuesday noon. A non-positive delay returns the starting
instant unchanged.

## Implementations

- `ContinuousBusinessCalendar` — every day is a working day. The default, and the right answer for
  anything not tied to office hours.
- `WeekdayBusinessCalendar` — skips a configurable set of weekdays, Saturday and Sunday by default,
  with the day boundaries read in a time zone of its own.

Calendars that walk day by day, `WeekdayBusinessCalendar` among them, cap a single delay at ten years
and reject anything longer: the walk costs CPU proportional to the delay's length, so an unbounded
one taken from business data would burn it. `ContinuousBusinessCalendar` adds the delay to the
starting instant in one step and needs no such cap.

### Which midnight ends the working day

A working day is a local notion, and the callers inside the framework hand the calendar a UTC
instant — `ScheduleTimeout` does. A `WeekdayBusinessCalendar` without a zone therefore delimits its
working days by **UTC midnight**, which is what the second constructor parameter exists to correct:

```csharp
var berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
o.AddCalendar("berlin", new WeekdayBusinessCalendar(nonWorkingDays: null, timeZone: berlin));
```

Left at UTC, a one business day reminder scheduled at Monday 00:30 Berlin time — Sunday 22:30 UTC —
counts as scheduled on the weekend, starts accruing at 02:00 local and fires an hour and a half
late, and the skew repeats at every weekend boundary. The zone only decides where one day ends and
the next begins; the instant handed in and the instant handed back are unaffected, and the result
keeps the offset it arrived with. A day that a daylight-saving transition makes 23 or 25 hours long
contributes exactly that much business time.

Omitting the parameter keeps UTC, so nothing changes for a calendar that does not ask for a zone.

Public holidays are **not** part of the framework. They differ per country, per federal state and
often per site; an application that needs them implements `IBusinessCalendar` itself and registers it
under a key.

## Registration

```csharp
services.AddBsftCalendar(o =>
{
    o.AddCalendar("berlin", new WeekdayBusinessCalendar(
        nonWorkingDays: null,
        timeZone: TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin")));
    o.AddCalendar("always-on", ContinuousBusinessCalendar.Instance);
});
```

Repeated calls are additive: `AddSagaEngine` already registers the default calendar options, and an
application calling `AddBsftCalendar` afterwards contributes another configuration step to that same
options instance rather than being silently ignored. Registration order does not matter.

`BsftCalendarOptions` is the package's primary options type but has no `SectionName` and
`AddBsftCalendar` has no `IConfiguration` overload: every member of it is an `IBusinessCalendar`
implementation chosen in code, and a configuration provider has nothing to bind to one. A host that
drives its calendars from configuration reads its own section and calls `AddCalendar` with the
instances it built.
Repeated calls are additive: `AddBsftSaga` already registers the default calendar options, and an
application calling `AddBsftCalendar` afterwards configures that same instance rather than being
silently ignored. Registration order does not matter.

`IBusinessCalendarProvider.Get(key)` resolves a calendar; `null` yields the default one. An unknown
key throws rather than falling back — silently computing a delay with the wrong working-day rules is
the kind of failure nobody notices for months.

## Resolution per instance, not per application

The key is passed at the call site, not fixed at startup:

```csharp
ctx.ScheduleTimeout("followup", TimeSpan.FromDays(2), new QuoteFollowUpDue(id), calendarKey: "berlin");
```

That is what allows two branches of the same company, running in the same process, to have different
working weeks. See [saga-timeouts.md](saga-timeouts.md).
