using System.Reactive.Disposables;
using Microsoft.AspNetCore.Components;
using TinapayanRMS.Presentation.Shared;

namespace TinapayanRMS.Components.Shared;

/// <summary>
/// A record view's page (Sub-Assemblies): its address names the tab showing, so the browser's Back and Forward step
/// between the tabs and records shown. A saved record's tab is at sub-assemblies/42; a new one's at sub-assemblies/new/3,
/// the tab's own number (RecordTab.Number), which still finds the tab once it's saved; and the view with no tab open at
/// sub-assemblies. An address names what to show (a record opens as a pick in the list would, in its tab or the preview
/// tab), and what the view shows goes into the address: a tab shown or opened adds the address to the history, while a new
/// record saved, or the view's bare address, has its place taken.
/// </summary>
public abstract class RecordViewBase<TView, TForm> : ScreenBase<TView>
    where TView : RecordViewState<TForm>
    where TForm : ScreenState, ITabForm, IDisposable
{
    /// <summary>The address last acted on, so the same one given again opens nothing.</summary>
    private (int? Id, int? Number) _addressOpened;

    /// <summary>The tab, and the record it held, the address last followed.</summary>
    private (RecordTab<TForm>? Tab, int RecordId) _followed;

    /// <summary>A saved record in the address, opened in its tab or the preview tab.</summary>
    [Parameter] public int? Id { get; set; }

    /// <summary>A tab's own number in the address, a new record's (sub-assemblies/new/3).</summary>
    [Parameter] public int? Number { get; set; }

    [Inject] private NavigationManager Navigation { get; set; } = null!;

    /// <summary>The view's address, "sub-assemblies".</summary>
    protected abstract string AddressRoot { get; }

    /// <summary>The address of <paramref name="tab"/>, or the view's own with none.</summary>
    protected string AddressOf(RecordTab<TForm>? tab) => tab switch
    {
        null => AddressRoot,
        { Form.RecordId: > 0 and var id } => $"{AddressRoot}/{id}",
        _ => $"{AddressRoot}/new/{tab.Number}",
    };

    /// <summary>A tab's ✕ on the strip (RecordTabStrip): asks about its changes first, showing it, and stays on Cancel.</summary>
    protected Task CloseTabAsync(RecordTab<TForm> tab) => State!.CloseTabAsync(tab);

    // The record in the address is the load's to open, at its end, its list fresh only then. A tab's number is shown now.
    protected override void OnInitialized()
    {
        base.OnInitialized();
        var view = State!;
        view.Tabs.Changed += OnTabsChanged;
        AddSubscription(Disposable.Create(() => view.Tabs.Changed -= OnTabsChanged));

        _addressOpened = (Id, Number);
        if (Id is { } id)
        {
            view.OpenRecordOnLoad(id);
        }
        else
        {
            if (Number is { } number)
            {
                view.ShowNumbered(number);
            }

            FollowTab(replace: true);
        }
    }

    // An address that changed: Back, Forward, a link from another screen, a tab clicked. Not the one the view opened on,
    // which this is called for too, once the load is back.
    protected override void OnParametersSet()
    {
        if ((Id, Number) == _addressOpened)
        {
            return;
        }

        _addressOpened = (Id, Number);
        if (Id is { } id)
        {
            State!.OpenRecord(id);
        }
        else if (Number is { } number)
        {
            State!.ShowNumbered(number);
        }

        // The tab the address found, under its own address: a new record's, saved since, is at its record's now.
        FollowTab(replace: true);
    }

    // The tab showing changed, or its record did: on whichever thread changed it.
    private void OnTabsChanged() => _ = InvokeAsync(() => FollowTab(replace: false));

    /// <summary>
    /// Moves the address to the tab showing. A tab shown, opened or showing another record adds to the history, so Back
    /// returns to the one before; a new record that's been saved, or the view's bare address, has its place taken
    /// (<paramref name="replace"/> too), so Back doesn't come back to an address that names the same tab.
    /// </summary>
    private void FollowTab(bool replace)
    {
        var view = State!;
        if (view.IsOpeningRecord)
        {
            return;
        }

        var tab = view.Tabs.Active;
        var recordId = tab?.Form.RecordId ?? 0;
        var address = AddressOf(tab);
        var current = Navigation.ToBaseRelativePath(Navigation.Uri).Split('?', '#')[0].Trim('/');
        var saved = tab is not null && tab == _followed.Tab && _followed.RecordId == 0 && recordId > 0;
        _followed = (tab, recordId);
        if (!string.Equals(current, address, StringComparison.OrdinalIgnoreCase))
        {
            MoveAddressTo(address, replace || saved || string.Equals(current, AddressRoot, StringComparison.OrdinalIgnoreCase));
        }

        _addressOpened = tab is null ? (null, null) : recordId > 0 ? (recordId, null) : (null, tab.Number);
    }
}
