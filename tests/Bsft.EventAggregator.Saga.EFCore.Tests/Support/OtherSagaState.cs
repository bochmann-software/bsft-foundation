namespace Bsft.EventAggregator.Saga.EFCore.Tests.Support;

/// <summary>A second, unrelated state type, used to prove a stored state is not read as another.</summary>
public sealed class OtherSagaState
{
    public string Name { get; set; } = string.Empty;
}
