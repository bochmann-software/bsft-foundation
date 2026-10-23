namespace Bsft.Hosting.BackgroundWork.Pipelines;

/// <summary>How an operation that was raced against a time limit and an abort token ended.</summary>
internal enum RacedStatus
{
    /// <summary>It finished and produced its value.</summary>
    Completed,

    /// <summary>It threw, or was cancelled by something other than the limit.</summary>
    Faulted,

    /// <summary>The limit or the abort token passed first and the operation was abandoned.</summary>
    GaveUp,
}
