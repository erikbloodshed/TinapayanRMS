using TinapayanRMS.Components.Shared;

namespace TinapayanRMS.Components.Tests.Fixtures;

/// <summary>The file dialogs for a test: Save answers with <see cref="SaveAnswer"/> and remembers what it suggested.</summary>
public sealed class FakeFileDialogs : IFileDialogs
{
    /// <summary>Where the user chooses to save; null cancels the dialog.</summary>
    public string? SaveAnswer { get; set; }

    public List<(string FileName, string Kind)> SavesAsked { get; } = [];

    public Task<string?> ChooseSaveFileAsync(string fileName, string kind)
    {
        SavesAsked.Add((fileName, kind));
        return Task.FromResult(SaveAnswer);
    }

    public Task<string?> ChooseFileToOpenAsync(string extension, string kind) => throw new NotSupportedException();

    public Task<string?> ChooseFolderAsync() => throw new NotSupportedException();

    public void ShowFolder(string folder) => throw new NotSupportedException();
}
