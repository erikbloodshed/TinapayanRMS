using Microsoft.AspNetCore.Components.Web;
using Syncfusion.Blazor.Data;
using Syncfusion.Blazor.DropDowns;

namespace TinapayanRMS.Components.Shared;

/// <summary>
/// PickField's combo box: Syncfusion's, listing the best match for what's typed first and highlighting it, and saying
/// when it's left with typed text. Left to itself it marks the item the box held when that item is still listed
/// (a line's All Purpose Flour, with "purpose" typed) and highlights nothing, though the best match (All Purpose Cream)
/// is what Enter and Tab take.
/// </summary>
public sealed class PickComboBox<TItem> : SfComboBox<int?, PickField<TItem>.Choice>
{
    /// <summary>The best match for what's typed, set as it's typed (PickField.OnFiltering); none with nothing typed.</summary>
    public PickField<TItem>.Choice? BestMatch { get; set; }

    /// <summary>
    /// Whether the box was left, or Enter pressed, with text other than its item's name: what Tab takes the best match
    /// for. Escape puts the name back first, so after it the item stays. PickField clears it as the box takes the cursor.
    /// </summary>
    public bool LeftWithTypedText { get; set; }

    // Every list it shows is sorted best match first; among equals, in the order the items came in. Given as its Query
    // parameter instead, the sort was taken in on the box's first redraw, as the first letter was typed into it, and
    // the list it then reloaded in full lost that letter's filter.
    protected override Query GetQuery(Query query) =>
        base.GetQuery(query).Sort(nameof(PickField<TItem>.Choice.Rank), "Ascending");

    protected override async Task SearchListAsync(KeyboardEventArgs arguments = null!, bool isTabAction = false)
    {
        await base.SearchListAsync(arguments, isTabAction);
        if (BestMatch is { } best && ListDataSource?.Any(option => Equals(option.CurrentItemData, best)) == true)
        {
            await UpdateListSelectionAsync(best, "e-item-focus");
            StateHasChanged();
        }
    }

    // Called with text other than the item's name as the box is left, or Enter finds nothing highlighted; and with
    // isValidateInput as it's typed, which only a form's validation asks for.
    protected override async Task CustomValueAsync(bool isValidateInput = false, KeyActions arguments = null!)
    {
        if (!isValidateInput)
        {
            LeftWithTypedText = true;
        }

        await base.CustomValueAsync(isValidateInput, arguments);
    }
}
