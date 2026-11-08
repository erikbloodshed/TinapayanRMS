// The app's JS initializer: Blazor imports it as it starts (the build lists it, under a fingerprinted name, in
// _framework/blazor.modules.json) and waits for its beforeStart before it draws anything.

// The same module index.html started as the page was parsed (one URL, so it runs once).
import { syncfusionScripts } from "./syncfusion.js";

// Syncfusion's components can't start before their scripts are in (syncfusion.js says why).
export function beforeStart() {
    return syncfusionScripts;
}
