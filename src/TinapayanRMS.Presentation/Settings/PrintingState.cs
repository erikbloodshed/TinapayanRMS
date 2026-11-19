using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Presentation.Printing;
using TinapayanRMS.Presentation.Shared;
using ILogger = Serilog.ILogger;

namespace TinapayanRMS.Presentation.Settings;

/// <summary>
/// Settings' Printing tab: the paper printouts are laid out for, which way up, and the margins. Kept in the database
/// (PrintSettings), for every computer the shop prints from; Save puts them in use at once (PrintPage).
/// </summary>
public sealed class PrintingState : ScreenState
{
    private readonly IPrintSettingsRepository _repository;
    private readonly PrintPage _page;

    /// <summary>What the Paper picker offers, each with its size.</summary>
    public IReadOnlyList<KeyValuePair<PaperSize, string>> PaperSizes { get; } =
    [
        KeyValuePair.Create(PaperSize.A4, "A4 (210 × 297 mm)"),
        KeyValuePair.Create(PaperSize.Letter, "Letter, short (8.5 × 11 in)"),
        KeyValuePair.Create(PaperSize.LongBond, "Long (8.5 × 13 in)"),
        KeyValuePair.Create(PaperSize.Legal, "Legal (8.5 × 14 in)"),
    ];

    public IReadOnlyList<KeyValuePair<PaperOrientation, string>> Orientations { get; } =
    [
        KeyValuePair.Create(PaperOrientation.Portrait, "Portrait"),
        KeyValuePair.Create(PaperOrientation.Landscape, "Landscape"),
    ];

    /// <summary>What the Margins picker offers, each with its width at the sides.</summary>
    public IReadOnlyList<KeyValuePair<PageMargins, string>> MarginChoices { get; } =
    [
        KeyValuePair.Create(PageMargins.Normal, "Normal (14 mm)"),
        KeyValuePair.Create(PageMargins.Narrow, "Narrow (8 mm)"),
        KeyValuePair.Create(PageMargins.Wide, "Wide (20 mm)"),
    ];

    private PaperSize _editingPaperSize;
    public PaperSize EditingPaperSize
    {
        get => _editingPaperSize;
        set
        {
            if (RaiseAndSetIfChanged(ref _editingPaperSize, value))
            {
                RaisePropertyChanged(nameof(PrintedAreaText));
            }
        }
    }

    private PaperOrientation _editingOrientation;
    public PaperOrientation EditingOrientation
    {
        get => _editingOrientation;
        set
        {
            if (RaiseAndSetIfChanged(ref _editingOrientation, value))
            {
                RaisePropertyChanged(nameof(PrintedAreaText));
            }
        }
    }

    private PageMargins _editingMargins;
    public PageMargins EditingMargins
    {
        get => _editingMargins;
        set
        {
            if (RaiseAndSetIfChanged(ref _editingMargins, value))
            {
                RaisePropertyChanged(nameof(PrintedAreaText));
            }
        }
    }

    /// <summary>"Each page prints on 182 × 267 mm of the paper.", for what's picked.</summary>
    public string PrintedAreaText
    {
        get
        {
            var area = PrintRules.PrintedAreaOf(Editing);
            return $"Each page prints on {area.Width:0.#} × {area.Height:0.#} mm of the paper.";
        }
    }

    public ScreenCommand LoadCommand { get; }
    public ScreenCommand SaveCommand { get; }

    public PrintingState(IPrintSettingsRepository repository, PrintPage page, ILogger logger)
        : base(logger)
    {
        _repository = repository;
        _page = page;

        // What's saved replaces the fields: a choice left unsaved doesn't outlive the visit.
        LoadCommand = new ScreenCommand(() => LoadSafelyAsync(async () =>
        {
            var settings = await _repository.GetAsync(CancellationToken.None);
            EditingPaperSize = settings.PaperSize;
            EditingOrientation = settings.Orientation;
            EditingMargins = settings.Margins;
        }));

        SaveCommand = new ScreenCommand(() => RunSafelyAsync(async () =>
        {
            var settings = Editing;
            await _repository.SetAsync(settings, CancellationToken.None);
            _page.Use(settings);
            SuccessMessage = "Printing saved. Print, Export to PDF and the Price Simulator's report use it from now on.";
        }));
    }

    private PrintSettings Editing => new()
    {
        PaperSize = EditingPaperSize,
        Orientation = EditingOrientation,
        Margins = EditingMargins,
    };
}
