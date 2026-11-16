using System.Collections.ObjectModel;
using System.Globalization;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Domain.Enums;
using ILogger = Serilog.ILogger;
using TinapayanRMS.Presentation.Shared;

namespace TinapayanRMS.Presentation.App;

/// <summary>
/// One record Find can open: the screen it's on and its id there, its name, what it is ("Raw Material", "Made Ingredient",
/// "Sub-Assembly", "Product") and its category, or a sub-assembly's role.
/// </summary>
public sealed record QuickFindResult(AppScreen Screen, int Id, string Name, string Kind, string? Group);

/// <summary>
/// Find (Ctrl+K): one box over the names of every raw material,
/// standalone sub-assembly and product, whatever screen is showing, and the record picked opened on its own screen as a link
/// from another screen opens one, by its address. A sub-assembly a product owns isn't one: it's opened with its product. The
/// records are read each time the dialog opens (<see cref="LoadCommand"/>), so a name saved since is found.
/// </summary>
public sealed class QuickFindViewModel : ViewModelBase
{
    /// <summary>The most matches listed: the best first, so a longer name can always be typed to reach the rest.</summary>
    public const int MostShown = 50;

    private const CompareOptions Loose = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;

    private readonly IRawMaterialRepository _rawMaterials;
    private readonly ISubAssemblyRepository _subAssemblies;
    private readonly IProductRepository _products;
    private IReadOnlyList<QuickFindResult> _records = [];

    public QuickFindViewModel(IRawMaterialRepository rawMaterials, ISubAssemblyRepository subAssemblies, IProductRepository products, ILogger logger)
        : base(logger)
    {
        _rawMaterials = rawMaterials;
        _subAssemblies = subAssemblies;
        _products = products;
        LoadCommand = new ScreenCommand(() => LoadSafelyAsync(LoadAsync));
    }

    /// <summary>Reads every record's name, then lists what <see cref="Query"/> matches.</summary>
    public ScreenCommand LoadCommand { get; }

    private string _query = string.Empty;

    /// <summary>What's typed: every word of it is somewhere in a match's name, ignoring case and accents.</summary>
    public string Query
    {
        get => _query;
        set
        {
            RaiseAndSetIfChanged(ref _query, value ?? string.Empty);
            Filter();
        }
    }

    /// <summary>The records <see cref="Query"/> matches, best first (<see cref="Match"/>), at most <see cref="MostShown"/>.</summary>
    public ObservableCollection<QuickFindResult> Results { get; } = [];

    private QuickFindResult? _selectedResult;

    /// <summary>The match Enter opens: the best one each time the list changes, then wherever Up and Down move it.</summary>
    public QuickFindResult? SelectedResult
    {
        get => _selectedResult;
        set => RaiseAndSetIfChanged(ref _selectedResult, value);
    }

    private string? _resultsNote = "Type part of a name.";

    /// <summary>Under the list: what to do with nothing typed, that nothing matched, or that only the best are listed.</summary>
    public string? ResultsNote
    {
        get => _resultsNote;
        private set => RaiseAndSetIfChanged(ref _resultsNote, value);
    }

    /// <summary>
    /// The records whose names hold every word of <paramref name="query"/>, ignoring case and accents, best first: the name
    /// that's exactly it, then names starting with it, then names with a word starting with its first word, then the rest;
    /// among equals, by name. Nothing for a query of only spaces.
    /// </summary>
    public static IReadOnlyList<QuickFindResult> Match(IEnumerable<QuickFindResult> records, string query)
    {
        var wanted = query.Trim();
        var words = wanted.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            return [];
        }

        var compare = CultureInfo.CurrentCulture.CompareInfo;
        return
        [
            .. records
                .Where(r => words.All(word => compare.IndexOf(r.Name, word, Loose) >= 0))
                .Select(r => (Record: r, Rank:
                    compare.Compare(r.Name, wanted, Loose) == 0 ? 0
                    : compare.IsPrefix(r.Name, wanted, Loose) ? 1
                    : WordsOf(r.Name).Any(w => compare.IsPrefix(w, words[0], Loose)) ? 2
                    : 3))
                .OrderBy(m => m.Rank)
                .ThenBy(m => m.Record.Name, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(m => m.Record.Screen)
                .Select(m => m.Record),
        ];
    }

    private static string[] WordsOf(string name) =>
        name.Split([.. name.Where(c => !char.IsLetterOrDigit(c)).Distinct()], StringSplitOptions.RemoveEmptyEntries);

    private async Task LoadAsync()
    {
        // Read together, off the UI thread, as the screens read their lists.
        var rawMaterialsRead = Task.Run(() => _rawMaterials.GetAllAsync(CancellationToken.None));
        var subAssembliesRead = Task.Run(() => _subAssemblies.GetStandaloneAsync(CancellationToken.None));
        var productsRead = Task.Run(() => _products.GetAllAsync(CancellationToken.None));
        await Task.WhenAll(rawMaterialsRead, subAssembliesRead, productsRead);
        var (rawMaterials, subAssemblies, products) = (await rawMaterialsRead, await subAssembliesRead, await productsRead);
        _records =
        [
            .. rawMaterials.Select(m => new QuickFindResult(
                AppScreen.RawMaterials, m.Id, m.Name, m.Source == RawMaterialSource.Made ? "Made Ingredient" : "Raw Material", m.Category?.Name)),
            .. subAssemblies.Select(s => new QuickFindResult(AppScreen.SubAssemblies, s.Id, s.Name, "Sub-Assembly", s.Role?.Name)),
            .. products.Select(p => new QuickFindResult(AppScreen.Products, p.Id, p.Name, "Product", p.Category?.Name)),
        ];
        Filter();
    }

    private void Filter()
    {
        var matches = Match(_records, Query);
        Results.Clear();
        foreach (var match in matches.Take(MostShown))
        {
            Results.Add(match);
        }

        SelectedResult = Results.FirstOrDefault();
        ResultsNote = Query.Trim().Length == 0 ? "Type part of a name."
            : matches.Count == 0 ? $"No raw material, sub-assembly or product is named anything like \"{Query.Trim()}\"."
            : matches.Count > MostShown ? $"The best {MostShown} of {matches.Count}: type more of the name to narrow them."
            : null;
    }
}
