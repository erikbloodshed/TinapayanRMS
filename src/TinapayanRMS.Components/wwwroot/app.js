// Small browser helpers the Blazor components call through JS interop (window.tinapayan).

// A number box's text, and a picker's (or its look-alike's, PickField's Deferred), is selected as it takes the cursor, so
// what's typed replaces it: without it a picker's typing is added to the
// name it holds, and matches nothing.
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
    if (input instanceof HTMLInputElement && !input.readOnly && (input.closest(".number-field, .deferred-pick") || input.classList.contains("e-combobox"))) {
        input.select();
        if (pressedOn instanceof Node && input.contains(pressedOn)) {
            input.addEventListener("mouseup", () => input.select(), { once: true });
        }
    }
});

// The pane a drag scrolls: the nearest element from `from` up that scrolls up and down and has more than it shows.
const scrollingPane = from => {
    for (let at = from; at && at !== document.body; at = at.parentElement) {
        const overflow = getComputedStyle(at).overflowY;
        if ((overflow === "auto" || overflow === "scroll") && at.scrollHeight > at.clientHeight) {
            return at;
        }
    }
    return null;
};

// A drag's scrolling, one animation frame at a time, for watchLineDrag and watchTreeDrag: with the pointer within 32
// pixels of its pane's top or bottom (drag.pane, drag.y), the pane scrolls that way, faster the nearer the edge, and while
// it moves, moved() tells what's under the pointer now and the next frame is asked for (drag.frame, 0 when none is).
// currentDrag() is the drag going on.
const dragScrolling = (currentDrag, moved) => {
    const edge = 32;
    const scroll = () => {
        const drag = currentDrag();
        drag.frame = 0;
        const pane = drag.pane;
        if (!pane) {
            return;
        }
        const box = pane.getBoundingClientRect();
        const step = drag.y < box.top + edge ? -Math.ceil((box.top + edge - drag.y) / 4)
            : drag.y > box.bottom - edge ? Math.ceil((drag.y - box.bottom + edge) / 4) : 0;
        if (step !== 0) {
            const before = pane.scrollTop;
            pane.scrollTop += step;
            if (pane.scrollTop !== before) {
                moved();
                drag.frame = requestAnimationFrame(scroll);
            }
        }
    };
    return scroll;
};

