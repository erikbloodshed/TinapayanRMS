namespace TinapayanRMS.Presentation.RawMaterials;

/// <summary>
/// Settings' cost buffer for a new material moved while a new material is open (RawMaterialDraft.TakeDefaultCostBuffer):
/// <paramref name="From"/> the old default, <paramref name="To"/> the new.
/// </summary>
public sealed record CostBufferDefaultMoved(decimal From, decimal To)
{
    /// <summary>
    /// An undo or redo step, or what Save measures against, as of the new default: a buffer on the old one takes the new, as on
    /// a form still showing it, so no undo or redo brings the old one back; any other stays, being the user's.
    /// </summary>
    public RawMaterialFormSnapshot Carry(RawMaterialFormSnapshot snapshot) =>
        IsOnDefault(snapshot.CostBufferPercent, snapshot.TargetCostBufferPercent, snapshot.BufferedCost, From)
            ? snapshot with { CostBufferPercent = To, TargetCostBufferPercent = To }
            : snapshot;

    /// <summary>
    /// A buffer is on <paramref name="defaultPercent"/> as a new material starts on it: the Cost Buffer % and its target at it,
    /// with no fixed buffered cost, which the % would follow instead. One typed over a price fixes a buffered cost, as one
    /// typed as an amount does, and is the user's: moved to a new default, the % would only go back to what that cost comes to.
    /// </summary>
    internal static bool IsOnDefault(decimal? percent, decimal? target, decimal? bufferedCost, decimal defaultPercent) =>
        percent == defaultPercent && target == defaultPercent && bufferedCost is not > 0m;
}
