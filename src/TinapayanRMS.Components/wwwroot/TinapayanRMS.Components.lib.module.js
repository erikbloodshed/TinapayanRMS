// The screens' JS initializer: Blazor imports it as it starts (the host's build lists each referenced library's, under a
// fingerprinted name, in _framework/blazor.modules.json) and waits for its beforeStart before it draws anything.

// The same module the host page started as it was parsed (one URL, _content/TinapayanRMS.Components/syncfusion.js, so it
// runs once).
import { syncfusionScripts } from "./syncfusion.js";

// Syncfusion's components can't start before their scripts are in (syncfusion.js says why).
export function beforeStart() {
    return syncfusionScripts;
}