window.tinapayan = {
    // Scrolls an element into view only if it isn't already.
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
                dotNet.invokeMethodAsync("OpenFromKeyAsync").catch(error => console.error("Find didn't open.", error));
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

    // Dragging a table's lines into order by their row headers (LineGrid): by the pointer, not the browser's own drag and
    // drop, which the Windows web view never hands a drop back to the page from (WinUI's WebView2 starts the drag, and
    // the page sees no dragover or drop, so a real mouse moved nothing). A press on a row header marked data-drag, moved
    // a few pixels, drags its line; the line under the pointer shows where it would go (a bar above it when the line
    // goes up, below when it goes down, the line dragged faded), and the release there tells the table
    // (MoveLine(from, to), by the lines' places as drawn). Anywhere beside the table, a release on the line itself,
    // Escape or a lost pointer moves nothing. Near the top or bottom of the pane that scrolls, the pane scrolls.
    // A press whose release never reaches the table (its line redrawn from under the pointer, then let go beside the
    // table) drags nothing afterwards: the pointer moving with no button down ends it, and so does the next press.
    // Returns an object whose dispose() lets go of the table.
    watchLineDrag(table, dotNet) {
        if (!table) {
            return { dispose() { } };
        }
        const threshold = 4;
        let drag = null;

        const lines = () => [...table.tBodies[0].rows].filter(row => row.querySelector(":scope > td.row-header[data-drag]"));
        const scroller = () => scrollingPane(table.parentElement);
        const unmark = () => table.querySelectorAll("tr.drop-above, tr.drop-below, tr.dragging-line")
            .forEach(row => row.classList.remove("drop-above", "drop-below", "dragging-line"));

        // The line the pointer is over, held to the first and last lines above and below them; none beside the table.
        const targetAt = (x, y) => {
            const box = table.getBoundingClientRect();
            if (x < box.left || x > box.right) {
                return null;
            }
            const rows = lines();
            if (rows.length === 0) {
                return null;
            }
            if (y < rows[0].getBoundingClientRect().top) {
                return rows[0];
            }
            return rows.find(row => y < row.getBoundingClientRect().bottom) ?? rows[rows.length - 1];
        };

        const mark = () => {
            unmark();
            drag.source.classList.add("dragging-line");
            const rows = lines();
            const from = rows.indexOf(drag.source);
            const target = targetAt(drag.x, drag.y);
            const to = target ? rows.indexOf(target) : -1;
            drag.to = to >= 0 && from >= 0 && to !== from ? to : -1;
            drag.from = from;
            if (drag.to >= 0) {
                target.classList.add(drag.to < from ? "drop-above" : "drop-below");
            }
        };

        const scroll = dragScrolling(() => drag, mark);

        const end = drop => {
            if (!drag) {
                return;
            }
            const { header, pointerId, moving, from, to, frame } = drag;
            drag = null;
            cancelAnimationFrame(frame);
            unmark();
            table.classList.remove("dragging-lines");
            document.removeEventListener("keydown", onKey, true);
            if (header.hasPointerCapture(pointerId)) {
                header.releasePointerCapture(pointerId);
            }
            if (drop && moving && from >= 0 && to >= 0) {
                dotNet.invokeMethodAsync("MoveLine", from, to).catch(() => { });
            }
        };

        const onKey = e => {
            if (e.key === "Escape") {
                e.preventDefault();
                e.stopPropagation();
                end(false);
            }
        };

        const onDown = e => {
            // A drag under way keeps the pointer it holds: a second finger starts nothing.
            if (drag?.moving && drag.header.hasPointerCapture(drag.pointerId)) {
                return;
            }
            // Any other drag still here is a press whose release the table never saw: a new press starts afresh, as a
            // list's does (watchTreeDrag).
            end(false);
            const header = e.button === 0 ? e.target.closest("td.row-header[data-drag]") : null;
            if (!header || !table.tBodies[0].contains(header)) {
                return;
            }
            drag = {
                header, source: header.parentElement, pointerId: e.pointerId, startX: e.clientX, startY: e.clientY,
                x: e.clientX, y: e.clientY, moving: false, from: -1, to: -1, frame: 0, pane: null
            };
            header.setPointerCapture(e.pointerId);
        };

        const onMove = e => {
            if (!drag || e.pointerId !== drag.pointerId) {
                return;
            }
            // No button down: the press ended where the table never saw it, so nothing is being dragged. Without this
            // the pointer coming back over the table dragged the line by itself.
            if (!(e.buttons & 1)) {
                end(false);
                return;
            }
            drag.x = e.clientX;
            drag.y = e.clientY;
            if (!drag.moving) {
                if (Math.abs(drag.x - drag.startX) < threshold && Math.abs(drag.y - drag.startY) < threshold) {
                    return;
                }
                drag.moving = true;
                drag.pane = scroller();
                table.classList.add("dragging-lines");
                document.addEventListener("keydown", onKey, true);
            }
            e.preventDefault();
            mark();
            if (!drag.frame) {
                drag.frame = requestAnimationFrame(scroll);
            }
        };

        const onUp = e => {
            if (drag && e.pointerId === drag.pointerId) {
                drag.x = e.clientX;
                drag.y = e.clientY;
                if (drag.moving) {
                    mark();
                }
                end(true);
            }
        };

        const onCancel = e => {
            if (drag && e.pointerId === drag.pointerId) {
                end(false);
            }
        };

        table.addEventListener("pointerdown", onDown);
        table.addEventListener("pointermove", onMove);
        table.addEventListener("pointerup", onUp);
        table.addEventListener("pointercancel", onCancel);
        table.addEventListener("lostpointercapture", onCancel);
        return {
            dispose() {
                end(false);
                table.removeEventListener("pointerdown", onDown);
                table.removeEventListener("pointermove", onMove);
                table.removeEventListener("pointerup", onUp);
                table.removeEventListener("pointercancel", onCancel);
                table.removeEventListener("lostpointercapture", onCancel);
            }
        };
    },

    // Dragging a list's rows onto a folder (FolderTree): by the pointer, as a table's lines are (watchLineDrag), not the
    // browser's own drag and drop, which the Windows web view doesn't hand back to the page, and under which the list
    // never scrolled. A press on a row marked data-drag, moved a few pixels, drags it with the rows picked (the tree says
    // which: Start); the row under the pointer is told to the tree as it changes (Over, its element's id, or null over the
    // list's empty space), and the release there drops on it (Drop). Near the top or bottom of the list, it scrolls, so a
    // folder far below can be reached; the row under the pointer is told again as it scrolls past. A release beside the
    // list, Escape or a lost pointer drops nothing (Cancel). A press let go where the list never saw it (beside the list,
    // the pointer gone before it had moved over it) drags nothing afterwards: the pointer coming back with no button down
    // ends it, and so does the next press. The click a release makes on the row is swallowed, so a drop
    // doesn't pick or open it too. While it drags, a see-through copy of the row follows the pointer, as File Explorer's
    // does: its icon and name, how many rows go with it when there are several, and "Move to Cakes" under it while it's over
    // a row they can drop on. Returns an object whose dispose() lets go of the list.
    watchTreeDrag(tree, dotNet) {
        if (!tree) {
            return { dispose() { } };
        }
        const threshold = 4;
        let drag = null;

        const scroller = () => scrollingPane(tree);

        const isInside = (x, y) => {
            const box = tree.getBoundingClientRect();
            return x >= box.left && x <= box.right && y >= box.top && y <= box.bottom;
        };

        // The row under the pointer, by its element's id; null over the list's empty space or beside the list.
        const rowAt = (x, y) => {
            if (!isInside(x, y)) {
                return null;
            }
            const row = document.elementFromPoint(x, y)?.closest(".tree-row");
            return row && tree.contains(row) ? row.id : null;
        };

        // The see-through copy of the row pressed that follows the pointer: made as the drag starts, gone as it ends.
        const makeGhost = row => {
            const ghost = document.createElement("div");
            ghost.className = "tree-drag-ghost";
            ghost.setAttribute("aria-hidden", "true");
            const item = document.createElement("div");
            item.className = "tree-drag-ghost-item";
            const glyph = row.querySelector(".tree-glyph");
            if (glyph) {
                item.appendChild(glyph.cloneNode(true));
            }
            const title = document.createElement("span");
            title.className = "tree-drag-ghost-title";
            title.textContent = (row.querySelector(".tree-title") ?? row).textContent.trim();
            item.appendChild(title);
            const count = document.createElement("span");
            count.className = "tree-drag-ghost-count";
            count.hidden = true;
            item.appendChild(count);
            const caption = document.createElement("div");
            caption.className = "tree-drag-ghost-caption";
            caption.hidden = true;
            ghost.append(item, caption);
            document.body.appendChild(ghost);
            return { element: ghost, count, caption };
        };

        const placeGhost = () => {
            if (drag?.ghost) {
                drag.ghost.element.style.transform = `translate(${drag.x + 14}px, ${drag.y + 16}px)`;
            }
        };

        const titleOf = id => {
            const row = id ? document.getElementById(id) : null;
            return (row?.querySelector(".tree-title") ?? row)?.textContent.trim() ?? "";
        };

        const tell = () => {
            const over = rowAt(drag.x, drag.y);
            if (over !== drag.over) {
                drag.over = over;
                const asked = drag;
                asked.ghost.caption.hidden = true;
                dotNet.invokeMethodAsync("Over", over)
                    .then(can => {
                        // Only for the row it's still over: an answer for one left since says nothing.
                        if (drag === asked && asked.over === over && can && over) {
                            asked.ghost.caption.textContent = "→ Move to " + titleOf(over);
                            asked.ghost.caption.hidden = false;
                        }
                    })
                    .catch(() => { });
            }
        };

        const scroll = dragScrolling(() => drag, tell);

        // The click the release makes, on the row the drag started from: swallowed before the page's own handlers see it.
        const swallowClick = () => {
            const swallow = e => {
                e.stopPropagation();
                e.preventDefault();
                window.removeEventListener("click", swallow, true);
            };
            window.addEventListener("click", swallow, true);
            setTimeout(() => window.removeEventListener("click", swallow, true), 400);
        };

        const end = drop => {
            if (!drag) {
                return;
            }
            const { pointerId, moving, frame, x, y, ghost } = drag;
            drag = null;
            cancelAnimationFrame(frame);
            ghost?.element.remove();
            tree.classList.remove("dragging-rows");
            document.removeEventListener("keydown", onKey, true);
            if (tree.hasPointerCapture(pointerId)) {
                tree.releasePointerCapture(pointerId);
            }
            if (!moving) {
                return;
            }
            swallowClick();
            if (drop && isInside(x, y)) {
                dotNet.invokeMethodAsync("Drop", rowAt(x, y)).catch(() => { });
            } else {
                dotNet.invokeMethodAsync("Cancel").catch(() => { });
            }
        };

        const onKey = e => {
            if (e.key === "Escape") {
                e.preventDefault();
                e.stopPropagation();
                end(false);
            }
        };

        const onDown = e => {
            if (drag?.moving) {
                return;
            }
            // A press that never moved, and was let go beside the list, left its drag behind: a new press starts afresh.
            drag = null;
            const row = e.button === 0 && !e.target.closest("input, textarea, select") ? e.target.closest(".tree-row[data-drag]") : null;
            if (!row || !tree.contains(row)) {
                return;
            }
            drag = {
                row, pointerId: e.pointerId, startX: e.clientX, startY: e.clientY, x: e.clientX, y: e.clientY,
                moving: false, over: undefined, frame: 0, pane: null, ghost: null
            };
        };

        const onMove = e => {
            if (!drag || e.pointerId !== drag.pointerId) {
                return;
            }
            // No button down: the press ended where the list never saw it, so nothing is being dragged. Without this the
            // pointer coming back over the list started the drag by itself, and the next click in the list dropped the
            // row there, a move saved at once.
            if (!(e.buttons & 1)) {
                end(false);
                return;
            }
            drag.x = e.clientX;
            drag.y = e.clientY;
            if (!drag.moving) {
                if (Math.abs(drag.x - drag.startX) < threshold && Math.abs(drag.y - drag.startY) < threshold) {
                    return;
                }
                drag.moving = true;
                drag.pane = scroller();
                drag.ghost = makeGhost(drag.row);
                tree.setPointerCapture(drag.pointerId);
                tree.classList.add("dragging-rows");
                document.addEventListener("keydown", onKey, true);
                const started = drag;
                dotNet.invokeMethodAsync("Start", drag.row.id)
                    .then(count => {
                        if (drag !== started) {
                            return;
                        }
                        // A row that can't be dragged drags nothing: no copy of it follows the pointer.
                        if (!count) {
                            started.ghost.element.remove();
                        } else if (count > 1) {
                            started.ghost.count.textContent = String(count);
                            started.ghost.count.hidden = false;
                        }
                    })
                    .catch(() => { });
            }
            e.preventDefault();
            placeGhost();
            tell();
            if (!drag.frame) {
                drag.frame = requestAnimationFrame(scroll);
            }
        };

        const onUp = e => {
            if (drag && e.pointerId === drag.pointerId) {
                drag.x = e.clientX;
                drag.y = e.clientY;
                end(true);
            }
        };

        const onCancel = e => {
            if (drag && e.pointerId === drag.pointerId) {
                end(false);
            }
        };

        tree.addEventListener("pointerdown", onDown);
        tree.addEventListener("pointermove", onMove);
        tree.addEventListener("pointerup", onUp);
        tree.addEventListener("pointercancel", onCancel);
        tree.addEventListener("lostpointercapture", onCancel);
        return {
            dispose() {
                end(false);
                tree.removeEventListener("pointerdown", onDown);
                tree.removeEventListener("pointermove", onMove);
                tree.removeEventListener("pointerup", onUp);
                tree.removeEventListener("pointercancel", onCancel);
                tree.removeEventListener("lostpointercapture", onCancel);
            }
        };
    },

    // A picker shown by a look-alike until it has the cursor (PickField's Deferred), its combo box now made beside it,
    // unseen: the cursor goes to the combo box, the Vim keys' mode staying as it was (a move the keys made stays a move);
    // a press on the look-alike's arrow is a click into the picker, which starts typing, as a click on the combo box's own
    // arrow did. What was typed into the look-alike meanwhile goes with it, and the combo box filters on it as it would on
    // a key (its input, then the key's keyup); else the combo box's text is selected, as any picker's is when it takes the
    // cursor. True if the combo box has the cursor: not when the cursor went on elsewhere first, unless the arrow was
    // pressed.
    handOverPick(cell, pressed) {
        const from = cell?.querySelector(".deferred-pick input");
        const to = cell?.querySelector("input.e-combobox");
        if (!to) {
            return false;
        }
        if (document.activeElement === to) {
            return true;
        }
        if (!from || (!pressed && document.activeElement !== from)) {
            return false;
        }

        const typed = from.value !== from.dataset.text ? from.value : null;
        if (window.tinapayanVim && !pressed) {
            window.tinapayanVim.focusKeepingMode(to);
        } else {
            to.focus();
        }
        if (typed !== null) {
            to.value = typed;
            to.setSelectionRange(typed.length, typed.length);
            const last = typed.slice(-1);
            const key = last || "Backspace";
            const code = !last ? "Backspace" : /[a-z]/i.test(last) ? "Key" + last.toUpperCase() : /\d/.test(last) ? "Digit" + last : last === " " ? "Space" : "";
            to.dispatchEvent(new Event("input", { bubbles: true }));
            to.dispatchEvent(new KeyboardEvent("keyup", { key, code, bubbles: true }));
        }
        return true;
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
            // The cursor goes back whatever the step did: a step that failed left it in no field at all.
            setTimeout(() => dotNet.invokeMethodAsync("Undo", redo)
                .catch(error => console.error("Undo didn't go through.", error))
                .finally(restore), 0);
        };
        document.addEventListener("keydown", handler);
        return { dispose: () => document.removeEventListener("keydown", handler) };
    },

    // While the window is locked (Layout/SignInScreen), no key reaches the screens waiting under the sign-in panel: the
    // document's own key watches (Find, Undo, a list's keys) and Blazor's handlers never see one. Caught on the window, as
    // it starts down, before any of them. A key outside the panel is stopped there, and so is every Ctrl, Alt or Win
    // combination anywhere: the ones that edit a box's text (select all, copy, cut, paste, undo) still do so in the
    // panel's boxes, and the rest (Alt+Left, Ctrl+P, F5…) do nothing at all. Plain keys in the panel's boxes type, and
    // Enter signs in. Returns an object whose dispose() lets keys through again.
    lockKeys(panel) {
        const editing = ["a", "c", "v", "x", "z", "y"];
        const guard = e => {
            const inPanel = panel?.contains(e.target) ?? false;
            const combination = e.ctrlKey || e.altKey || e.metaKey;
            const browserKey = e.key === "F5" || e.key === "F3" || e.key === "F7" || e.key?.startsWith("Browser");
            if (inPanel && !combination && !browserKey) {
                return;
            }
            e.stopImmediatePropagation();
            const editsText = inPanel && e.ctrlKey && !e.altKey && !e.metaKey && editing.includes(e.key?.toLowerCase());
            if ((combination || browserKey) && !editsText) {
                e.preventDefault();
            }
        };
        window.addEventListener("keydown", guard, true);
        window.addEventListener("keyup", guard, true);
        return {
            dispose: () => {
                window.removeEventListener("keydown", guard, true);
                window.removeEventListener("keyup", guard, true);
            },
        };
    },

    // A key, click, scroll or mouse move anywhere in the window, told to the app at most once every everyMs, for its
    // auto-lock (AppLock.NoteActivity). Returns an object whose dispose() stops it.
    watchActivity(dotNet, everyMs) {
        let last = 0;
        const note = () => {
            const now = Date.now();
            if (now - last >= everyMs) {
                last = now;
                dotNet.invokeMethodAsync("NoteActivity")
                    .catch(error => console.error("The auto-lock wasn't told of a key or click.", error));
            }
        };
        const kinds = ["keydown", "pointerdown", "pointermove", "wheel"];
        kinds.forEach(kind => window.addEventListener(kind, note, { capture: true, passive: true }));
        return { dispose: () => kinds.forEach(kind => window.removeEventListener(kind, note, { capture: true })) };
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
        // Sent only when something moved: the window keeps what it was told last, and sets it again itself after a change
        // of style (AppWindowChrome.AfterStyleChanged).
        let sent = "";
        const send = () => {
            queued = false;
            const titleBar = device(bar.getBoundingClientRect());
            const buttons = [...bar.querySelectorAll("button, a, input")]
                .filter(e => e.getClientRects().length > 0)
                .map(e => device(e.getBoundingClientRect()))
                .filter(r => r.width > 0 && r.height > 0);
            const told = JSON.stringify([titleBar, buttons]);
            if (told !== sent) {
                sent = told;
                dotNet.invokeMethodAsync("SetTitleBar", titleBar, buttons)
                    .catch(error => console.error("The window wasn't told where its title bar is.", error));
            }
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
            "HistoryChanged", window.navigation?.canGoBack ?? true, window.navigation?.canGoForward ?? true)
            .catch(error => console.error("Back and Forward weren't updated.", error));
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
