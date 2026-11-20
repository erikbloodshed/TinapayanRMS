using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.Presentation.Shared;

/// <summary>
/// What Move to In-House asks on the Products screen for a product's own section, and Make In-House Ingredient on the
/// Sub-Assemblies screen for a recipe there: the made ingredient's name, and the Made category it goes in, read fresh each
/// time by the screen's queries (ReadMadeCategoriesAsync), so one added on the Raw Materials screen since is offered at once.
/// </summary>
public static class MadeIngredientPrompt
{
    /// <summary>The category the picker starts on when there's one by this name: the shop's own.</summary>
    public const string DefaultCategoryName = "In-House";

    public const string NoMadeCategoryMessage =
        "There's no Made category to put it in. Add one on the Raw Materials screen first, with New Made Category.";

    /// <summary>
    /// The prompt, titled <paramref name="title"/>, its name box starting with <paramref name="initialName"/> and its picker
    /// on In-House when there's one.
    /// </summary>
    public static ChoicePromptRequest RequestFor(string title, string initialName, IReadOnlyList<RawMaterialCategory> categories, string message)
    {
        var inHouse = categories.ToList().FindIndex(c => string.Equals(c.Name, DefaultCategoryName, StringComparison.OrdinalIgnoreCase));
        return new ChoicePromptRequest(
            title, "Name", initialName, "Category", [.. categories.Select(c => c.Name)], Math.Max(inHouse, 0), message);
    }

    /// <summary>A name another raw material has, or another section is moving under, refused before anything is saved.</summary>
    public static string NameTakenMessage(string name) => $"There's already a raw material named '{name}'. Give the made ingredient its own name.";
}
