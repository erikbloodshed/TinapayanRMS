using Microsoft.AspNetCore.Components;
using TinapayanRMS.Presentation.Shared;

namespace TinapayanRMS.Components.Shared;

/// <summary>
/// A record screen with an address per record (products/42, raw-materials/7, sub-assemblies/3), so the browser's Back and
/// Forward step between the records opened: the record in the address opens as picking it in the list would, and the
/// record picked goes into the address. Products, Raw Materials and Sub-Assemblies; each names its address and record, and
/// its state opens one (IRecordOpener).
/// </summary>
public abstract class RecordScreenBase<TState> : ScreenBase<TState>
    where TState : ScreenState, IRecordOpener
{
    /// <summary>The address last acted on: its record is opened once, not each time the screen is given it again.</summary>
    private int? _addressOpened;

    /// <summary>The record in the address, opened as picking it in the list would.</summary>
    [Parameter] public int? Id { get; set; }

    /// <summary>The screen's address, "products": a record's is it with the record's id after it.</summary>
    protected abstract string AddressRoot { get; }

    /// <summary>The id of the record the form shows, or null for none; a new one's is 0.</summary>
    protected abstract int? ShownId { get; }

    /// <summary>The name of the screen state's property for the record shown (SelectedProduct and the like).</summary>
    protected abstract string ShownProperty { get; }

    // The record in the address is the load's to open, at its end. New clicked meanwhile is the later word.
    protected override void OnInitialized()
    {
        base.OnInitialized();
        _addressOpened = Id;
        if (Id is { } id && ShownId != id)
        {
            State!.OpenRecordOnLoad(id);
        }
    }

    // An address that changed: Back, Forward, or another screen's link. Not the one the screen opened on, which this is
    // called for too, once the load is back: New clicked before then would have the record load over it.
    protected override void OnParametersSet()
    {
        if (Id == _addressOpened)
        {
            return;
        }

        _addressOpened = Id;
        if (Id is { } id && ShownId != id)
        {
            State!.OpenRecord(id);
        }
    }

    // The record picked goes into the address, so Back returns to the one before. Called on whichever thread changed it.
    protected override void OnStateChanged(string? propertyName)
    {
        if (propertyName == ShownProperty && ShownId is { } id and > 0 && id != Id)
        {
            _ = InvokeAsync(() => MoveAddressTo($"{AddressRoot}/{id}"));
        }
    }

    protected override bool IsOwnAddress(string path) => path.StartsWith(AddressRoot, StringComparison.OrdinalIgnoreCase);
}
