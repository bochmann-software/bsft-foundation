namespace Bsft.Data.Tests.Support;

/// <summary>
/// A DTO carrying a field that TestEntity does not have, so the default entity expression cannot be
/// built for it and the builder has to answer with nothing.
/// </summary>
/// <param name="Id">The identifier, which does map.</param>
/// <param name="Extra">The field that does not.</param>
internal sealed record DtoWithExtraField(int Id, string Extra);
