namespace Bsft.Hosting.BackgroundWork.Tests.Pipelines.Fakes;

/// <summary>The item the pipeline tests carry.</summary>
/// <param name="Id">Identifies the item.</param>
/// <param name="Tag">What an enricher adds.</param>
internal sealed record SampleItem(int Id, string? Tag = null);
