namespace Bsft.Data.Tests.Support;

/// <summary>
/// An entity with an object-typed property. It exercises the branch in EntityMemberBinder that
/// takes an entity expression whose body is already typed as object and returns it unchanged,
/// because there is no Convert around it to strip.
/// </summary>
internal class ObjectPropEntity
{
    public object? ObjProp { get; set; }
}
