# Bsft.Calendar

The business-day arithmetic the framework's delays are computed with: a keyed registry of calendars
and the provider that resolves one, so "in two days" can mean 48 hours in one branch of a company and
the next working day in another.

## Install

```bash
dotnet add package Bsft.Calendar
```

No further Bsft packages; it depends on `Microsoft.Extensions.Options`, the DI abstractions and
`Microsoft.Extensions.Hosting.Abstractions`, which is where the `ValidateOnStart()` the registration
call makes comes from. It is pulled in by `Bsft.EventAggregator.Saga`, which schedules saga timeouts
through it, and by `Bsft.Workflow`, which dates a step's deadline with it.

## Register

```csharp
services.AddBsftCalendar(options =>
{
    options.AddCalendar("berlin", new WeekdayBusinessCalendar(
        nonWorkingDays: null,
        timeZone: TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin")));
    options.Default = ContinuousBusinessCalendar.Instance;
});
```

Nothing else is required. Repeated calls are additive: each one contributes another configuration
step to the same options instance, so a package registering its defaults never discards the calendars
the application registers afterwards, whichever order they run in. There is no `IConfiguration`
overload — see below.

## Configure

`BsftCalendarOptions` has **no** `SectionName` and the entry point has no `IConfiguration` overload.
Every member of it is an `IBusinessCalendar` implementation chosen in code, and a configuration
provider has nothing to bind to one. A host that drives its calendars from configuration reads its
own section and calls `AddCalendar` with the instances it built.

| Option | Default | Effect |
| --- | --- | --- |
| `BsftCalendarOptions.Default` | `ContinuousBusinessCalendar.Instance` | The calendar used when a caller passes no key; every day is a working day. |
| `BsftCalendarOptions.Calendars` | empty | The keyed registry, contributed to through `AddCalendar(key, calendar)`. |

Validation: a default calendar must be present. A failure names the member, and it is reported on
start.

## Extend

`IBusinessCalendar` (decides whether a moment is working time and advances a delay across
non-working time; `ContinuousBusinessCalendar` and `WeekdayBusinessCalendar` are the two shipped,
and a host implements it for public holidays, shift patterns or site-specific rules).
`IBusinessCalendarProvider` (resolves a key to a calendar) is registered with `TryAdd`, so a host
that registered its own resolution strategy keeps it. Public holidays are deliberately not in the
framework: they differ per country, per federal state and often per site.

## Documentation

Topic page: [docs/calendar.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/calendar.md).
Conventions: [docs/package-conventions.md](https://github.com/bochmann-software/bsft-foundation/blob/main/docs/package-conventions.md).
