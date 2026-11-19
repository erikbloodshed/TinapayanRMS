using System.Reactive;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Presentation.Printing;
using TinapayanRMS.Presentation.Shared;
using ILogger = Serilog.ILogger;

namespace TinapayanRMS.Presentation.Settings;

/// <summary>
/// Settings' Printing tab: the paper printouts are laid out for, which way up and the margins; the shop's heading (name,
/// address line, logo) and the note at each page's foot; the type's size; and the columns and parts left out. Kept in the
/// database (PrintSettings), for every computer the shop prints from; Save puts them in use at once (PrintPage).
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

    public IReadOnlyList<KeyValuePair<PrintTypeSize, string>> TypeSizes { get; } =
    [
        KeyValuePair.Create(PrintTypeSize.Compact, "Compact"),
        KeyValuePair.Create(PrintTypeSize.Normal, "Normal"),
        KeyValuePair.Create(PrintTypeSize.Large, "Large"),
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

    private string? _editingShopName;
    public string? EditingShopName
    {
        get => _editingShopName;
        set => RaiseAndSetIfChanged(ref _editingShopName, value);
    }

    private string? _editingShopAddress;
    public string? EditingShopAddress
    {
        get => _editingShopAddress;
        set => RaiseAndSetIfChanged(ref _editingShopAddress, value);
    }

    private string? _editingFooterNote;
    public string? EditingFooterNote
    {
        get => _editingFooterNote;
        set => RaiseAndSetIfChanged(ref _editingFooterNote, value);
    }

    private (byte[] Picture, string ContentType)? _editingLogo;
    /// <summary>The logo picked, with its media type; null for none.</summary>
    public (byte[] Picture, string ContentType)? EditingLogo
    {
        get => _editingLogo;
        private set
        {
            if (RaiseAndSetIfChanged(ref _editingLogo, value))
            {
                RaisePropertyChanged(nameof(LogoSource));
            }
        }
    }

    /// <summary>The logo picked as a picture's address the page can show; null for none.</summary>
    public string? LogoSource => EditingLogo is { } logo ? $"data:{logo.ContentType};base64,{Convert.ToBase64String(logo.Picture)}" : null;

    private PrintTypeSize _editingTypeSize;
    public PrintTypeSize EditingTypeSize
    {
        get => _editingTypeSize;
        set => RaiseAndSetIfChanged(ref _editingTypeSize, value);
    }

    private bool _printsUnitCosts = true;
    /// <summary>Each recipe line's Cost/g and each packaging line's Unit Cost.</summary>
    public bool PrintsUnitCosts
    {
        get => _printsUnitCosts;
        set => RaiseAndSetIfChanged(ref _printsUnitCosts, value);
    }

    private bool _printsPercentages = true;
    /// <summary>The recipe's Baker's % or Absolute % column.</summary>
    public bool PrintsPercentages
    {
        get => _printsPercentages;
        set => RaiseAndSetIfChanged(ref _printsPercentages, value);
    }

    private bool _printsFigures = true;
    /// <summary>The figure tiles under each product's title.</summary>
    public bool PrintsFigures
    {
        get => _printsFigures;
        set => RaiseAndSetIfChanged(ref _printsFigures, value);
    }

    private bool _printsPriceComparison = true;
    /// <summary>The suggested price beside the current one, under the costing.</summary>
    public bool PrintsPriceComparison
    {
        get => _printsPriceComparison;
        set => RaiseAndSetIfChanged(ref _printsPriceComparison, value);
    }

    public ScreenCommand LoadCommand { get; }
    public ScreenCommand SaveCommand { get; }

    /// <summary>Choose Logo: a PNG or JPEG picture, through <see cref="ChooseLogoFile"/>, shown until Save keeps it.</summary>
    public ScreenCommand ChooseLogoCommand { get; }

    /// <summary>Remove Logo: none, once saved.</summary>
    public ScreenCommand RemoveLogoCommand { get; }

    /// <summary>Answered with the picture file chosen, or null when cancelled.</summary>
    public Interaction<Unit, string?> ChooseLogoFile { get; } = new();

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
            EditingShopName = settings.ShopName;
            EditingShopAddress = settings.ShopAddress;
            EditingFooterNote = settings.FooterNote;
            EditingLogo = settings.Logo is { } logo ? (logo, settings.LogoContentType!) : null;
            EditingTypeSize = settings.TypeSize;
            PrintsUnitCosts = !settings.OmitUnitCosts;
            PrintsPercentages = !settings.OmitPercentages;
            PrintsFigures = !settings.OmitFigures;
            PrintsPriceComparison = !settings.OmitPriceComparison;
        }));

        SaveCommand = new ScreenCommand(() => RunSafelyAsync(async () =>
        {
            var settings = Editing;
            await _repository.SetAsync(settings, CancellationToken.None);
            // As it's kept, names tidied: what the printouts use, and what the fields show.
            settings = await _repository.GetAsync(CancellationToken.None);
            EditingShopName = settings.ShopName;
            EditingShopAddress = settings.ShopAddress;
            EditingFooterNote = settings.FooterNote;
            _page.Use(settings);
            SuccessMessage = "Printing saved. Print, Export to PDF and the Price Simulator's report use it from now on.";
        }));

        ChooseLogoCommand = new ScreenCommand(() => RunSafelyAsync(async () =>
        {
            if (await ChooseLogoFile.Handle(Unit.Default) is not { } path)
            {
                return;
            }

            PrintRules.EnsureLogoSize(new FileInfo(path).Length);
            var picture = await File.ReadAllBytesAsync(path);
            EditingLogo = (picture, PrintRules.LogoContentTypeOf(picture));
        }));

        RemoveLogoCommand = ScreenCommand.Of(() => EditingLogo = null, () => EditingLogo is not null);
    }

    private PrintSettings Editing => new()
    {
        PaperSize = EditingPaperSize,
        Orientation = EditingOrientation,
        Margins = EditingMargins,
        ShopName = EditingShopName,
        ShopAddress = EditingShopAddress,
        FooterNote = EditingFooterNote,
        Logo = EditingLogo?.Picture,
        LogoContentType = EditingLogo?.ContentType,
        TypeSize = EditingTypeSize,
        OmitUnitCosts = !PrintsUnitCosts,
        OmitPercentages = !PrintsPercentages,
        OmitFigures = !PrintsFigures,
        OmitPriceComparison = !PrintsPriceComparison,
    };
}
