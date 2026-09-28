namespace Bsft.Mediator.StartupValidation;

/// <summary>
/// One type found in a configured assembly, paired with the interfaces it implements. The interface
/// array is carried alongside the type because every startup check asks for it and
/// <see cref="Type.GetInterfaces"/> walks the type's whole hierarchy on each call.
/// </summary>
/// <param name="Type">The type found in the assembly.</param>
/// <param name="Interfaces">The interfaces the type implements, reflected once.</param>
internal sealed record ScannedType(Type Type, IReadOnlyList<Type> Interfaces);
