namespace TinapayanRMS.Presentation.ViewModels;

/// <summary>
/// A screen with an editable Category picker whose typed text is settled once the picker is left, or Enter is pressed
/// in it (controls:CategoryTextCommit): the category of that name is picked, and a name that isn't one yet brings up a
/// prompt to add it. Raw Materials and Products.
/// </summary>
public interface ICategoryPickerScreen
{
    /// <summary>
    /// The picker took the cursor. Typing a name it doesn't list clears its pick (ComboBoxAdv's Suggest mode), so the
    /// category it holds now is remembered here, for a cancelled prompt to put back.
    /// </summary>
    void BeginCategoryEdit();

    /// <summary>
    /// Settles what's typed: the category of that name, whatever its case, is picked; a name that isn't one yet asks
    /// whether to add it. Nothing typed leaves the picker with no category.
    /// </summary>
    Task CommitCategoryTextAsync();
}
