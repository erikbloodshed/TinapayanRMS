// Syncfusion's scripts for the components the screens use, and only those. Syncfusion's all-components script,
// syncfusion-blazor.min.js, is 6.95 MB, every component Syncfusion makes; the screens' components need 1.64 MB of it.
// They're loaded as Syncfusion's own per-component scripts load theirs: its base script, then sfBlazor.loadDependencies,
// which fetches a component's scripts and those they need (the base script's map) and runs them.
//
// Nothing in Syncfusion waits for them: the window starts Blazor once the page is parsed, and a component whose script
// isn't in yet throws (the "Something went wrong" bar) or quietly does without it. So index.html starts this module as
// the page is parsed, and Blazor's start waits for it (TinapayanRMS.Maui.lib.module.js, the app's JS initializer).

// The Syncfusion components the screens use, by the names Syncfusion's loader knows them by (SfGrid is "sfgrid"); SfButton
// has no script. A component new to the screens goes here too: SyncfusionScriptsTests (Components.Tests) checks the list
// against the components the screens name and against the loader.
const components = [
    "sfcheckbox",
    "sfchart",
    "sfcombobox",
    "sfcontextmenu",
    "sfdatepicker",
    "sfdialog",
    "sfdropdownbutton",
    "sfdropdownlist",
    "sfgrid",
    "sfnumerictextbox",
    "sfsidebar",
    "sftab",
    "sftextbox",
    "sftoast",
];

async function load() {
    // By the URL each of Syncfusion's per-component scripts imports it by, so it runs once, whichever asks first.
    await import(`${document.baseURI}_content/Syncfusion.Blazor.Core/scripts/syncfusion-blazor-base.min.js`);
    await Promise.all(components.map(name => window.sfBlazor.loadDependencies(name)));
}

// A failure is logged, and Blazor starts all the same: a component whose script is missing then shows the error bar,
// where a page that never started would stay blank.
export const syncfusionScripts = load().catch(error => console.error("Syncfusion's scripts didn't load.", error));
