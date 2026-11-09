// Small browser helpers the Blazor components call through JS interop (window.tinapayan).

// A number box's text, and a picker's, is selected as it takes the cursor, so what's typed replaces it, as the WPF
// DoubleTextBoxes and ComboBoxAdvs did: without it a picker's typing is added to the name it holds, and matches nothing.
// Done here, in the browser, so it's in place before the first key: the server can't be asked in time. A click puts the
// caret where it lands after focus arrives, so when the cursor came in by that click, the selection is made again once
// the click is over. Only then: a box the cursor came into by Tab (or the Vim keys) is clicked in to put the caret
// somewhere, and selecting it all then had the next key replace the whole number.
let pressedOn = null;

// When the mouse was last pressed anywhere in the page: a list's name box (TreeNameBox) tells the cursor taken by a click
// of the user's from one taken by a drop-down or dialog closing, which presses nothing.
let lastPressedAt = -1;
document.addEventListener("pointerdown", e => { pressedOn = e.target; lastPressedAt = performance.now(); }, true);
document.addEventListener("pointerup", () => { pressedOn = null; }, true);
document.addEventListener("pointercancel", () => { pressedOn = null; }, true);
document.addEventListener("focusin", e => {
    const input = e.target;
    if (input instanceof HTMLInputElement && !input.readOnly && (input.closest(".number-field") || input.classList.contains("e-combobox"))) {
        input.select();
        if (pressedOn instanceof Node && input.contains(pressedOn)) {
            input.addEventListener("mouseup", () => input.select(), { once: true });
        }
    }
});

