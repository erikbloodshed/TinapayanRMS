// The Vim keys (Settings → Keyboard), as the WPF app's VimKeys read them: NORMAL, where letters move and act, and
// INSERT, where they type. The keys are read here, in the browser, where the keyboard and the cursor are; the key table
// (VimKeymap) and the command list (VimCommandLine) come from the server as the keys are turned on, and the server makes
// the labels for f and runs what's typed after a colon (the VimKeys component). Everything is done the way a hand would:
// a key moves the cursor to a control, clicks it, or presses the arrow key it stands for.
window.tinapayanVim = (() => {
    let dotNet = null;
    let bindings = [];
    let commands = [];
    let on = false;
    let mode = "NORMAL";
    let pending = "";

    // A cursor move the keys made themselves: it doesn't start typing, as a click or Tab into a field does.
    let selfFocus = false;

    let hints = null;   // { items: [{ label, element, tag }], typed, layer }, while f's labels are up
    let bar = null;     // { element, input, before, sum }, while the command line, or the sum after Ctrl+R =, is open
    let register = false; // Ctrl+R pressed while typing: = next opens the sum
    let sheet = null;   // the cheat sheet's element, while it's up
    let flash = null;   // what the last command had to say, until a key or a few seconds pass
    let flashTimer = 0;

    const EDITABLE_TYPES = new Set(["", "text", "search", "number", "password", "email", "tel", "url"]);
    const KEY_CODES = { ArrowLeft: 37, ArrowUp: 38, ArrowRight: 39, ArrowDown: 40, Home: 36, End: 35, Enter: 13, Escape: 27, Delete: 46, F2: 113, z: 90, y: 89 };
    const TREE_KEYS = { Down: "ArrowDown", Up: "ArrowUp", Left: "ArrowLeft", Right: "ArrowRight", First: "Home", Last: "End", Open: "Enter", Delete: "Delete", Rename: "F2" };
    const HINT_TARGETS = "a[href], button, input, select, textarea, [role=tab], [role=treeitem], .e-toolbar-item, .e-checkbox-wrapper, .e-grid tr.e-row, .e-popup-open .e-list-item, .tree-menu li";
    const HINT_WRAPPERS = "a[href], button, [role=tab], .e-toolbar-item, .e-checkbox-wrapper";
    const CONTEXT_NAMES = { Field: "in a field", Table: "in a table", Tree: "in a list", Other: "on a button, a tab or a check box" };

    // ---- Where the cursor is. ----

    function isEditable(element) {
        if (element instanceof HTMLTextAreaElement) {
            return !element.readOnly && !element.disabled;
        }
        if (element instanceof HTMLInputElement) {
            return EDITABLE_TYPES.has((element.getAttribute("type") || "").toLowerCase()) && !element.readOnly && !element.disabled;
        }
        return element instanceof HTMLElement && element.isContentEditable;
    }

    function lineGridOf(element) {
        return element.closest(".line-grid tbody") ? element.closest(".line-grid") : null;
    }

    function pickGridOf(element) {
        return element.closest(".e-gridcontent, .e-gridheader") ? element.closest(".e-grid") : null;
    }

    function contextOf(element) {
        if (!(element instanceof HTMLElement) || element === document.body) {
            return "Other";
        }
        // A row's name typed in place (TreeNameBox) is a field: its keys are text, not the tree's.
        if (element.closest(".folder-tree") && !isEditable(element)) {
            return "Tree";
        }
        if (lineGridOf(element) || pickGridOf(element)) {
            return "Table";
        }
        return isEditable(element) ? "Field" : "Other";
    }

    function isVisible(element) {
        return element.getClientRects().length > 0 && getComputedStyle(element).visibility !== "hidden";
    }

    // The top dialog while one is open, as its keys stay in it; else the page.
    function scope() {
        const dialogs = [...document.querySelectorAll(".e-dialog.e-popup-open")];
        return dialogs.length ? dialogs[dialogs.length - 1] : document.body;
    }

    function isOutOfReach(element) {
        return element.closest(".e-sidebar.e-close, #blazor-error-ui, .vim-layer") !== null;
    }

    function popupIsOpen() {
        return document.querySelector(".e-popup.e-popup-open .e-list-item, .e-popup.e-popup-open .e-nodata") !== null;
    }

    // ---- The mode. ----

    function setMode(next) {
        mode = next;
        pending = "";
        const label = document.getElementById("vim-mode");

        // Written only when it changes, as it's set again on every move into a field: the title bar watches what's in it
        // (app.js's watchTitleBar), and each change in it tells the window where its caption is again.
        if (label && (label.hidden !== !on || label.textContent !== mode || label.classList.contains("vim-insert") !== (mode === "INSERT"))) {
            label.hidden = !on;
            label.textContent = mode;
            label.classList.toggle("vim-insert", mode === "INSERT");
        }
    }

    function focusQuietly(element) {
        selfFocus = true;
        try {
            element.focus();
        } finally {
            selfFocus = false;
        }
        element.scrollIntoView({ block: "nearest", inline: "nearest" });
    }

    // Presses a key the way the keyboard would, for the control that already knows what the key does.
    function press(target, key, modifiers = {}, bubbles = true) {
        const init = { key, code: key.length === 1 ? "Key" + key.toUpperCase() : key, bubbles, cancelable: true, composed: true, ...modifiers };
        for (const type of ["keydown", "keyup"]) {
            const event = new KeyboardEvent(type, init);
            Object.defineProperty(event, "keyCode", { get: () => KEY_CODES[key] || 0 });
            Object.defineProperty(event, "which", { get: () => KEY_CODES[key] || 0 });
            target.dispatchEvent(event);
        }
    }

    function swallow(event) {
        event.preventDefault();
        event.stopPropagation();
    }

    // ---- Moving between controls. ----

    function tabbables() {
        return [...scope().querySelectorAll("a[href], button, input, select, textarea, [tabindex]")].filter(element =>
            !element.disabled && element.tabIndex >= 0 && element.type !== "hidden" && element.type !== "file"
            && isVisible(element) && !isOutOfReach(element));
    }

    // The control after (1) or before (-1) the one with the cursor, as Tab and Shift+Tab find it; past `leaving` (a
    // list or table), when one is given.
    function stepControl(from, delta, leaving) {
        const list = tabbables();
        if (list.length === 0) {
            return;
        }

        let index = list.indexOf(from);
        if (index < 0) {
            if (!(from instanceof HTMLElement) || from === document.body || !document.body.contains(from)) {
                const page = document.querySelector(".app-page");
                const first = delta > 0 ? list.find(element => page && page.contains(element)) || list[0] : list[list.length - 1];
                focusQuietly(first);
                return;
            }

            // Not in the tab order itself (a row, say): start from where it sits in the page.
            index = list.findIndex(element => from.compareDocumentPosition(element) & Node.DOCUMENT_POSITION_FOLLOWING);
            index = index < 0 ? list.length : index;
            if (delta > 0) {
                index -= 1;
            }
        }

        // Round from the last to the first, so a dialog's few buttons are all a j or a k away.
        for (let steps = 1; steps <= list.length; steps++) {
            const next = list[(((index + delta * steps) % list.length) + list.length) % list.length];
            if (next !== from && (!leaving || !leaving.contains(next))) {
                focusQuietly(next);
                return;
            }
        }
    }

    // ---- A field's caret. ----

    function setCaret(element, position) {
        try {
            const length = element.value.length;
            const at = Math.max(0, Math.min(length, position));
            element.setSelectionRange(at, at);
        } catch {
            // Not a box with a caret.
        }
    }

    function moveCaret(element, action) {
        if (!isEditable(element)) {
            return;
        }

        const start = element.selectionStart ?? 0;
        const end = element.selectionEnd ?? start;
        switch (action) {
            case "Left": setCaret(element, start === end ? start - 1 : start); break;
            case "Right": setCaret(element, start === end ? end + 1 : end); break;
            case "LineStart": setCaret(element, 0); break;
            case "LineEnd": setCaret(element, element.value.length); break;
        }
    }

    // Starts typing in the field: at the caret, after it, at either end, or over what's there.
    function startTyping(element, where) {
        if (!isEditable(element)) {
            return;
        }

        if (document.activeElement !== element) {
            focusQuietly(element);
        }

        const start = element.selectionStart ?? 0;
        const end = element.selectionEnd ?? start;
        switch (where) {
            case "caret": setCaret(element, start); break;
            case "after": setCaret(element, start === end ? end + 1 : end); break;
            case "start": setCaret(element, 0); break;
            case "end": setCaret(element, element.value.length); break;
            case "over": element.select(); break;
        }
        setMode("INSERT");
    }

    // What's typed in a number box lands as the box is left; stopping typing lands it too.
    function commit(element) {
        if (isEditable(element) && !element.classList.contains("e-combobox")) {
            element.dispatchEvent(new Event("change", { bubbles: true }));
        }
    }

    // ---- A table typed straight into (LineGrid). ----

    function cellControls(row) {
        return [...row.querySelectorAll("td input, td textarea, td select")].filter(element =>
            !element.disabled && element.type !== "hidden" && isVisible(element));
    }

    function lineRows(grid) {
        return [...grid.tBodies[0].rows].filter(row => !row.classList.contains("empty-row") && cellControls(row).length > 0);
    }

    // The control in `row` under the column the cursor is in, or the nearest it has.
    function controlInColumn(row, column, fallbackIndex) {
        const controls = cellControls(row);
        return controls.find(control => control.closest("td").cellIndex === column) || controls[Math.min(fallbackIndex, controls.length - 1)];
    }

    function lineGridKey(grid, element, action) {
        const row = element.closest("tr");
        const rows = lineRows(grid);
        const rowIndex = rows.indexOf(row);
        const controls = cellControls(row);
        const index = controls.indexOf(element);
        const column = element.closest("td").cellIndex;
        const inRow = target => target ? controlInColumn(target, column, Math.max(index, 0)) : null;

        let target = null;
        switch (action) {
            case "Down": target = inRow(rows[rowIndex + 1]); break;
            case "Up": target = inRow(rows[rowIndex - 1]); break;
            case "First": target = inRow(rows[0]); break;
            case "Last": target = inRow(rows[rows.length - 1]); break;
            case "Left": target = controls[index - 1]; break;
            case "Right": target = controls[index + 1]; break;
            case "LineStart": target = controls[0]; break;
            case "LineEnd": target = controls[controls.length - 1]; break;
            case "Insert": startTyping(element, "start"); return;
            case "Append": startTyping(element, "end"); return;
            case "Change": startTyping(element, "over"); return;
            case "InsertAtStart": startTyping(controls.find(isEditable) || element, "start"); return;
            case "AppendAtEnd": startTyping([...controls].reverse().find(isEditable) || element, "end"); return;
            case "OpenAbove":
                // Insert Line, as the Insert key does in the ingredient tables: the line it puts in takes the cursor, ready to type.
                if (grid.classList.contains("inserts-lines")) {
                    press(grid, "Insert");
                }
                return;
            case "Open": {
                const adding = grid.querySelector("tbody tr.add-row");
                const first = adding && cellControls(adding).find(isEditable);
                if (first) {
                    startTyping(first, "over");
                }
                return;
            }
            case "Delete": removeLine(grid, row, rowIndex, column); return;
            case "NextControl": stepControl(element, 1, grid); return;
            case "PreviousControl": stepControl(element, -1, grid); return;
        }

        if (target) {
            focusQuietly(target);
        }
    }

    // The nearest Remove button after the table, which takes the line picked: the one with the cursor.
    function removeButtonFor(grid) {
        for (let ancestor = grid.parentElement; ancestor && ancestor !== document.body; ancestor = ancestor.parentElement) {
            const buttons = [...ancestor.querySelectorAll("button.remove-line")];
            if (buttons.length > 0) {
                return buttons.find(button => grid.compareDocumentPosition(button) & Node.DOCUMENT_POSITION_FOLLOWING) || buttons[0];
            }
        }
        return null;
    }

    function removeLine(grid, row, rowIndex, column) {
        const button = removeButtonFor(grid);
        if (row.classList.contains("add-row") || !button || button.disabled) {
            return;
        }

        button.click();

        // The cursor goes to the line that took its place, once the table has been drawn without it.
        setTimeout(() => {
            if (!document.body.contains(grid)) {
                return;
            }
            const rows = lineRows(grid);
            const next = rows[Math.min(rowIndex, rows.length - 1)];
            const target = next && controlInColumn(next, column, 0);
            if (target) {
                focusQuietly(target);
            }
        }, 200);
    }

    // ---- A grid of rows to pick from (PickGrid, the reports). ----

    function pickGridKey(grid, element, action) {
        if (action === "NextControl" || action === "PreviousControl") {
            stepControl(element, action === "NextControl" ? 1 : -1, grid);
            return;
        }

        const rows = [...grid.querySelectorAll(".e-gridcontent tr.e-row")];
        if (rows.length === 0) {
            return;
        }

        const current = rows.findIndex(row => row.getAttribute("aria-selected") === "true" || row.querySelector("td.e-selectionbackground"));
        let next;
        switch (action) {
            case "Down": next = Math.min(current + 1, rows.length - 1); break;
            case "Up": next = Math.max(current - 1, 0); break;
            case "First": next = 0; break;
            case "Last": next = rows.length - 1; break;
            default: return;
        }

        const cell = rows[next].querySelector("td.e-rowcell:not(.e-hide)");
        if (cell) {
            cell.click();
            cell.focus({ preventScroll: true });
            rows[next].scrollIntoView({ block: "nearest" });
        }
    }

    // ---- Search, screens, undo. ----

    function search() {
        const box = [...document.querySelectorAll(".list-search input, input.list-search, input[placeholder^='Search' i]")].find(isVisible);
        if (box) {
            box.focus();
            box.select();
        }
    }

    // The next tab along the top (1) or the one before (-1), round from the last to the first, as the WPF app's gt went:
    // a closed tab is passed over. In Settings, its own tabs: so the strip's Settings tab is passed over too, or the keys
    // that went in couldn't come back out.
    function stepScreen(delta) {
        const settings = [...document.querySelectorAll(".settings-tabs a")];
        if (settings.length > 0) {
            const current = settings.findIndex(tab => tab.classList.contains("active"));
            settings[(Math.max(current, 0) + delta + settings.length) % settings.length].click();
            return;
        }

        const tabs = [...document.querySelectorAll(".screen-tabs a:not([href='settings'])")];
        if (tabs.length === 0) {
            return;
        }

        const current = tabs.findIndex(tab => tab.parentElement.classList.contains("active"));
        tabs[current < 0 ? (delta > 0 ? 0 : tabs.length - 1) : (current + delta + tabs.length) % tabs.length].click();
    }

    function navigate(url) {
        window.Blazor.navigateTo(new URL(url, document.baseURI).href);
    }

    // Ctrl+Z and Ctrl+Y, which the screen's own undo keys (UndoKeys) answer.
    function undo(redo) {
        press(document.activeElement || document.body, redo ? "y" : "z", { ctrlKey: true });
    }

    // ---- f: labels on what can be clicked. ----

    function hintTargets() {
        const root = scope();
        const found = [...root.querySelectorAll(HINT_TARGETS)];

        // A dropdown's list and a row's menu open outside the dialog or page they belong to.
        if (root !== document.body) {
            found.push(...document.querySelectorAll(".e-popup-open .e-list-item, .tree-menu li"));
        }

        const visible = found.filter(element => {
            if (element.disabled || element.type === "hidden" || element.type === "file" || isOutOfReach(element) || !isVisible(element)) {
                return false;
            }
            const box = element.getBoundingClientRect();
            return box.width > 0 && box.height > 0 && box.bottom > 0 && box.right > 0 && box.top < innerHeight && box.left < innerWidth;
        });

        // One label for a button, a tab or a check box, not one more for what's inside it.
        const set = new Set(visible);
        return visible.filter(element => {
            const wrapper = element.parentElement && element.parentElement.closest(HINT_WRAPPERS);
            return !(wrapper && set.has(wrapper));
        });
    }

    async function showHints() {
        const targets = hintTargets();
        if (targets.length === 0) {
            return;
        }

        const layer = document.createElement("div");
        layer.className = "vim-layer vim-hints";
        document.body.appendChild(layer);
        hints = { items: [], typed: "", layer };
        const mine = hints;

        const labels = await dotNet.invokeMethodAsync("HintLabelsFor", targets.length);
        if (hints !== mine) {
            return;
        }

        // Every position read before any label goes in: a read after a change makes the page lay itself out again, once
        // per label. The labels are put together off the page and added at once.
        const boxes = targets.map(element => element.getBoundingClientRect());
        const tags = document.createDocumentFragment();
        targets.forEach((element, index) => {
            const box = boxes[index];
            const tag = document.createElement("span");
            tag.className = "vim-hint";
            tag.textContent = labels[index];
            tag.style.left = Math.max(0, Math.min(box.left, innerWidth - 30)) + "px";
            tag.style.top = Math.max(0, box.top) + "px";
            tags.appendChild(tag);
            mine.items.push({ label: labels[index], element, tag });
        });
        layer.appendChild(tags);
    }

    function closeHints() {
        if (hints) {
            hints.layer.remove();
            hints = null;
        }
    }

    function hintKey(event, isEscape) {
        if (isEscape) {
            closeHints();
            return;
        }

        if (event.key === "Backspace") {
            hints.typed = hints.typed.slice(0, -1);
        } else if (event.key.length === 1 && !event.ctrlKey && !event.altKey && !event.metaKey) {
            hints.typed += event.key.toLowerCase();
        } else if (["Shift", "Control", "Alt", "Meta"].includes(event.key)) {
            return;
        } else {
            closeHints();
            return;
        }

        const typed = hints.typed;
        const matching = hints.items.filter(item => item.label.startsWith(typed));
        if (matching.length === 0) {
            closeHints();
            return;
        }

        const exact = matching.find(item => item.label === typed);
        if (exact) {
            closeHints();
            activate(exact.element);
            return;
        }

        for (const item of hints.items) {
            const matches = item.label.startsWith(typed);
            item.tag.hidden = !matches;
            if (matches) {
                item.tag.innerHTML = `<b>${typed}</b>${item.label.slice(typed.length)}`;
            }
        }
    }

    // Clicks what a label was on; a field is typed into instead.
    function activate(element) {
        if (!document.body.contains(element)) {
            return;
        }

        if (isEditable(element)) {
            element.focus();
            element.select();
            setMode("INSERT");
        } else if (element.matches(".e-checkbox-wrapper")) {
            (element.querySelector("input") || element).click();
        } else if (element.matches(".e-toolbar-item")) {
            (element.querySelector(".e-tab-wrap") || element).click();
        } else if (element.matches("tr.e-row")) {
            (element.querySelector("td.e-rowcell:not(.e-hide)") || element).click();
        } else if (element.matches("[role=treeitem]")) {
            element.click();
            const tree = element.closest(".folder-tree");
            if (tree) {
                focusQuietly(tree);
            }
        } else {
            if (element.tabIndex >= 0) {
                focusQuietly(element);
            }
            element.click();
        }
    }

    // ---- The command line. ----

    function openBar() {
        clearFlash();
        const element = document.createElement("div");
        element.className = "vim-layer vim-bar";
        element.innerHTML = '<span class="vim-bar-colon">:</span><input type="text" spellcheck="false" autocomplete="off" aria-label="Command">';
        document.body.appendChild(element);
        bar = { element, input: element.querySelector("input"), before: document.activeElement };
        bar.input.focus();
    }

    // Ctrl+R = while typing (Vim's expression register): a sum typed in the bar, whose answer goes into the field or
    // cell where the caret is, over what's selected there, as typing it would.
    function openSumBar(target) {
        clearFlash();
        const element = document.createElement("div");
        element.className = "vim-layer vim-bar vim-sum";
        element.innerHTML = '<span class="vim-bar-colon">=</span><input type="text" spellcheck="false" autocomplete="off" aria-label="Sum"><span class="vim-sum-error"></span>';
        document.body.appendChild(element);
        bar = { element, input: element.querySelector("input"), before: target, sum: true };
        bar.input.focus();
    }

    // Closes the command line and puts the cursor back where it was, without starting to type there; or the sum's bar,
    // back to typing where it was.
    function closeBar() {
        if (!bar) {
            return;
        }

        const { before, sum } = bar;
        bar.element.remove();
        bar = null;
        if (before instanceof HTMLElement && before !== document.body && document.body.contains(before)) {
            focusQuietly(before);
        }
        setMode(sum ? "INSERT" : "NORMAL");
    }

    async function runSum() {
        const { input, before, element } = bar;
        const decimals = before?.dataset?.decimals;
        const result = await dotNet.invokeMethodAsync("EvaluateSum", input.value, decimals === undefined ? null : Number(decimals));
        if (!bar || bar.input !== input) {
            return;
        }
        if (result.error) {
            element.querySelector(".vim-sum-error").textContent = result.error;
            return;
        }

        closeBar();
        if (isEditable(before)) {
            const start = before.selectionStart ?? before.value.length;
            const end = before.selectionEnd ?? start;
            before.setRangeText(result.answer, start, end, "end");
            before.dispatchEvent(new Event("input", { bubbles: true }));
        }
    }

    function barKey(event, isEscape) {
        event.stopPropagation();
        if (bar.sum) {
            if (isEscape) {
                event.preventDefault();
                closeBar();
            } else if (event.key === "Enter") {
                event.preventDefault();
                runSum();
            } else {
                bar.element.querySelector(".vim-sum-error").textContent = "";
            }
            return;
        }
        if (isEscape) {
            event.preventDefault();
            closeBar();
        } else if (event.key === "Enter") {
            event.preventDefault();
            const line = bar.input.value;
            closeBar();
            runLine(line);
        }
    }

    async function runLine(line) {
        const result = await dotNet.invokeMethodAsync("RunCommandLine", line);
        if (result.help) {
            showSheet();
        } else if (result.url) {
            navigate(result.url);
        } else if (result.message) {
            showFlash(result.message);
        }
    }

    function showFlash(message) {
        clearFlash();
        flash = document.createElement("div");
        flash.className = "vim-layer vim-bar vim-flash";
        flash.textContent = message;
        document.body.appendChild(flash);
        flashTimer = setTimeout(clearFlash, 5000);
    }

    function clearFlash() {
        if (flash) {
            clearTimeout(flashTimer);
            flash.remove();
            flash = null;
        }
    }

    // ---- ?: the keys for wherever the cursor is. ----

    function showSheet() {
        closeSheet();
        const context = contextOf(document.activeElement);
        const here = bindings.filter(binding => !binding.anywhere && binding.where.includes(context));
        const anywhere = bindings.filter(binding => binding.anywhere);
        const rows = list => list.map(item => `<dt>${escapeHtml(item.keys || item.usage)}</dt><dd>${escapeHtml(item.description)}</dd>`).join("");

        sheet = document.createElement("div");
        sheet.className = "vim-layer vim-sheet";
        sheet.innerHTML = `
            <div class="vim-sheet-card" role="dialog" aria-label="Vim keys">
                <h2>Vim keys</h2>
                <div class="vim-sheet-columns">
                    <section><h3>Here: ${CONTEXT_NAMES[context]}</h3><dl>${rows(here)}</dl></section>
                    <section><h3>Anywhere</h3><dl>${rows(anywhere)}</dl></section>
                    <section><h3>Commands, after :</h3><dl>${rows(commands)}</dl></section>
                </div>
                <p>Esc, q or ? closes this.</p>
            </div>`;
        document.body.appendChild(sheet);
    }

    function closeSheet() {
        if (sheet) {
            sheet.remove();
            sheet = null;
        }
    }

    function escapeHtml(text) {
        return text.replace(/[&<>"]/g, character => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" }[character]));
    }

    // ---- Reading the keys. ----

    function run(action, context, element) {
        switch (action) {
            case "Hints": showHints(); return;
            case "Search": search(); return;
            case "NextTab": stepScreen(1); return;
            case "PreviousTab": stepScreen(-1); return;
            case "Back": history.back(); return;
            case "Forward": history.forward(); return;
            case "Undo": undo(false); return;
            case "CommandLine": openBar(); return;
            case "CheatSheet": showSheet(); return;
        }

        if (context === "Tree") {
            const tree = element.closest(".folder-tree");
            if (action === "NextControl" || action === "PreviousControl") {
                stepControl(tree, action === "NextControl" ? 1 : -1, tree);
            } else if (TREE_KEYS[action]) {
                press(tree, TREE_KEYS[action]);
            }
            return;
        }

        if (context === "Table") {
            const lines = lineGridOf(element);
            if (lines) {
                lineGridKey(lines, element, action);
            } else {
                pickGridKey(pickGridOf(element), element, action);
            }
            return;
        }

        if (context === "Field") {
            switch (action) {
                case "Down": case "NextControl": stepControl(element, 1); break;
                case "Up": case "PreviousControl": stepControl(element, -1); break;
                case "Left": case "Right": case "LineStart": case "LineEnd": moveCaret(element, action); break;
                case "Insert": startTyping(element, "caret"); break;
                case "Append": startTyping(element, "after"); break;
                case "InsertAtStart": startTyping(element, "start"); break;
                case "AppendAtEnd": startTyping(element, "end"); break;
                case "Change": startTyping(element, "over"); break;
            }
            return;
        }

        switch (action) {
            case "Down": case "NextControl": stepControl(element, 1); break;
            case "Up": case "PreviousControl": stepControl(element, -1); break;
            case "Left": press(element || document.body, "ArrowLeft"); break;
            case "Right": press(element || document.body, "ArrowRight"); break;
        }
    }

    // One typed character: it completes a key, starts a two-key one (gg, gt, dd, cc), or means nothing, which drops
    // whatever was half typed, as Vim's own gx does.
    function feed(key, context, element) {
        const typed = pending + key;
        const binding = bindings.find(candidate => candidate.typed && candidate.keys === typed && (candidate.anywhere || candidate.where.includes(context)));
        if (binding) {
            pending = "";
            run(binding.action, context, element);
        } else if (!pending && bindings.some(candidate => candidate.typed && candidate.keys.length === 2 && candidate.keys[0] === key)) {
            pending = key;
        } else {
            pending = "";
        }
    }

    function onKeyDown(event) {
        // A key the keys pressed themselves (press) is the control's to answer.
        if (!on || !event.isTrusted) {
            return;
        }

        const control = event.ctrlKey && !event.altKey && !event.metaKey;
        const isEscape = event.key === "Escape" || (control && event.key === "[");

        if (sheet) {
            swallow(event);
            if (isEscape || event.key === "q" || event.key === "?") {
                closeSheet();
            }
            return;
        }

        if (bar) {
            if (bar.element.contains(event.target)) {
                barKey(event, isEscape);
                return;
            }
            closeBar();
        }

        clearFlash();

        if (hints) {
            swallow(event);
            hintKey(event, isEscape);
            return;
        }

        // While a dialog is open the keys are for it, wherever the cursor was left: the list behind it isn't moved
        // through, and Esc closes it.
        let element = document.activeElement;
        const dialog = scope();
        if (dialog !== document.body && !(element instanceof HTMLElement && (dialog.contains(element) || element.closest(".e-popup-open")))) {
            element = null;
            if (event.key === "Escape") {
                swallow(event);
                press(dialog, "Escape");
                return;
            }
        }

        // Down and up an open list, with the hands where they are.
        if (control && !event.shiftKey && popupIsOpen()) {
            const key = event.key.toLowerCase();
            if (key === "j" || key === "n" || key === "k" || key === "p") {
                swallow(event);
                press(element || document.body, key === "j" || key === "n" ? "ArrowDown" : "ArrowUp");
                return;
            }
        }

        if (mode === "INSERT") {
            if (register) {
                register = false;
                if (event.key === "=") {
                    swallow(event);
                    openSumBar(element);
                    return;
                }
            }
            if (control && !event.shiftKey && event.key.toLowerCase() === "r" && isEditable(element)) {
                swallow(event);
                register = true;
                return;
            }
            if (isEscape) {
                // Typing stops, and what was typed stays. An open list closes first; a window closes on the next Esc.
                // The key the list is given bubbles: a Syncfusion list hears keys through Blazor, which listens at the
                // page's root, so one that didn't bubble left the list open. A Syncfusion window around the list
                // hears it too, but doesn't close on an Esc while a list is open.
                swallow(event);
                if (popupIsOpen()) {
                    press(element, "Escape");
                } else {
                    commit(element);
                }
                setMode("NORMAL");
            }
            return;
        }

        if (isEscape) {
            pending = "";
            if (event.key !== "Escape") {
                swallow(event);
                press(element || (dialog !== document.body ? dialog : document.body), "Escape");
            }
            return;
        }

        if (control && !event.shiftKey && event.key.toLowerCase() === "r") {
            swallow(event);
            undo(true);
            return;
        }

        if (event.ctrlKey || event.altKey || event.metaKey) {
            return;
        }

        // Letters don't type in NORMAL, and nor do the keys that rub out.
        if (event.key === "Backspace" || event.key === "Delete") {
            if (isEditable(element)) {
                swallow(event);
            }
            return;
        }

        // Space still presses a button or ticks a box; Enter, Tab and the arrows are never the keys'.
        if (event.key.length !== 1 || (event.key === " " && !isEditable(element))) {
            return;
        }

        swallow(event);
        feed(event.key, contextOf(element), element);
    }

    // A field that takes the cursor by a click or Tab is typed into; anything else ends the typing.
    function onFocusIn(event) {
        if (!on || (bar && bar.element.contains(event.target))) {
            return;
        }

        if (isEditable(event.target)) {
            if (!selfFocus) {
                setMode("INSERT");
            }
        } else if (mode === "INSERT") {
            setMode("NORMAL");
        }
    }

    function onFocusOut() {
        setTimeout(() => {
            if (on && !bar && mode === "INSERT" && !isEditable(document.activeElement)) {
                setMode("NORMAL");
            }
        }, 0);
    }

    // Clicking the field the cursor is already in starts typing too.
    function onMouseDown(event) {
        if (on && mode === "NORMAL" && isEditable(event.target)) {
            setMode("INSERT");
        }
        if (hints) {
            closeHints();
        }
    }

    function closeEverything() {
        closeHints();
        closeSheet();
        clearFlash();
        if (bar) {
            bar.element.remove();
            bar = null;
        }
    }

    return {
        // Turns the keys on, with the key table and the command list.
        enable(reference, keys, commandList) {
            dotNet = reference;
            bindings = keys;
            commands = commandList;
            if (!on) {
                on = true;
                document.addEventListener("keydown", onKeyDown, true);
                document.addEventListener("focusin", onFocusIn, true);
                document.addEventListener("focusout", onFocusOut, true);
                document.addEventListener("mousedown", onMouseDown, true);
            }
            setMode(isEditable(document.activeElement) ? "INSERT" : "NORMAL");
        },

        // Remembers where the cursor is and whether it was typing, for something that has to take it away for a moment
        // (undo leaves the field so the step shows in it). The function returned puts it back as it was, keys on or off:
        // in the same control, or, where the step drew a table's lines afresh, in the same cell of the same table.
        holdCursor() {
            const element = document.activeElement;
            const was = mode;
            const grid = element instanceof HTMLElement ? lineGridOf(element) : null;
            const place = grid && {
                grid: [...document.querySelectorAll(".line-grid")].indexOf(grid),
                row: [...grid.tBodies[0].rows].indexOf(element.closest("tr")),
                column: element.closest("td").cellIndex,
            };
            return () => {
                let target = element instanceof HTMLElement && element !== document.body && document.body.contains(element) ? element : null;
                if (!target && place) {
                    const table = document.querySelectorAll(".line-grid")[place.grid];
                    const rows = table ? lineRows(table) : [];
                    const row = rows[Math.min(place.row, rows.length - 1)];
                    target = row ? controlInColumn(row, place.column, 0) : null;
                }
                if (target) {
                    focusQuietly(target);
                    if (on) {
                        setMode(was === "INSERT" && isEditable(target) ? "INSERT" : "NORMAL");
                    }
                }
            };
        },

        // Puts the cursor in a control that takes the place of the one that has it, the mode staying as it was: a picker's
        // combo box, made once its look-alike had the cursor (PickField's Deferred). A move the keys made stays a move.
        focusKeepingMode(element) {
            focusQuietly(element);
        },

        disable() {
            if (on) {
                document.removeEventListener("keydown", onKeyDown, true);
                document.removeEventListener("focusin", onFocusIn, true);
                document.removeEventListener("focusout", onFocusOut, true);
                document.removeEventListener("mousedown", onMouseDown, true);
            }
            on = false;
            closeEverything();
            setMode("NORMAL");
        },
    };
})();
