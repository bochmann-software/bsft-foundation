namespace Bsft.EventAggregator.Saga.EFCore;

/// <summary>
/// The column widths the two saga tables share. They are declared once because they have to agree:
/// a saga instance is addressed by the same type and correlation key in both tables, so a key that
/// fits one and not the other would let an instance exist that could never carry a timer. Two copies
/// of the number would let that agreement lapse without anything noticing.
/// </summary>
internal static class SagaColumnLengths
{
    /// <summary>Column length of the saga type, indexed in both tables.</summary>
    public const int SagaType = 200;

    /// <summary>Column length of the correlation key, indexed in both tables.</summary>
    public const int CorrelationKey = 200;

    /// <summary>Column length of an assembly-qualified type name, whether of a state or a payload.</summary>
    public const int TypeName = 500;
}
