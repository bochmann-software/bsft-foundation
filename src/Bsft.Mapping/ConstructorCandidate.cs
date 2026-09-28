using System.Reflection;

namespace Bsft.Mapping;

/// <summary>
/// One public constructor of a destination type with the compiled call that invokes it.
/// </summary>
/// <param name="Constructor">The constructor itself, which a projection builds a call to as an expression.</param>
/// <param name="Parameters">The constructor's parameters, in declaration order.</param>
/// <param name="ParameterNames">
/// The names of those parameters, which are the members the constructor takes rather than leaves to
/// the mapper to assign afterwards.
/// </param>
/// <param name="Create">Calls the constructor with the arguments its parameters were resolved to.</param>
internal sealed record ConstructorCandidate(
    ConstructorInfo Constructor,
    IReadOnlyList<ParameterInfo> Parameters,
    IReadOnlySet<string> ParameterNames,
    Func<object?[], object> Create);
