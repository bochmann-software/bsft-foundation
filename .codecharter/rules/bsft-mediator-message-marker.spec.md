# Command without its mediator marker interface

The rule checks `*Command` and deliberately not `*Query`. `Query` has a second,
older meaning in this tree - the object an endpoint binds its HTTP query string to -
and seventeen types use it that way (`ListChatMessagesQueryParameters`, `AgentRunListQueryParameters`,
`WorkItemListQuery` and their kin). Those are argument shapes, not messages, and no
expression separates them from a mediator query by anything the model holds. The
`*Command` half has one meaning and is enforced; the `*Query` half stays a review
item, which is written up in `docs/package-conventions.md`.

Static classes are excluded because a helper holding query-building code is not a
message either.

## hits

### a command with no marker

```csharp
public sealed record CreateOrderCommand(string Name);
```

### a command with an unrelated interface

```csharp
public interface IHaveTenant { }
public sealed record ArchiveOrderCommand(int Id) : IHaveTenant;
```

## misses

### a command carrying the marker

```csharp
public interface ICommand<TResult> { }
public sealed record CreateOrderCommand(string Name) : ICommand<int>;
```

### a command with no result

```csharp
public interface ICommand { }
public sealed record ArchiveOrderCommand(int Id) : ICommand;
```

### an endpoint's query-string binding shape

```csharp
public sealed record ListChatMessagesQueryParameters(int Page, int Size);
```

### a parameter object named as one

```csharp
public sealed record CreateOrderRequest(string Name);
```

### a static helper that only reads like a message

```csharp
public static class WorkflowCommand
{
    public static int Build() => 0;
}
```