window.tinapayan = {
    // Scrolls an element into view only if it isn't already, as the WPF lists' BringIntoView did.
    scrollIntoView(id) {
        const element = document.getElementById(id);
        if (element) {
            element.scrollIntoView({ block: "nearest", inline: "nearest" });
        }
    },

    focus(id) {
        const element = document.getElementById(id);
        if (element) {
            element.focus();
            if (typeof element.select === "function") {
                element.select();
            }
        }
    },

    // A list's name box (TreeNameBox) as it shows: focus, and the time it showed, for takeFocusBack.
    showNameBox(id) {
        const element = document.getElementById(id);
        if (element) {
            element.__shownAt = performance.now();
            tinapayan.focus(id);
        }
    },

    // A list's name box that lost the cursor before it was used: takes it back, unless the mouse was pressed anywhere since
    // the box showed, when the user moved on (picked another row, clicked a field) and the box is to close. The cursor taken
    // with no press was taken by whatever started the naming as it closed: a drop-down, or the dialog about unsaved changes.
    // True when it took it back.
    takeFocusBack(id) {
        const element = document.getElementById(id);
        if (!element || lastPressedAt > (element.__shownAt ?? Infinity)) {
            return false;
        }

        if (document.activeElement !== element) {
            tinapayan.focus(id);
        }

        return true;
    },

    // Puts the cursor on the nearest element around this one that matches the selector: a list's name box giving it back
    // to its tree (TreeNameBox).
    focusClosest(element, selector) {
        element?.closest(selector)?.focus();
    },

    // Find's Ctrl+K, from anywhere in the tab, a field or a table included; Cmd+K on a Mac. Returns an object whose
    // dispose() unregisters it.
    registerFindKey(dotNet) {
        const isMac = /Mac|iPhone|iPad/.test(navigator.userAgentData?.platform || navigator.platform || "");
        const handler = e => {
            const shortcut = e.ctrlKey !== e.metaKey && (e.ctrlKey || isMac);
            if (shortcut && !e.altKey && !e.shiftKey && e.key.toLowerCase() === "k") {
                e.preventDefault();
                e.stopPropagation();
                dotNet.invokeMethodAsync("OpenFromKeyAsync");
            }
        };
        document.addEventListener("keydown", handler, true);
        return { dispose: () => document.removeEventListener("keydown", handler, true) };
    },

    // The web view's own "Leave site?" question while a screen's form holds unsaved changes: reloading the page (F5)
    // would otherwise throw them away without a word. Moves within the app are asked about by the screen itself, and
    // closing the window by the app (UnsavedChangesGuard).
    setLeaveWarning(on) {
        if (on && !window.__tinapayanLeave) {
            window.__tinapayanLeave = e => {
                e.preventDefault();
                e.returnValue = "";
            };
            window.addEventListener("beforeunload", window.__tinapayanLeave);
        } else if (!on && window.__tinapayanLeave) {
            window.removeEventListener("beforeunload", window.__tinapayanLeave);
            window.__tinapayanLeave = null;
        }
    },

    // The first field of a table's row (LineGrid's Insert Line): its line's first cell, ready to type.
    focusLineCell(tableId, row) {
        const line = document.getElementById(tableId)?.tBodies[0]?.rows[row];
        const field = line?.querySelector("input:not([readonly]):not([disabled]), textarea, select");
        if (field) {
            field.focus();
            if (typeof field.select === "function") {
                field.select();
            }
        }
    },

    // Keeps the keys a component handles itself (a tree's arrows, say) from also scrolling the page.
    preventKeys(element, keys) {
        if (!element || element.__tinapayanKeys) {
            return;
        }
        element.__tinapayanKeys = true;
        element.addEventListener("keydown", e => {
            if (keys.includes(e.key) && !e.target.closest("input, textarea, select")) {
                e.preventDefault();
            }
        });
    },

    // Ctrl+Z, Ctrl+Y and Ctrl+Shift+Z for a screen with undo (UndoKeys), and on a Mac Cmd+Z and Cmd+Shift+Z as well, since
    // Cmd is a Mac's shortcut key: the field with the cursor is left first, so what's half-typed lands before the screen
    // steps back, and so the field shows what the step put in it (a field never has its text rewritten while it holds the
    // cursor). The cursor goes back once the screen has been drawn again, which it has by the time the call returns.
    // Returns an object whose dispose() unregisters it.
    registerUndoKeys(dotNet) {
        const isMac = /Mac|iPhone|iPad/.test(navigator.userAgentData?.platform || navigator.platform || "");
        const handler = e => {
            const shortcut = e.ctrlKey !== e.metaKey && (e.ctrlKey || isMac);
            if (!shortcut || e.altKey) {
                return;
            }
            const key = e.key.toLowerCase();
            let redo;
            if (key === "z") {
                redo = e.shiftKey;
            } else if (key === "y" && !e.shiftKey) {
                redo = true;
            } else {
                return;
            }
            e.preventDefault();
            const restore = window.tinapayanVim.holdCursor();
            const active = document.activeElement;
            if (active && active !== document.body) {
                active.blur();
            }
            setTimeout(() => dotNet.invokeMethodAsync("Undo", redo).then(restore), 0);
        };
        document.addEventListener("keydown", handler);
        return { dispose: () => document.removeEventListener("keydown", handler) };
    },

    // Opens the web view's print dialog for the page (the print views lay themselves out with @page CSS).
    print() {
        window.print();
    },

    // The window's title bar, which the page draws (Layout/TitleBar.razor). The window is told where the bar is and where
    // its buttons are, in device pixels, so Windows takes the rest of the bar as the window's caption (IAppWindow): told
    // again whenever the bar or anything in it moves or changes size, as the window is resized or maximized, the heading
    // changes, or the Vim keys' mode shows. And Back and Forward follow the web view's history (the Navigation API), on
    // every move from one address to another. Returns an object whose dispose() stops both.
    watchTitleBar(bar, dotNet) {
        let queued = false;
        const device = r => {
            const scale = window.devicePixelRatio || 1;
            return {
                x: Math.round(r.left * scale),
                y: Math.round(r.top * scale),
                width: Math.round(r.width * scale),
                height: Math.round(r.height * scale),
            };
        };
        const send = () => {
            queued = false;
            const buttons = [...bar.querySelectorAll("button, a, input")]
                .filter(e => e.getClientRects().length > 0)
                .map(e => device(e.getBoundingClientRect()))
                .filter(r => r.width > 0 && r.height > 0);
            dotNet.invokeMethodAsync("SetTitleBar", device(bar.getBoundingClientRect()), buttons);
        };
        const queue = () => {
            if (!queued) {
                queued = true;
                requestAnimationFrame(send);
            }
        };

        const sizes = new ResizeObserver(queue);
        const observeAll = () => {
            sizes.observe(bar);
            bar.querySelectorAll("*").forEach(e => sizes.observe(e));
        };
        const changes = new MutationObserver(() => {
            observeAll();
            queue();
        });
        observeAll();
        changes.observe(bar, { childList: true, subtree: true, characterData: true, attributes: true, attributeFilter: ["hidden", "class", "style", "disabled"] });
        window.addEventListener("resize", queue);
        queue();

        const history = () => dotNet.invokeMethodAsync(
            "HistoryChanged", window.navigation?.canGoBack ?? true, window.navigation?.canGoForward ?? true);
        window.navigation?.addEventListener("currententrychange", history);
        history();

        return {
            dispose: () => {
                sizes.disconnect();
                changes.disconnect();
                window.removeEventListener("resize", queue);
                window.navigation?.removeEventListener("currententrychange", history);
            },
        };
    },
};
