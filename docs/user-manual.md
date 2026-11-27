# TinapayanRMS User Manual

TinapayanRMS keeps the recipes and costs of the bakeshop's products in one place. You record what raw materials cost. The app then works out what every recipe, every finished product and every selling price comes to. When a price changes, it carries the change up to every product that uses that material.

This manual is for the people who use the app day to day. It doesn't assume any technical knowledge.

> **Amounts are shown without a currency symbol.** Every amount in the app is in pesos. Costs are shown to 4 decimal places where small figures matter, such as the cost per gram, and to 2 decimal places elsewhere. Numbers always use a dot for decimals and commas between thousands, as in **1,250.50**, whatever Windows' region setting uses, and you type them the same way. Dates and the names of months follow the region.

## Contents

1. [Key ideas](#1-key-ideas)
2. [Getting around the app](#2-getting-around-the-app)
3. [First-time setup](#3-first-time-setup)
4. [Settings](#4-settings)
5. [Raw Materials](#5-raw-materials)
6. [Sub-Assemblies](#6-sub-assemblies)
7. [Products](#7-products)
8. [Printing recipes and costings](#8-printing-recipes-and-costings)
9. [Reports](#9-reports)
10. [Price Simulator](#10-price-simulator)
11. [Production Plan](#11-production-plan)
12. [How costs are worked out](#12-how-costs-are-worked-out)
13. [How to…](#13-how-to)
14. [Troubleshooting and questions](#14-troubleshooting-and-questions)

---

## 1. Key ideas

The app is built from a few ideas. Knowing them makes every screen easier to follow.

### Raw materials: ingredients and packaging

A **raw material** is anything the shop buys or makes to use in its products. Each one is one of two **types**:

- **Ingredient**: goes into recipes (flour, sugar, butter, eggs). Ingredients are always measured in **grams**.
- **Packaging**: goes on a product's packaging (boxes, bags, wrappers, ribbon, labels). Each packaging material has a **base unit** of its own, such as a piece for boxes or a centimetre for ribbon.

A material's type can't be changed after it's saved.

### Units: base unit and alternative units

Every material's quantities and costs are tracked in its **base unit**: the gram for an ingredient, or the unit you chose for a packaging material.

You can also give a material any number of **alternative units**. Each one is a multiple of the base unit. Examples:

- a *bag* of flour = 25,000 g
- a *tray* of eggs = 1,500 g
- a *pack* of boxes = 100 pieces
- a *roll* of ribbon = 1,000 cm

**Standard units have fixed multipliers**, and the app fills them in for you. A kilogram of an ingredient is always 1,000 g. An inch of a packaging material measured in centimetres is always 2.54 cm. The app recognises these standard units by their name or abbreviation:

- **Mass**: mg, g, kg (kilo), oz, lb
- **Length**: mm, cm, m, in, ft
- **Volume**: ml, l

You type every other multiplier yourself. This includes a bag, and a litre of an ingredient, because only its density says how many grams a litre weighs.

### Where a cost comes from: the source

Every ingredient has a **source**, which is set when it's created and can't be changed afterwards:

- **Purchased**: bought from a supplier. Its cost comes from the brand prices you enter. Packaging is always purchased.
- **Made**: made in the shop from other ingredients, such as a syrup or an icing. Its recipe is a **sub-assembly** on the Sub-Assemblies screen, the one place that recipe is written. Its cost comes from that recipe's ingredients, any **add-ins** of its own on top, and its **weight loss %**, the weight lost while making it.
- **Linked**: a part of another ingredient, such as egg white and egg yolk from a whole egg. Its cost is a share of that ingredient's cost.

An ingredient's cost can't end up coming from itself. A recipe line, an add-in or a link that would close such a loop is refused, and the red notice names the two ingredients in it, for example "Egg White can't be costed from Meringue Base: Meringue Base is already costed from Egg White…".

### Cost buffer and buffered cost

A **cost buffer %** is a cushion on top of what a purchased material really costs. It covers price swings, waste and variation in yield.

The **buffered cost** is the cost with that cushion on top. **Recipes and products are always costed at buffered costs.** To see what a product costs without the cushion, press **Unbuffered** in the Products header (see [The header](#the-header-1)), or on the Price Simulator (see [Buffered and unbuffered costs](#buffered-and-unbuffered-costs)).

For a purchased material, the buffered cost is a **fixed amount**. A new brand price doesn't move it on its own. See [Cost buffer](#cost-buffer) for how to update it.

### Wastage

**Wastage %** is a cost allowance for what's lost making a product: dough left in the mixer, a filling that cooks down, a tray that burns. It's one figure for the whole shop, set in Settings → Costing Defaults (3% unless you change it there), and a product category can set its own in its place (Settings → Product Categories): say 5% for Cakes, whose batters stick to the pans, while everything else stays at the shop's 3%. Every product adds its category's figure, or the shop's if its category doesn't set one, to its costing as a percentage of its **sub-assemblies (ingredients) cost**: 3% of 100.00 of ingredients is 3.00 more. Packaging isn't wasted, so it's left out.

Wastage is a cost, not lost weight. A batch's pieces share out the whole batch, and the wastage goes on the product's Costing tab. A composite product adds it once, on everything it holds, so its products' ingredients are never charged wastage twice. It uses its own category's figure (or the shop's), not those of the products in it.

### Sub-assemblies and roles

A **sub-assembly** is one part of a recipe that's made as a batch: a dough, a filling, a topping, a glaze. It's a list of ingredients in grams.

Every sub-assembly has a **role** that says what it is in the recipe, such as Dough, Filling, Topping or Packaging. Roles also set the order in which a product's sections appear.

A product uses a sub-assembly in one of two ways:

- **Linked**: the product uses a sub-assembly that's kept on the Sub-Assemblies screen. Every product that links it follows every change made to it. Use this when several products share one dough, for example.
- **Its own**: the sub-assembly belongs to that one product. Other products can copy it but can't link it, except its variants (see [Clone tab](#clone-tab)). A role can be set to be **product-owned** in Settings. A section made in a product in that role is then always the product's own.

A sub-assembly in the **Packaging** role is a **packaging set**, such as a gift box with its ribbon and tag. It holds packaging materials instead of ingredients.

A sub-assembly on the Sub-Assemblies screen can also be the recipe of a **made ingredient**, such as Butter Icing, so other recipes can use it as one ingredient. The made ingredient follows every change made to the sub-assembly, as linking products do.

### Products

A **product** is something the shop sells. It is made from:

- its **recipe**: one or more sub-assemblies, each with how much of it one piece takes
- its **packaging**
- its **costing**: the percentages that turn the cost of materials into a selling price

Other terms you'll see on a product:

- **Quantity Required** is how many finished pieces make one sellable unit. It's 1 for a product sold by the piece, and 8 for a "Box of 8". The recipe is always written per piece. The costing is per sellable unit.
- **Stage**: every product is either in **Innovation** (still being worked out) or in **Production** (made and sold). A new product goes in Production, unless you change that in Settings → Costing Defaults.
- **Clone** and **Create Variant** start a new product from an existing one. See [Clone tab](#clone-tab).
- **Versions**: each Save that changes a product's recipe or price is kept as a version you can compare with and go back to. See [History tab](#history-tab).

### Composite products

A **composite product** is made of two or more other products, such as a *Pasalubong Box* of 6 Ensaymada and 4 Cheese Rolls. Its recipe is a list of products and how many of each go in. It has its own packaging and its own costing.

- Each product in it brings its **recipe cost**: what its sub-assemblies (ingredients) cost for one of it, as that product's own Costing tab shows.
- A product's packaging stays out. The composite's own packaging covers the box and anything wrapping the pieces in it. If the pieces go into the box already in their own packaging, tick **Include each product's packaging** on the composite's Products tab. Each product's packaging then joins the composite's.
- It can also have sub-assemblies of its own, such as icing over a whole party box, but only in **product-owned** roles (see [Roles](#roles)). With the usual roles that's Topping, Filling and Pan Greasing, never Dough or Sponge. Its sections are sized for one whole composite.
- A composite can hold another composite, but it can never end up holding itself.
- A composite is always one sellable unit, so it has no **Quantity Required**.
- You choose whether a product is a composite when you make it. That can't change once it's saved.

### Piece size: weight per piece and yield

For each sub-assembly in a product you say how much of the batch one piece takes. Type **either** figure, and the app works out the other:

- **Weight/piece (g)**: how many grams of the batch one piece takes
- **Yield (pcs)**: how many pieces one batch makes

The pieces share out the whole batch: its **total weight**, the Grams column added up. What's lost making the product is costed as [wastage](#wastage), not taken off the batch.

The figure you typed stays put. If the batch changes, the other figure is worked out again.

A new section in a **product-owned** role, such as a Topping or a Filling, starts at a **Yield of 1**: one piece takes the whole batch. Its Weight/piece is then the total of the Grams column, and it follows the lines as you type them and whenever you change them later. Type a weight per piece or a yield instead if the batch makes more than one piece. A section in any other role, and any section you link, starts with no piece size.

A section that's [on another's lines](#a-section-on-another-sections-lines) makes its pieces of what that one leaves of its batch. It needn't have a piece size at all: the one that uses it counts its cost.

### Baker's % and Absolute %

Each recipe line also shows a percentage.

- **Baker's %**: tick the **Base** box on one or more lines, usually the flour. Every line is then shown as a percentage of the ticked lines' total weight. You can type either the grams or the baker's %, and the other is worked out. You can tick a line's Base while you're still typing the line. The empty row at the bottom shows no box until you start typing into it.
- **Absolute %**: with nothing ticked, each line is shown as its share of the whole batch. This figure is read-only.

The column heading changes to show which one you're looking at. Only the grams are ever used for costing.

---

## 2. Getting around the app

### The main window

- **The window** opens in the middle of the screen the mouse pointer is on. On a screen too small for it, it opens as large as the screen leaves room for, above the taskbar. Only one copy of the app opens on the PC at a time: opening it again brings the window that's open to the front. If it's open in another Windows sign-in on the PC, it says so and doesn't open (see [Troubleshooting](#14-troubleshooting-and-questions)).
- **The window doesn't zoom**: Ctrl with the mouse wheel, Ctrl with **+**, **-** or **0**, and a pinch on a touchpad leave it as it is. To make everything larger, change the scale in Windows' display settings.
- **The title bar**, along the top, has the app's name on the left, and **Find**, **Lock**, **Back**, **Forward**, then **Minimize**, **Maximize** and **Close** on the right. Drag the title bar by any empty part of it to move the window, drag it to the top of the screen or double-click it to make the window fill the screen, and right-click it for Windows' own window menu. While the window fills the screen, **Maximize** becomes **Restore Down**.
- **The activity bar**, the strip of icons down the left edge of the window, opens the screens: **Raw Materials**, **Sub-Assemblies**, **Products**, **Reports** and **Price Simulator** from the top, and **Settings** (the gear) at the bottom. Hold the pointer over an icon to see its name, in a tooltip to its right; the icon darkens while the pointer is on it. The other buttons and boxes have tooltips in the same look, which say in a line what they do: they show above (or below) what the pointer rests on. The screen showing has its icon darker, with a coloured line on its left.
  - Each screen keeps its place until you close the app. **Raw Materials**, **Sub-Assemblies** and **Products** keep every record you've opened, each in a tab of its own along the top, with what you've typed into it: going to another screen asks nothing, and coming back finds them as you left them (see [Editing and saving](#editing-and-saving)).
  - **Settings** opens on its first tab, **Units of Measure**.
- **Printing** opens the **Print** window over the screen you printed from, which stays as it was, changes not yet saved included. It shows the pages as they'll print, with **Print** and **Export to PDF** above them. Close it with its **✕** or **Esc** to go back to the screen (see [Printing recipes and costings](#8-printing-recipes-and-costings)).
- **Back and Forward** (the arrows at the top right, or **Alt+Left** and **Alt+Right**) move between the screens and the record tabs you've shown, Settings' tabs included, like a web browser. If the cursor was in a box when another record opens, the cursor leaves the box, so that you don't type over the record that just opened. Click the box again to type in it.
- **Find** (the magnifier at the top right, before Back, or **Ctrl+K** from anywhere) looks up any raw material, sub-assembly or product by name and opens it on its own screen: in its tab if it's open already, otherwise in the preview tab. Type part of the name: every word you type has to be in the name, in any order, and capitals and accents don't matter, so `ube ensay` finds *Ube Macapuno Ensaymada*. The closest names come first, with what each one is (Raw Material, Made Ingredient, Sub-Assembly or Product) and its category or role. **Up** and **Down** pick one, **Enter** or a double-click opens it, and **Esc** closes Find, at the first press even with the Vim keys on. The best 50 matches are listed; type more of the name to narrow them. Your last search is still there the next time you open Find. A sub-assembly that belongs to one product isn't listed: find the product instead. Deleted products aren't listed either. Changes you haven't saved on the screen you leave stay in their tab.
- Changes made on one screen show up on another the next time you open it.
- **Closing the window** closes the app. What you've saved is kept. If a raw material, sub-assembly or product open in a tab has changes you **haven't** saved, the app asks whether to save them first, for each tab with changes, whichever screen it's on (see [Editing and saving](#editing-and-saving)). While a save is still on its way, the window waits for it rather than ask. While **Settings → Database** is backing up, copying or restoring, the window doesn't close, and a red notice says why: close it once that's done (a restore restarts the app by itself).
- **Windows closing the app** (when you sign out, shut down or restart, or an installer updates or removes the app) works the same way. If there are changes you haven't saved, or **Settings → Database** is busy, the app stays open and asks its question (it comes back up first if it was minimized). When you're signing out or shutting down, Windows lists the app as keeping it waiting, with the reason (*A product, sub-assembly or raw material has changes you haven't saved.*): choose **Cancel** there, answer the app, then sign out or shut down again. **Sign out anyway** or **Shut down anyway** there closes the app without saving.

### Signing in and locking

The app has one password, for whoever uses it: there's no user name.

- **When the app opens** it asks for the password before it shows anything else: the whole window, under its title bar, is the sign-in page, with the app's name, the password box and the button in a column down the middle. Type it and press **Enter** or **Sign In**. The very first time, or after the password was cleared (see [Troubleshooting](#14-troubleshooting-and-questions)), it asks you to **set** one instead: type it in both boxes, at least 6 characters. Capitals, spaces and accents all count, exactly as typed.
- **Lock** (the padlock at the top right, before Back) covers the window with the password box at once, for when you step away. Everything stays as you left it underneath, changes not yet saved included, and the password brings you back to it. While it's locked, only the password box takes typing, and the title bar keeps just **Minimize**, **Maximize** and **Close**.
- **The window locks itself** after 15 minutes without a key press, a click, a scroll or a mouse movement in it. Change the time, or turn it off, in [Settings → Security](#security). A backup running in Settings → Database carries on while it's locked. Windows' print window counts as no movement in the app, so one left open is cancelled as the window locks, and nothing is printed: its preview doesn't stay on the screen for anyone to read. Sign in and press **Print** again to go on.
- **A wrong password** says so, and the box empties. After five wrong ones in a row, the next try has to wait 30 seconds.
- **Closing a locked window** still asks about changes you haven't saved, as it always does.
- The password is kept in the app's data, so it's the same for every Windows user on the PC, and a backup keeps the password it had: restoring an older backup brings back the password from then. Anyone who can sign in to Windows as the app's user can clear it (see [Troubleshooting](#14-troubleshooting-and-questions)), so keep that Windows sign-in to yourself too.

### Lists as folder trees

The **Raw Materials**, **Sub-Assemblies** and **Products** lists are trees of folders, like File Explorer:

- **Search** at the top of the list narrows it to matching names and opens the folders that hold them.
- **Drag** one or more items onto a folder to move them there. To reach a folder further down (or up), hold the item near the bottom (or top) edge of the list: it scrolls, faster the nearer the edge. While you drag, a see-through copy of the item follows the pointer, with a number on its corner when you drag several, and **→ Move to …** under it when it's over a folder it can go in, which is outlined too. Let go beside the list, or press **Esc**, to move nothing. A notice under the list says what moved, with an **Undo** button.
- **Right-click** an item, a folder or the empty space for a menu of what you can do there. Some menu items have keyboard shortcuts: **F2** renames and **Del** deletes.
- Folders you leave open or closed are the same the next time you use the app. A folder that's new since, such as a category, role or product folder you've just added, starts open, so whatever you save into it shows. The first time, every folder starts open, apart from the **Deleted** folder on Products.

### Editing and saving

- **One Save saves the whole record**, including everything on its tabs. Until you press **Save**, nothing you type is stored.
- **Undo** (**Ctrl+Z**) and **Redo** (**Ctrl+Y**) step back and forth through the changes you've made since opening the record.
- **Revert**, on the Products screen only, puts the whole product back as it was last saved.
- **Pickers** (the boxes with a ˅ arrow, such as Category, Role or Existing Sub-Assembly) can be typed into. Typing narrows the list to the names that contain what you typed, closest first: the name itself, then names starting with it, then names with a word starting with it, then the rest, so "salt" lists Sea Salt before Unsalted Butter. The closest is highlighted in blue, with a bar down its left side, and the box keeps just what you typed. **Enter** or **Tab** picks the highlighted name, so "marg" and Enter picks Margarine, and so does clicking somewhere else. **Down** and **Up** move the highlight through the list: the first press puts the highlighted name in the box, and each one after moves to the next. Before you type, the highlight is on the name the picker holds, or on an empty picker the first name, which Enter picks but Tab doesn't. The name under the mouse is the same blue, without the bar. **Backspace** takes off a letter at a time, and you can empty the picker to see the whole list. If what you typed matches nothing, or you leave the picker empty where a name is needed (a recipe line's ingredient), it shows what it had again once you leave it. **Esc** closes the list, and the picker keeps what it had. Clicking or tabbing into a picker selects its text, so what you type replaces it.
- **Number boxes take only numbers**, in the forms and in the tables alike: digits, commas between thousands, and one decimal point. A letter, a symbol or a second point doesn't appear when you press it. A box that holds a count, such as a quantity of pieces, takes no point either, and only a box whose figure can go below zero, such as the Price Simulator's **Change %**, takes a minus, at the start. Pasting or dropping a figure puts it in where the cursor is when it's one number, so `₱1,250.00` goes in as `1,250.00`; text with more than the number in it, such as `1/2` or `3 x 250 g`, and a figure the box can't take, such as `10-12` or a negative figure where none can go, is left out, and the box stays as it was.
- **New** (the **+** under the list's search box) starts a blank record, in the preview tab. **Delete** asks before deleting anything, and closes the deleted record's tab.
- **The buttons in a record's header show only their icons.** Point at one to see its name and what it does. The record's name is on its tab, not in the header.
- **Each raw material, sub-assembly and product you open has a tab of its own** (see Raw Materials' [Tabs](#tabs), Sub-Assemblies' [Tabs](#tabs-1) and Products' [Tabs](#tabs-2)), which keeps what you've typed, and its **Undo**, until you save it or close the tab. So going somewhere else asks nothing: picking another item in the list, **New**, **Copy**, another screen (from the activity bar, Find, a **Dependents** tile or another link), **Back** or **Forward** leave the changes in their tab.
- **Changes you haven't saved are never thrown away without asking.** A message asks **Save the changes to …?** when you close a tab that has them, and when you close the window, for each tab with changes, even one on another screen. **Clone**, **Create Variant** and **Apply Costing Defaults** ask too, before they work from a product with changes, since what they make or change is the product as saved. Reloading the page (**F5**) with changes not saved asks whether to leave instead.
  - **Yes** saves them, then goes on. If the save is refused, for example because a field is missing, you stay, and the red notice says why.
  - **No** throws them away, then goes on: the tab closes, or the product is put back as it was last saved. If it can't be put back, for example because the database isn't answering, the red notice says why and you stay.
  - **Cancel** keeps them, and you stay where you were.
  - The **Print** window doesn't ask, since it prints the item with your changes and leaves it as it was. Nor does **Copy**: on Sub-Assemblies the copy holds them, and on Raw Materials it opens beside them.
- **If another PC saves the same record while you have it open**, your **Save** is refused, so neither of you overwrites the other without knowing. So is renaming or moving that record in the list while you have unsaved changes on it, in whichever tab it's open, showing or not (a move of several records, or of a folder, is refused if any one of them is). A red notice says so, and what you typed stays on the form. [Troubleshooting](#14-troubleshooting-and-questions) says what to do next.
- A green notice confirms a save. A red notice explains anything the app refused, such as a missing field, a name already taken, or something that can't be deleted because it's in use. It also says when the app couldn't reach its database, or the database didn't answer in time. Nothing is saved then, and the screen keeps working: once the database answers again, try again, or pick the record again. A red notice stays until you close it or do something else, and goes when you open another item, answer **No** about unsaved changes, or leave the screen.
- If something goes wrong that the app didn't expect, a red notice says what happened, the screen keeps working, and the details go in the [log files](#14-troubleshooting-and-questions). Nothing you haven't saved is lost. If it was worse than that, the screen gives way to a panel saying **This screen ran into a problem it didn't expect**: press **Open It Again**, and the screen comes back as it was, changes you haven't saved included. The other screens keep working meanwhile. Only rarely, a bar at the bottom of the window says the page needs reloading: see [Troubleshooting](#14-troubleshooting-and-questions).

### Working in tables

- Most tables are typed straight into. Click a cell and type. The empty row at the bottom adds a new line.
- On that bottom row, **Tab** from its last cell adds the line, as **Enter** does, and opens the first cell of the next empty row, so you can type one line after another without reaching for the mouse.
- Pickers inside tables, such as the ingredient or material pickers, suggest names as you type.
- To change a line's ingredient, material or product, type over it. The line keeps what it had until you pick another, so its cost and the totals don't change while you type. Text that names nothing in the list leaves the line as it was.
- **Drag a row** by its left edge (the narrow cell before the first column) to change the order of recipe lines, add-ins, packaging lines and a composite product's products. While you drag, the line fades and a blue bar shows where it will go: above the line under the pointer when you drag up, below it when you drag down. Let go there, and the lines in between move up or down one to make room. Dragging near the top or bottom of the table scrolls it. Press **Esc**, or let go beside the table, to leave the order as it was. A move is one step for **Undo**, and the order is saved with the rest.
- **Insert Line** puts a blank line in above the selected one, so you don't have to add it at the bottom and drag it up. The new line's Ingredient cell takes the cursor, ready to type. With no line selected, the new line goes in at the bottom. You can also press **Insert** while the cursor is in the table. It's on the ingredient and packaging tables: a sub-assembly's or packaging set's lines on the Sub-Assemblies screen, a product's packaging, a product section's lines and add-ins (the add-ins button is **Insert Add-In**), and a made ingredient's add-ins. A line you leave blank does no harm, because Save leaves it out. **Undo** or **Remove Line** takes it away.
- **Remove Line** removes the selected line.
- Figures shown in grey are worked out by the app, so they can't be typed into.

### Vim keys

If you'd rather keep your hands on the keyboard, the app can be driven with keys like those of the Vim text editor. They're off until you turn them on in **Settings → Keyboard**, and the setting is only for you, on this computer.

With them on, the keys work in two modes. The mode shows at the top right of the window, before the Back arrow:

- **NORMAL**: letters move around the app and run commands. They don't type.
- **INSERT**: what you type goes into the field or table cell, as usual.

Clicking a field or cell, tabbing into it, or picking it by its label (**f**, below) puts you in INSERT, ready to type. **Esc** stops typing and goes back to NORMAL, keeping what you typed. In a picker with its list open, that Esc closes the list and puts back what the picker had, as Esc does there with the keys off. The cursor stays in the field, but in NORMAL **Backspace** and **Delete** don't rub out any more than letters type: click the field to type into it again.

**Ctrl+[** does everything Esc does, as in Vim, so you don't have to reach for the Esc key. It works wherever Esc does: in a field or cell, on the labels, the command bar and the list of keys, and to close a small window.

While a picker's list is open, **Ctrl+J** and **Ctrl+K** move down and up it, like the Down and Up arrows. **Ctrl+P** moves up too, as in Vim. (Vim's Ctrl+N belongs to the window's web view, which opens a new window with it.) That covers the list under a field you're typing a name into and the one in a table cell. With no list open, they do nothing.

- **Tab** takes you to the next field or cell still in INSERT, so just keep typing. Press **Esc** when you're done, and the letters move around again.
- In a small window that asks for a name or a Yes or No, the first Esc stops typing, and a second Esc closes the window. While such a window is open, **j** and **k** move between its buttons. **Find** closes at the first Esc, as its own keys (**Up**, **Down**, **Enter**) work while you type.

**Moving around**

- **j** and **k**: down and up. In a list or table that's a row. In a form, it's the next or previous field, like Tab and Shift+Tab.
- **h** and **l**: left and right. In a table that's a cell. In the Raw Materials, Sub-Assemblies and Products lists, **h** closes a folder and **l** opens it. In a field, they move the cursor.
- **gg** and **G**: the first and last row.
- **0** and **$**: the first and last column of a table, or the start and end of a field.
- **J** and **K**: leave the list or table, for the control after it or before it.
- **gt** and **gT**: the next and previous screen down the activity bar, round from the last to the first. They pass over **Settings**, because in Settings they move through its own tabs instead.
- **H** and **L**: Back and Forward, like **Alt+Left** and **Alt+Right**.
- **/**: the search box of the list on screen.
- **f**: puts a small yellow label on everything you can click. Type a label to click its item. Esc takes the labels away.

**Typing and changing**

- **i** or **a**: start typing in the field or cell. **I** and **A** start at the beginning or end of a field. In a table, they start in the row's first or last column.
- **cc**: type over a field or cell. Its text is selected, so what you type replaces it.
- **o**: start a new line in a table, in the empty row at the bottom.
- **O**: start a new line above the one under the cursor, as **Insert Line** does. It works in the ingredient tables that have that button.
- **dd**: delete the table line under the cursor. In the Raw Materials, Sub-Assemblies and Products lists, it deletes the row, as the row's menu does, and asks first.
- **r**: rename the list row, as the row's menu does.
- **u**: undo. **Ctrl+R**: redo. While the Vim keys are on, Ctrl+R doesn't reload the page as it otherwise does; use **F5** for that.

**Working out a sum while you type**

While you're typing in a table cell or a field (INSERT), press **Ctrl+R**, then **=**. A bar opens at the bottom of the window with an **=** in it. Type a sum and press **Enter**, and the answer goes into the cell, as if you'd typed it. For example, **5*(2+3)** types **25**.

- Use **+** and **-**, **\*** to multiply, **/** to divide, **%** for the remainder, and brackets. Use a dot for decimals, as in **1.5**. Commas in figures such as **1,250** are fine.
- Division keeps the decimals: **7/2** is 3.5.
- The answer goes in where the cursor is, over any selected text, like typing does. When you click a cell, its whole value is selected, so the answer replaces it.
- In a number cell or box, the answer is rounded to the number of decimals that cell or box takes. **20/3** in a cell that takes 2 decimals types 6.67.
- **Ctrl+V** pastes into the sum, and **Backspace** takes the last character off it.
- If the sum can't be worked out, for example **5*(2+3** with its closing bracket missing, the bar says why and stays open, so you can fix it. **Esc** closes the bar and leaves the cell as it was. Clicking anywhere closes it too.
- After Tab or Enter has taken you to the next cell, Ctrl+R = works there too, and the answer starts that cell's typing.
- Only the answer is kept, not the sum.

**Commands**

Type **:**, then a command, then **Enter**. A bar opens at the bottom of the window for it, and Esc closes it. Enter closes it too and runs the command. If the command can't run, for example **:w** when there's nothing to save, a bar at the bottom says why. That message goes away at your next key or click, or after a few seconds.

- **:w** saves.
- **:new** starts a new one.
- **:delete** deletes the one open, asking first.
- **:e** and a name opens the one whose name matches. For example, **:e pandesal** opens Pandesal on the Products screen.
- **:revert** throws away a product's changes since the last Save.
- **:raw**, **:sub**, **:products**, **:reports** and **:simulator** switch screens. **:print** opens the Print window on the product of the Products tab showing, as its **Print** button does.
- **:settings** opens Settings.
- **:help** lists every key.

A command can be shortened as long as it stays clear, as in **:del**, **:pro** and **:rep**.

**?** shows the keys for wherever you are. **Esc**, **q** or **?** closes the list.

---

## 3. First-time setup

On a new database, the app first asks you to set its password (see [Signing in and locking](#signing-in-and-locking)). Then set things up in this order. Each step uses what the one before it made.

1. **Units of measure** (Settings → Units of Measure): the units you buy and use things in: g, kg, piece, pack, bag, cm, roll, and so on.
2. **Material categories** (Settings → Material Categories): how raw materials are grouped, such as Flour, Dairy, In-House, Boxes and Ribbons. Each category is for ingredients, made ingredients or packaging.
3. **Product categories** (Settings → Product Categories): how products are grouped, such as Breads, Cakes and Pastries.
4. **Roles** (Settings → Roles): the parts of a recipe. Dough, Filling, Topping, Inclusion, Finishing and Packaging are there from the start.
5. **Costing defaults** (Settings → Costing Defaults): the stage a new product goes in, the labour, overhead, administrative and markup percentages that it starts with, the cost buffer a new raw material starts with, and the shop's VAT and wastage.
6. **Raw materials**: every purchased ingredient and packaging material with its prices, then the linked ingredients.
7. **Sub-assemblies**: the doughs, fillings and packaging sets shared by more than one product, and the recipes of the shop's made ingredients, such as a syrup or an icing.
8. **Made ingredients**, on the Raw Materials screen, each made from one of those sub-assemblies.
9. **Products**.

You don't have to finish each list before moving on. You can add a category or unit later, and most pickers let you add a new category just by typing its name.

---

## 4. Settings

Open **Settings** from the gear at the bottom of the activity bar. It opens as a screen of its own, and has nine tabs inside it: **Units of Measure**, **Material Categories**, **Product Categories**, **Roles**, **Costing Defaults**, **Printing**, **Keyboard**, **Security** and **Database**. Each list tab works the same way: pick a row to edit it, or press **New**. Then fill in the form and press **Save**, or press **Delete**. Leave Settings by clicking another screen's tab, or close its tab with its **✕**.

### Units of Measure

Each unit has a **Name** and an **Abbreviation** (for example *kilogram* and *kg*). Names are always saved in lowercase.

A unit that's in use can't be deleted. This means a price or a packaging line is in it, and the message tells you which. Renaming a unit renames it everywhere.

### Material Categories

Each category has a **Name** and a **Type**:

- **Ingredient**: for purchased and linked ingredients, such as Flour and Dairy.
- **Made**: for made ingredients, and only them, such as In-House.
- **Packaging**: for packaging materials, such as Boxes and Labels.

Every material goes in a category of its own type, and no other. A made ingredient can only go in a Made category, and a Made category takes nothing else.

A category's type can only change when everything in it fits the new type. For example, an ingredient category that holds only made ingredients can become a Made one. If anything in it wouldn't fit, the message says how many materials to move out first. A category with materials in it can't be deleted.

A made ingredient saved in an ingredient category before Made categories existed stays there, but it can't be saved again until it's in a Made category. Make a Made category, or change an ingredient category that holds only made ingredients to Made, then drag the made ingredient into it.

### Product Categories

Each category has a **Name**, and can have its own **Wastage %** (0 to 100), which its products are priced with in place of the shop's (see [Wastage](#wastage)). Leave it blank, as it starts, and its products use the shop's Wastage % from Costing Defaults. Changing it reprices the category's products, saved ones included.

A category can also have its own **Labor %**, **Overhead %**, **Administrative %** and **Markup %**: what a new product in the category starts with, each in place of the one in [Costing Defaults](#costing-defaults). Leave one blank, as each starts, and the category uses Costing Defaults' for it, so a Cakes category can set only its Labor % and Markup % and take the rest from Costing Defaults. These are starting values, as Costing Defaults' are: changing them changes no saved product. A new product you haven't saved yet takes its category's costing when you pick the category, and again if the category's costing changes, unless you've typed into it. **Apply Costing Defaults…** on the Products list gives each product its own category's (see [The list](#the-list-2) under Products).

A category can also have its own **Warn Below %**, in place of the one in [Costing Defaults](#costing-defaults): its products are flagged as short of their target markup only once their price earns under both their Markup Metric and this. Unlike the costing percentages, it reaches saved products too, as the Wastage % does.

The list shows each category's percentages, blank for those using Costing Defaults'. Products don't need a category, and the **category filter** on the Products list finds those without one under **Uncategorized**. A category isn't a folder of the Products list: the list's folders are your own (see [The list](#the-list-2) under Products). Deleting a category leaves its products uncategorized, in whatever folder they're in.

### Roles

Each role has:

- **Name**: what the section is called in a recipe, such as Dough or Filling.
- **Order**: where the role's sections go in a recipe. Lower numbers come first. A new role goes last. To change a role's place, pick it in the list and press **Move Up** or **Move Down** above the list. The roles are numbered 1, 2, 3… again after a move or a delete, so there are no gaps.
- **Product-owned**: a section made in a product in this role stays that product's own. It's edited in the product's recipe instead of on the Sub-Assemblies screen, and other products can copy it but not link it, except its variants. Turning this on or off for a role that's already used asks first, and says how many sub-assemblies will move. A section of one that you were changing in a product keeps what you typed, and that product's **Save** is refused until you press **Revert** and make the change again.
  - These are also the only roles a composite product can have sections in. So Product-owned can't be turned off while a composite has a section in the role; the message names the composites. Take the section out there first.
  - A section you make in a product in this role starts at a Yield of 1, so its Weight/piece is the whole batch (see [Piece size](#piece-size-weight-per-piece-and-yield)).
- **Into other sections**: a section in this role can be a line of another section of the same product, as a sponge goes into the dough (see [A section on another section's lines](#a-section-on-another-sections-lines)). Only sections in these roles are offered in a line's Ingredient list.
  - It can't be turned off while a product has a section in the role on another's lines; the message names the products. Take the section off those lines first.
  - The roles already used that way when this came in (2026-11-26) were ticked for you. Every other role starts without it, a new one too.

The **Packaging** role is special. Sub-assemblies in it are packaging sets. It can be renamed but not deleted, and it can't be product-owned or go into another section.

A role can't be deleted while any sub-assembly uses it.

### Costing Defaults

**New Products In** is where **New** on the Products screen files a product when you name it: **Production** (the default) or **Innovation**. That's the **New** shown while no product is open, and **New Product** under the list's search box while nothing in the list is highlighted. Like **Type** on Material Categories, it's a picker you can type into to narrow its list, as on the screens. **New Product Here**, and **New Product** with a folder or product highlighted, still file it in that folder, and a clone or variant still goes in Innovation, in the folder with the same path as its source's.

Under it are the four percentages a **new** product's costing starts with, unless its category sets its own (Settings → [Product Categories](#product-categories)):

- **Labor %**, **Overhead %** and **Administrative %** (office, permits, bookkeeping): each is a percentage of the product's sub-assemblies (ingredients) cost.
- **Markup %**: added on the base cost (materials, wastage, labour, overhead and administrative).

**VAT %** is the shop's VAT, added on top of every product's marked-up price. It isn't where a new product starts: **every** product is priced with it, saved ones included, so changing it changes every product's suggested price. A product's Costing Summary shows what it adds to the price, but you change it only here.

Under them, **Wastage %** is the shop's [wastage](#wastage): a percentage of every product's sub-assemblies (ingredients) cost, added to its costing. It starts at 3%, and can be anything from 0 to 100. Like the VAT, it isn't where a new product starts: **every** product is priced with it, saved ones included, unless its category sets its own (Settings → Product Categories), so changing it changes those products' costs and suggested prices.

**Price Rounding** rounds a suggested price up to a price you'd charge: up to the next 0.25, 0.50, 1, 5 or 10, or **None** (the default). It's always rounded up, so the rounded price still earns at least the markup. The exact figures don't change: the rounded price is shown beside them, in the last row of a product's Costing tab, under the **Suggested Price** (*Rounded up to the next 0.25*), in the **Rounded Price** column of Reports' Product Cost Summary, and in the Price Simulator.

**Warn Below %** is the markup a product's current price can fall to before it's flagged as short: the amber Markup % in its header and on its Costing tab, the ⚠ in the Products list, and the **Short By** column in Reports. A product is flagged only when its price earns under both its own **Markup Metric** and this. At 28%, a product aiming for 30% is flagged once it earns under 28%, and one aiming for 20% still once it earns under its 20%: a product that meets its own target is never flagged. Leave it blank, as it starts, to flag any shortfall from the Markup Metric. A product category can set its own in place of this one (Settings → [Product Categories](#product-categories)). It changes no cost or price, only which products are flagged.

**Cost Buffer %** is what a new raw material's [cost buffer](#cost-buffer) starts with on the Raw Materials screen. It starts at 10%, and can be anything from 0 to 100. It only matters for a purchased material: a linked ingredient takes the buffer of the one it's linked to, and a made ingredient has none of its own.

Each product keeps its own stage and percentages, and each raw material its own cost buffer, from its first Save. Changing those defaults doesn't change anything saved. Changing the Wastage % does: it reprices every product. A new product you haven't saved yet moves to the new stage, since you didn't pick one for it, and to the new percentages unless you've typed into them. A new raw material you haven't saved moves to the new cost buffer, unless you've typed one. **Undo** never brings the old defaults back on either: undoing your typing goes back to the new ones.

**Stored Costs**, to the right of the defaults, has a **Recalculate Costs** button. The app keeps every made and linked ingredient's cost up to date as you save. You only need this button if the database was changed outside the app, for example by a script. It works those costs out again from what they're made of, and a green notice says how many were out of date. Sub-assemblies and products need nothing: their costs are always worked out fresh from their lines. Purchased materials keep their prices.

### Printing

How printouts are laid out and what's on them: [Print](#8-printing-recipes-and-costings), [Export to PDF](#exporting-a-product-to-pdf) and the [Price Simulator's report](#printing-a-price-simulator-report) all use it. It has two cards, and either card's **Save** keeps both.

**Paper**:

- **Paper**: **A4 (210 × 297 mm)**, **Letter, short (8.5 × 11 in)**, **Long (8.5 × 13 in)** (long bond) or **Legal (8.5 × 14 in)**.
- **Orientation**: **Portrait**, or **Landscape** for the paper on its side: wider tables, fewer rows on each page.
- **Margins**: **Normal (14 mm)**, **Narrow (8 mm)** for more on each page, or **Wide (20 mm)** for room to write notes. The foot's margin is a little deeper, as the page number is printed in it.

Under them, a line says how much of the paper each page prints on, such as *Each page prints on 182 × 267 mm of the paper*. The pages break for the paper picked, so a **Continued on page** note names the right page. Choose the same paper in the print window when you print.

**On Each Printout**:

- **Shop Name** and **Address Line**: a heading at the top of each product's pages, and of a set's contents page, under a thin rule. The address line can hold anything that fits on one line, such as an address and a phone number. Leave both empty for no heading.
- **Logo**: press **Choose…** to pick a PNG or JPEG picture of at most 512 KB. It's printed at the heading's left, at most 16 mm tall. **Remove** takes it away. Either takes effect when you press **Save**.
- **Note at the Foot**: a short line, such as *For internal use only*, printed at the bottom left of every page, opposite the page number. It shows only on paper and in the PDF, not in the preview.
- **Type Size**: **Compact** (a tenth smaller, more rows on each page), **Normal** or **Large** (a fifth larger, easier to read at arm's length in the kitchen).
- **Print**: untick what you don't want on the paper. **Cost/g and Unit Cost** is each recipe line's cost per gram and each packaging line's unit cost; the lines' costs and totals still print, and a printout with **Sub-assembly total** ticked prints them anyway. **Baker's % and Absolute %** is the recipe's percentage column. **Figure tiles** are Material Cost, Base Cost, Current Price and Markup under each product's title. **Suggested beside current price** is the table of the suggested price's and the current price's markup, VAT and selling price under the costing.

These settings are kept in the database, so every computer the shop prints from uses them. Before they're saved, printouts are A4, portrait, with normal margins and normal type, with no heading or note, and with everything printed.

### Keyboard

**Vim keys** turns on the keys described in [Vim keys](#vim-keys). The change takes effect at once.

The setting is kept for you on this computer, not in the bakeshop's data. Turning it on doesn't change the app for anyone else, or for you on another computer.

### Security

The app's password and how soon the window locks itself (see [Signing in and locking](#signing-in-and-locking)). Both are kept in the app's data, not just for you.

- **Password**: to change it, type the **Current Password**, then the new one in **New Password** and again in **Type It Again**, and press **Change Password** (or **Enter**). The boxes empty after each try. If there's no password (after restoring a backup from before passwords, say), there's no Current Password box: type the new one twice and press **Set Password**.
- **Auto-Lock**: **Lock After (Minutes)** is how long the window waits, with no key press, click, scroll or mouse movement in it, before it locks itself: 1 to 240, or **0** to lock it only when you click **Lock**. Press **Save**; it takes effect at once.

### Database

Backups of all of the app's data: every raw material, price, sub-assembly, product and setting.

- **Folder** shows where the backups are kept, *C:\ProgramData\TinapayanRMS\Backups*. **Open Folder** shows it in File Explorer. Everyone who uses the app on this computer shares the folder.
- The list shows every backup, newest first: when it was **Taken**, its **Kind** and its **Size (MB)**.
  - **Manual**: taken with **Back Up Now**.
  - **Automatic**: the app takes one by itself each day, while it's open: when it starts, and if it's left open overnight, within the hour after midnight. The newest 14 are kept, and older automatic ones are deleted.
  - **Before restore**: the safety backup a restore takes of the data it replaces.
  - **Before update**: the safety backup the app takes the first time a new version opens, before it brings the data up to that version (see [What an update does](#what-an-update-does)).
  - **Copied in**: a backup brought in with **Restore from File…**.
  - Only automatic backups are ever deleted by the app. Delete others in File Explorer when you no longer need them.
- **Also Copy To** is a second folder each automatic backup is copied to as well, such as a USB stick that stays plugged in or a OneDrive folder. Press **Choose…** to pick it: the newest automatic backup is copied there straight away. The newest 14 automatic backups are kept there too; nothing else in the folder is touched. **Stop Copying** stops it, and leaves what was copied. If the folder isn't there when a backup is taken (the USB stick was taken out), the backup is still taken here, and a note in amber says it wasn't copied. The note stays until a copy goes through or you change the folder. The folder is kept for you on this computer.
  - While **Also Copy To** is empty, a reminder across the top of every screen says every backup is on this computer only. **Choose a Folder** opens this tab, and **×** hides the reminder until TinapayanRMS starts again.
- **Last Proved** says when a backup was last checked by restoring it. Once a week, after the day's automatic backup, the app restores the newest backup into a scratch copy, counts the raw materials, sub-assemblies and products in it, and deletes the copy. Your data isn't touched, and nothing changes on the screens. This shows that the backups would bring the data back. If the backup won't restore, a note in amber under **Last Proved** says why. A warning across the top of every screen says so too, until a backup restores. The app tries again the next day. Ask whoever set up the app to look into it.
- **App Version** shows which build of TinapayanRMS this is, such as *1.0 (commit 6c89b34)*. Give it when you report a problem.
- If an automatic backup can't be taken, a note in amber under **Folder** says why, and the app tries again within the hour. The note stays until one is taken, even if you close the app and open it again. If they keep failing, and the newest backup is three days old or more, a warning across the top of every screen says so until a backup is taken. Ask whoever set up the app to look into it.
- A file in the folder ending in *.partial* is a backup still being written, or one that was cut short, by a power cut for example. It isn't listed, it can't be restored, and the app deletes one that's left behind after a day.
- **Back Up Now** takes a backup straight away.
- **Copy To…** saves a copy of the backup picked in the list where you choose, such as a USB stick. Do this now and then: backups kept only on this computer are lost with it if it breaks.
- **Restore** puts the backup picked in the list back, in place of all of the app's data. It asks first, saying when the backup was taken. Then it takes a safety backup of the data as it is, puts the backup back, and every screen starts afresh with the restored data. Anything saved since the backup was taken is lost, and so is anything not yet saved on another screen.
  - A backup from an earlier version of TinapayanRMS is brought up to date as it's restored.
  - A backup from a later version is refused, and nothing changes. Update the app first.
  - A backup is checked and brought up to date before it takes the data's place. If it's refused, or anything goes wrong part-way, the data stays as it was.
- **Restore from File…** restores a backup file from elsewhere, such as a USB stick or another computer. Pick the file, and it's copied into the folder and restored as above.

A backup or a restore can take a little while. The tab says what it's doing until it's done, and the window can't be closed meanwhile. If the computer goes off in the middle of a restore, the app puts the data back as it was before the restore the next time it starts.

Backups are *.backup* files. The *.bak* files the app made before 21 October 2026, when it kept its data in SQL Server, aren't listed and can't be restored. Everything they held was carried over into the app's data on that day.

---

## 5. Raw Materials

The Raw Materials screen has the list of materials on the left, and to its right a tab for each material you open, with its details.

### Tabs

**Each material you open has a tab of its own**, along the top, as on the Sub-Assemblies screen, so you can have several open at once and go between them. Each keeps what you've typed into it, and its **Undo**, while another shows, and while you're on another screen.

- **A click on a material in the list** opens it in the **preview tab**, whose name is in *italics*. The next one you click takes the preview tab's place, so looking through the list doesn't fill the strip with tabs. A material that's open already just shows its own tab.
- **To keep a tab open**, double-click the material in the list, or double-click its tab. A tab you type into is kept too: the next click in the list opens in a new preview tab beside it.
- **New Raw Material** and **Copy** start the new material in the preview tab, so a tab with changes stays as it is. The screen opens on a blank new material, in the preview tab, as it always has.
- **A dot (●) on a tab** means it has changes you haven't saved. Its **✕** comes back when you point at it.
- **The tab's ✕ closes it.** If it has changes you haven't saved, it shows and asks **Save the changes to …?**: **Yes** saves them and closes the tab, **No** throws them away and closes it, and **Cancel** keeps the tab open. Closing the tab showing shows the one before it.
- **Back** and **Forward** go through the tabs you've shown, like a web browser. Going back to a material whose tab you've closed opens it again, in the preview tab.
- **Delete** closes the deleted material's tab.
- With every tab closed, the screen says so, with **New** beside it. Click a material in the list to open it.

### The list

The list has two top-level folders, **Ingredients** and **Packaging**, with a folder for each category inside them. Made categories sit inside **Ingredients**, among the ingredient ones. Every material is in a category.

Right-click menus:

- **A material**: **Move to** (another category of the same type: a made ingredient's are the Made ones), **Rename** (F2), **Copy** and **Delete** (Del).
- **A type's folder**: **New Ingredient Here** or **New Packaging Material Here**, **New Folder**, **New Made Folder** (Ingredients only), **Expand All** and **Collapse All**.
- **A category's folder**: **New … Here**, **New Folder**, **Rename Category** (F2) and **Delete Category** (Del). In a Made category's folder, **New Made Ingredient Here** starts a made ingredient in that category, and **New Folder** adds another Made category. You can only delete a category when it's empty. Otherwise the menu shows how many materials it holds.
- **The empty space**: **New Raw Material**, **New Ingredient Folder**, **New Made Folder**, **New Packaging Folder**, **Expand All** and **Collapse All**.

The same commands are buttons under the search box: **New Raw Material**, **New Folder** (its arrow offers an ingredient, a Made or a packaging folder), **Expand All** and **Collapse All**. Each button shows only its icon; point at it to see its name.

**Names are typed straight into the list**, as on the Products screen:

- **Rename** (or F2, or clicking the material you've already picked once more, slowly: a double-click doesn't rename) turns its name into a box. Type the new name and press **Enter**, or click away, and it's saved straight away. **Esc** leaves it as it was. **Rename Category** does the same for a category's folder.
- **New Folder** adds a *New folder* row, its name selected. Type the category's name and press **Enter**: the category is saved. **Esc**, an empty name, or clicking away before you've typed one, adds nothing.
- **New**, **New Raw Material** and **New … Here** add a row for the new material, its name selected: in the folder you picked, or at the end of **Ingredients** when it has no category yet. **New Raw Material** (under the search box, or in the empty space's menu) goes where the highlighted row in the list is: in that folder, or beside that material, in its category. A Made folder or a made ingredient starts a made one, and **Ingredients** or **Packaging** itself a material of that type with no category yet. With nothing highlighted, it starts an ingredient. Type its name and press **Enter**. The name goes into the form, and the row keeps it, but **the material isn't saved yet**: fill in the rest (its category, its prices, a packaging material's base unit, a made ingredient's sub-assembly) and press **Save**. **Esc**, or clicking away before you've typed a name, adds nothing: the form stays on the new material.

Dragging materials onto another category moves them straight away. A material can't be dragged into a category of another type, so a made ingredient only drops into a Made category, and nothing else does.

**Copy** starts a new material from the one you right-clicked, named after it with *Copy* on the end (*Disposable Spoon* becomes *Disposable Spoon Copy*, or *Disposable Spoon Copy 2* if that name is taken). Only its type, its category and its base unit come with it. Its prices, alternative units, cost buffer and the rest start empty, as for **New**. A copy of a made ingredient is a made ingredient in the same Made category, but you still pick its sub-assembly. Change the name if you like, fill in the rest, then press **Save**: nothing is saved until you do. The copy opens in the preview tab, so a tab with unsaved changes stays as it is.

### The header

At the top are **Save**, **Undo**, **Redo** and **Delete**, as icons: point at one to see its name. The material's name is on its tab, and **New Raw Material** is under the list's search box. Below them are these figures:

- **Base Unit**: *gram (g)* for an ingredient.
- **Current Cost per …**: the true cost, as last saved. For a purchased material it's the price of the brand in **Basis** (the dearest brand unless you pick one), per the unit it's shown in.
- **Buffered Cost per …**: the cost with the buffer on top. This is the figure recipes use.
- **Total Weight** and **Total Cost**, for a made ingredient only: what its batch yields after weight loss, and what that batch costs.
- **Last Updated**: when the material was last saved, and when its cost last changed.

**Delete** removes the material with its prices and alternative units, and closes its tab. While anything uses it, it stays, and the message names each one and how it uses it: a recipe or packaging set with it in its lines, a product with it in its packaging or as an add-in, a made ingredient with it as an add-in, or an ingredient linked to it. Take it out of those first.

Below the figures are the fields you fill in:

- **Name**: must be unique. The app tells you straight away if another material already has it.
- **Type**: Ingredient or Packaging. Only chosen for a new material.
- **Category**: pick one, or type a new name to add it. The app asks before adding. It lists only the categories the material can go in: the Made ones for a made ingredient, and the ingredient or packaging ones for the rest. On a new material, changing the Type or Source to one the picked category doesn't take empties the Category.

- **Source** (ingredients): Purchased, Made or Linked. Only chosen for a new material.
- **Base Unit** (packaging): the unit the material is counted in. It can't be changed once the material has alternative units, prices, or packaging lines that use it.
- **Weight Loss %** (made ingredients): how much of the batch's weight is lost while making it. A new made ingredient starts at 3%.

### Cost Setup tab

What this tab shows depends on the material's source.

#### Purchased materials: Purchase Cost Setup

Enter one row per brand you buy:

| Column | What to enter |
|---|---|
| **Brand** | Optional. The brand or supplier. |
| **Purchase Unit** | The unit you buy it in: the base unit or an alternative unit. Picking a unit the material doesn't have yet adds it as an alternative unit. If you then make that unit a packaging material's base unit, the price moves to the base unit and the added alternative unit goes. |
| **Qty per Purchase Unit** | How many base units one purchase unit holds. It's filled in for saved and standard units. |
| **Purchase Price** | What one purchase unit costs. |
| **Unit Price** | Read-only. What one base unit costs at this price: the Purchase Price divided by the Qty per Purchase Unit. A case of 600 at 900.00 is 1.5000 a piece. Use it to compare brands bought in different units. |
| **Buffer %** | Read-only. How far the buffered cost is above this brand's price. |

The material's **current cost** comes from the **dearest** brand: its price divided by the base units in its purchase unit. For example, a 25 kg bag at 1,250.00 costs 1,250 ÷ 25,000 = 0.05 per gram.

<a id="basis"></a>**Basis**, under the brand table beside **Remove**, picks another brand to cost the material at instead: say you nearly always buy the cheaper Queen flour, and only buy Sampaguita when Queen runs out. Pick **Queen** and the material is costed at Queen's price; leave it on **Dearest brand**, as it starts, to cost it at the most you pay. If the brand is priced in more than one unit, its dearest per base unit is used. If you remove every price of the brand you picked, the dearest brand is used until you price it again or pick another (the picker says *no price: the dearest is used*). The cost buffer, the texts under it, **Re-apply Target**, Price History, and every recipe and product that uses the material all follow the brand it's costed at once you Save.

<a id="cost-buffer"></a>**Cost buffer.** Under the brand table are **Cost Buffer %**, **Buffered Cost** and **Per**:

- A new material's **Cost Buffer %** starts at the default in Settings → Costing Defaults, 10% unless you change it there. Set it to 0 for no buffer.
- Typing the **Cost Buffer %** works out the **Buffered Cost** as the brand it's costed at (see Basis) plus that percentage.
- Typing the **Buffered Cost** works out the percentage.
- **Per** chooses the unit the buffered cost is shown and typed in, such as per gram or per bag. It's always stored per base unit, so choosing another unit there isn't a change to save: **Save** doesn't light up for it, though **Undo** takes it back. A material opens on the unit of the brand it's costed at.

The buffered cost stays **fixed** when brand prices change. The percentage then follows, showing how far the fixed amount now sits above the price it's costed at. If that brand goes above the buffered cost, a warning says so. **To put the buffer back on top of a new price, type the Cost Buffer % again**, then Save.

The app remembers the Cost Buffer % you last typed as the material's **target**. While prices have moved the buffer away from it by a tenth of a point or more, **Target 10%** shows under the buffer with a **Re-apply Target** button, which types it again for you; then Save. To do this for many materials at once, see [Cost Buffers](#cost-buffers) in Reports. A material saved before targets were kept takes the buffer it was last saved with as its target.

On a material's first price, Save works out the buffered cost from the percentage for you.

Every Save that changes a purchased material's cost adds an entry to its **Price History**.

#### Made ingredients: Recipe

A made ingredient is made from a sub-assembly, the recipe kept on the Sub-Assemblies screen, with extras of its own on top.

- **Made from**: pick the sub-assembly. Every recipe on the Sub-Assemblies screen is offered. A packaging set isn't, and nor is a product's own section: move it to the Sub-Assemblies screen first. You need to pick one before a new made ingredient can be saved.
- Once it's picked, it shows as a badge, such as **Butter Icing Base, from Sub-Assemblies**, so it can't be changed by accident. Click the badge to open that sub-assembly on the Sub-Assemblies screen. That's where its lines are changed. When you save it there, the made ingredient's cost follows, and so does every product that uses it.
- To make it from another sub-assembly, click the **Edit** button (the pencil) beside the badge. The picker comes back. Pick the other sub-assembly, or click the **✕** beside the picker to keep the one you had. Save as usual, or Undo to put the old one back.
- The table under the picker shows the recipe's lines as they're saved: **Ingredient**, **Grams**, **Cost/g** (its buffered cost per gram) and **Cost**. You can't type into it here.
- **Add-Ins** are this made ingredient's own extra ingredients on top of the recipe, such as a colouring or a flavouring. Type them like any recipe lines, in grams. An add-in can't be an ingredient the recipe already has, and can't be the made ingredient itself. Add-ins go into the made ingredient only: the sub-assembly itself stays as it is, and so do the products that link it. **Remove Add-In** removes the selected one.

Several made ingredients can be made from the same sub-assembly, each with its own add-ins and weight loss.

The header's **Total Weight** is the recipe's grams and the add-ins', less the **Weight Loss %**. The **Total Cost** is the sum of both tables' Cost columns. Its buffered cost per gram is Total Cost ÷ Total Weight.

A recipe can also become a made ingredient from the other screens: a product's own filling or topping with **Move to In-House** on the Products screen (see *The product's own sections*), and a sub-assembly with **Make In-House Ingredient…** on the Sub-Assemblies screen's list (see *Make In-House Ingredient* there).

Made ingredients can be built from other made ingredients to any depth.
 For example, a caramel can be made from a recipe with a syrup in it, and the syrup from a recipe with sugar in it. The app refuses a recipe or an add-in that would contain the made ingredient itself anywhere down the chain.

A made ingredient has no buffer of its own, because its components are already buffered.

#### Linked ingredients: Linked Cost

Use this for parts of another ingredient, such as egg white and egg yolk from a whole egg:

- **Linked To**: the ingredient this one is a part of.
- **Weight Share %**: how much of that ingredient's weight this part is. Egg white is about 70% of an egg.
- **Cost Share %**: how much of that ingredient's cost this part carries. Egg white might carry 30%.
- **Cost Buffer %**: read-only. It's inherited from the linked-to ingredient, so change it there.

Cost per gram = linked-to ingredient's cost per gram × Cost Share % ÷ Weight Share %.

The parts of one ingredient can't add up to more than 100% of its weight or 100% of its cost. The card lists the ingredient's other parts so you can check.

### Alternative Units tab

Add as many alternative units as you need. Pick the **Alternative Unit** and type its **Multiplier**, which is how many base units it holds. The multiplier of a standard unit is filled in and can't be changed.

The **Unit Conversion Overview** beside the table shows how each unit converts to the base unit.

Correcting a multiplier reprices any brand bought in that unit.

### Usage Example tab

This tab is a calculator. Type a **Use Quantity**, pick a unit and press **Calculate**. It shows the quantity in the base unit, the cost per base unit, the calculated cost, the buffer, and the buffered cost.

### Dependents tab

This tab lists every product the material ends up in as tiles, including products that use it indirectly, such as through a made ingredient, a linked part or a packaging set. An ingredient's list includes every composite product holding a product that uses it. A packaging material's list includes every composite product that holds such a product and has **Include each product's packaging** ticked. Click a tile to open that product.

Under each product's name is what the material makes up of that product's **Material Cost** (its ingredients and packaging, at buffered costs), such as *38% of materials*. It counts the material however it gets into the product, through a made ingredient or a linked part too, so a price rise in a material with a large share moves the product's cost the most. The figures take a moment to appear the first time the tab shows.

### Price History tab

This tab is shown for saved purchased materials only. It's a log of every price the material has been costed at: **Date**, **Brand**, **Unit**, **Price** and **Cost / Base Unit**.

**Delete Price** removes a log entry only. It doesn't change the material's cost.

A Save adds an entry only when it changes the material's cost. Saving again without changing a price adds nothing.

---

## 6. Sub-Assemblies

The Sub-Assemblies screen keeps the sub-assemblies that products **link**: shared doughs, fillings, toppings and packaging sets.

Sections that belong to a single product, in a product-owned role, aren't listed here. They're edited in that product's recipe instead.

### Tabs

The list is on the left. To its right, **each sub-assembly you open has a tab of its own**, along the top, as in Visual Studio Code, so you can have several open at once and go between them. Each keeps what you've typed into it, and its **Undo**, while another shows, and while you're on another screen.

- **A click on a sub-assembly in the list** opens it in the **preview tab**, whose name is in *italics*. The next one you click takes the preview tab's place, so looking through the list doesn't fill the strip with tabs. A sub-assembly that's open already just shows its own tab.
- **To keep a tab open**, double-click the sub-assembly in the list, or double-click its tab. A tab you type into is kept too: the next click in the list opens in a new preview tab beside it.
- **New Sub-Assembly** starts a blank new sub-assembly in the preview tab, so a tab with changes stays as it is. The screen opens on a blank new one, in the preview tab, as it always has.
- **A dot (●) on a tab** means it has changes you haven't saved. Its **✕** comes back when you point at it.
- **The tab's ✕ closes it.** If it has changes you haven't saved, it shows and asks **Save the changes to …?**: **Yes** saves them and closes the tab, **No** throws them away and closes it, and **Cancel** keeps the tab open. Closing the tab showing shows the one before it.
- **Back** and **Forward** go through the tabs you've shown, like a web browser. Going back to a sub-assembly whose tab you've closed opens it again, in the preview tab.
- **Delete** closes the deleted sub-assembly's tab.
- With every tab closed, the screen says so, with **New** beside it. Click a sub-assembly in the list to open it.

### The list

The list has a folder for each role, in the order set in Settings → Roles, with the sub-assemblies in that role inside it, by name. Each folder shows how many it holds. When a product saves a new section, it's saved here under a name made from the product and the role, such as *Cheese Roll Dough*. If the section has its own name, that's added after them, as in *Cheese Roll Filling: Ube*.

Right-click menus:

- **A sub-assembly**: **Move to** (another role), **Rename** (F2), **Make In-House Ingredient…** and **Delete** (Del). A packaging set has no **Move to** or **Make In-House Ingredient…**: it always stays in the Packaging role.
- **A role's folder**: **New Sub-Assembly Here** (**New Packaging Set Here** on the Packaging role), **New Folder**, **Rename Role** (F2), **Delete Role** (Del), **Expand All** and **Collapse All**. You can only delete a role when no sub-assembly here is in it. Otherwise the menu shows how many it holds. The Packaging role can't be deleted.
- **The empty space**: **New Sub-Assembly**, **New Folder**, **Expand All** and **Collapse All**.

The same commands are buttons under the search box. Each button shows only its icon; point at it to see its name.

Dragging sub-assemblies onto another role's folder changes their role straight away, and every product that links them follows. A recipe can't go into the Packaging role, and a packaging set can't leave it. A move is refused, with a message saying why, when a product that links the sub-assembly would end up with two sections in that role it can't tell apart, or when a composite product links it and the new role isn't product-owned, or when a product has it on another section's lines and the new role can't go into another section.

**Names are typed straight into the list**, as on the Products screen. **Rename** (or F2, or clicking the sub-assembly you've already picked once more, slowly: a double-click doesn't rename) turns its name into a box: press **Enter**, or click away, and the new name is saved straight away, as a move is. If the sub-assembly is open in a tab, its name or role there changes too, and anything else you've changed stays to be saved. **Esc** leaves it as it was. **Rename Role** does the same for a role's folder.

**New Folder** adds a *New folder* row after the roles: type the role's name and press **Enter** to add it. **New Sub-Assembly** and **New … Here** add a row for the new sub-assembly in its role's folder: type its name and press **Enter**. **New Sub-Assembly** (under the search box, or in the empty space's menu) goes in the role of the highlighted row in the list: that role's folder, or that sub-assembly's role, so with the Packaging role or a packaging set highlighted it starts a packaging set. With nothing highlighted, it goes in the first role. The name goes into the new sub-assembly's tab, but **it isn't saved yet**: add its lines and press **Save**. **Esc**, or clicking away before you've typed a name, adds nothing: the tab stays on the new sub-assembly.

A new role from the list goes after the others and isn't product-owned. Change its place and **Product-owned** in Settings → Roles.

#### Make In-House Ingredient

**Make In-House Ingredient…** adds a made ingredient on the Raw Materials screen that's made from this recipe, so other recipes can use it as one ingredient. It asks for the ingredient's **Name**, which starts as the sub-assembly's name, and its **Category**, one of the Made categories, starting on **In-House** when there is one. If there's no Made category yet, add one on the Raw Materials screen first, with **New Made Category…**. The name can't be one another raw material already has. Press **OK** and it's saved straight away:

- The new made ingredient is made from this sub-assembly, with no add-ins and a **Weight Loss %** of 0, so it costs per gram what the recipe does.
- The sub-assembly stays here exactly as it is, and you still change its lines here. The products that link it don't change.
- If the recipe really does lose weight while it's made, set its Weight Loss % on the Raw Materials screen afterwards. Every product that uses the made ingredient then follows.

If the sub-assembly is open in a tab with changes you haven't saved, save or undo them first: the made ingredient is made from it as it's saved. To remove the made ingredient again, delete it on the Raw Materials screen.


### The form

Each tab holds the sub-assembly's form, which has two cards. The header at the top holds **Copy**, **New Product**, **Save**, **Undo**, **Redo**, **Print** and **Delete**, as icons: point at one to see its name. The sub-assembly's name is on its tab, and **New Sub-Assembly** is under the list's search box. Then:

- **Batch Weight**, **Batch Cost** and **Cost per g**, worked out from the lines as you type.
- **Name** and **Role**.

The card under it has two tabs along its bottom. Each scrolls while the header stays put:

- **Recipe**: the **Lines**, with **Ingredient**, **Grams**, the Baker's % or Absolute %, **Cost/g**, **Cost** and **Base** (tick it for baker's percentages).
- **Dependents**: a tile for each product that links this sub-assembly. Click one to open it.

Each sub-assembly's tab stays on the card tab you left it on (Recipe or Dependents), when you show another sub-assembly's and when you go to another screen and come back.

**Scale**, under the lines, resizes the recipe. Pick **the whole batch** or **the base (ticked lines)**, type the grams it should weigh, and press **Scale**: every line is multiplied by the same amount, so each keeps its percentage. For example, a 1,200 g batch scaled to 2,400 g doubles every line, and a dough whose ticked flour is 1,000 g, scaled on the base to 500 g, halves. It's one step for **Undo**, and **Save** keeps it. Scaling the base needs at least one line's **Base** ticked. It isn't offered for a packaging set.

**Print** shows the sub-assembly on its own in the Print window, as the form shows it: its name and role, the base and whole formula, each line's grams, percentage, cost per gram and cost, and the batch's total. A packaging set prints its materials, each set's quantity and cost. Changes you haven't saved are in it, marked **Draft**. **Print** there prints it, **Export to PDF** saves it as a PDF file, and its **✕** or **Esc** closes it, leaving the sub-assembly as it was, unsaved changes and all. It's laid out on the paper in [Settings → Printing](#printing), with the shop's heading.

**Copy** turns the tab into a new, unsaved sub-assembly with the same role and lines, named "… Copy". Rename it if you like, then press Save. The original stays as it was last saved.

**New Product** starts a new product from the sub-assembly picked in the list. The Products screen opens with a new, unsaved product named after the sub-assembly, and its recipe already **links** the sub-assembly. Type the piece size (and rename the product if you like), then press Save there. New Product isn't offered for a packaging set. If the sub-assembly has changes you haven't saved, save or undo them first, because the product links the sub-assembly as it's saved.

Changing a sub-assembly here changes every product that links it, and every made ingredient made from it. When saving new lines would change what any of those products cost, **Save** asks first and lists each one's base cost now and after, for one sellable unit, such as *Pandesal x 8: 2.58 → 2.32 (-0.26)*, biggest change first. **No** leaves the changes unsaved in the tab. When made ingredients are made from it, the question says they change too. A sub-assembly that a product links, or that a made ingredient is made from, **can't be deleted**. The message names them.

The **Dependents** tab shows a tile for each product that links the sub-assembly, then one for each made ingredient made from it. Click a product's tile to open it on the Products screen, and a made ingredient's to open it on the Raw Materials screen. Saving lines here refuses a line that's a made ingredient made from this sub-assembly, directly or further down, because that ingredient would contain itself.

### Packaging sets

Pick the **Packaging** role to make a packaging set. The lines table then holds packaging materials instead: **Material**, **Qty**, **Unit**, **Unit Cost** and **Cost per Set**, with a **Set Cost** total.

A product whose packaging links a set takes one whole set per sellable unit. For example, *Ensaymada Box of 8* can link a set holding 8 bags and 1 box.

A sub-assembly can't be switched between a recipe role and the Packaging role once it's saved.

---

## 7. Products

The Products screen has the product list on the left, and to its right a tab for each product you open, with its header, its details, and four tabs of its own along the bottom: **Recipe**, **Costing**, **Clone** and **History**.

### Tabs

**Each product you open has a tab of its own**, along the top, as on the Raw Materials and Sub-Assemblies screens, so you can have several open at once and go between them. Each keeps what you've typed into it, its **Undo**, the tab it was on along the bottom, and **Unbuffered**, while another shows, and while you're on another screen.

- **A click on a product in the list** opens it in the **preview tab**, whose name is in *italics*. The next one you click takes the preview tab's place, so looking through the list doesn't fill the strip with tabs. A product that's open already just shows its own tab.
- **To keep a tab open**, double-click the product in the list, or double-click its tab. A tab you type into is kept too: the next click in the list opens in a new preview tab beside it.
- **New Product** and **New Composite Product** start the new product in the preview tab, so a tab with changes stays as it is. So does Sub-Assemblies' **New Product**. The screen opens on a blank new product, in the preview tab, as it always has.
- **Clone** and **Create Variant** open the new product in a tab of its own, beside the one it came from.
- **A dot (●) on a tab** means it has changes you haven't saved. Its **✕** comes back when you point at it.
- **The tab's ✕ closes it.** If it has changes you haven't saved, it shows and asks **Save the changes to …?**: **Yes** saves them and closes the tab, **No** throws them away and closes it, and **Cancel** keeps the tab open. Closing the tab showing shows the one before it.
- **Back** and **Forward** go through the tabs you've shown, like a web browser. Going back to a product whose tab you've closed opens it again, in the preview tab.
- **Delete** closes the deleted product's tab. **Restore** opens the product it brings back in the preview tab.
- With every tab closed, the screen says so, with **New** beside it. Click a product in the list to open it.
- **Print**, and the Vim keys' `:print`, print the product of the tab showing.

### The list

The top-level folders are **Production** and **Innovation**. Inside them you make your own folders, as in File Explorer, and put products in them however suits you: by kind, by season, by who makes them. A folder can hold other folders, as deep as you like, and products can also sit loose at the top of a stage. In each folder, its folders come first, then its products, each by name. Each folder shows how many products it holds, counting the folders inside it.

**A folder isn't a category.** A product's category is set on its form, and it still sets the product's Wastage % and prints on its page, whichever folder the product is in. To find a category's products, pick the category in the **category filter**, the box on a row of its own under the buttons below the search box. The list then shows only that category's products (or, with **Uncategorized**, those without one), in their folders, and only the folders that hold one. **All categories** shows everything again. The filter and the search work together: with both, a product has to match both.

When the app was first updated to have its own folders, it made a folder in each stage for each category that had products there, and put those products in it, so the list looked as it did. From then on you can rename, move or delete those folders like any other: changing a folder changes no product's category.

**Drag** products onto a folder, or onto a product in it, to move them there. Dropped on **Production** or **Innovation** itself, they go loose at the top of that stage. Moving into the other stage changes the product's stage. You can also drag **one folder** at a time onto another folder or a stage: it moves with everything in it, and moved into the other stage, every product in it changes stage too. A folder can't be dropped inside itself or a folder of its own, and a drop that would leave things where they are does nothing. A notice under the list says what moved, with **Undo**. One move happens at a time: until a move has finished saving, the menus' moves and **Undo** are greyed, and a drag or a paste does nothing.

**Cut and paste** move them too, as in File Explorer, without dragging: pick one or more products (Ctrl+click or Shift+click for several), or one folder, and press **Ctrl+X** (or right-click → **Cut**). They fade, and nothing moves yet. Then right-click the folder they should go in, or Production or Innovation, or a product in that folder, and choose **Paste**, or pick it and press **Ctrl+V**. They move there straight away, as a drag would, with the same notice and **Undo**. **Esc** in the list forgets what was cut, which stays where it is. Paste is greyed where nothing would move, such as the folder they're in already, or a folder inside the one cut.

Below them is the **Deleted** folder 🗑, where deleted products wait, by name, until you restore them or delete them permanently. Clicking a deleted product doesn't open it. It can't be dragged, and nothing can be dropped on the folder. A deleted product keeps its folder, and goes back to it when you restore it.

Right-click menus:

- **A product**:
  - **Promote to Production** or **Send Back to Innovation**: puts the product in the other stage, in the folder with the same path there (Innovation › Breads › Holiday goes to Production › Breads › Holiday). Any of those folders that isn't there yet is made. **Undo** on the notice moves the product back, and takes away any folder the move made, if nothing else has gone in it since.
  - **Move to**: any stage, then the top of the stage or any folder in it. A folder that holds others opens a further menu, starting with the folder itself. The place the product is in already is greyed.
  - **Cut** (Ctrl+X) and **Paste** (Ctrl+V): see Cut and paste above. Paste on a product moves what was cut into that product's folder.
  - **Apply Costing Defaults…**: sets the product's Labor, Overhead, Admin and Markup percentages to Settings' Costing Defaults, each in place of which its category sets its own (Settings → Product Categories), once you confirm. The question names the percentages; when the products' categories come to different ones, it has a line for each, such as *Cakes: labor 50%, …*. Each product it changes gets a version noted "Costing defaults applied". If a tab holds one of the products with unsaved changes, it shows, and you're asked to save or discard them first.
  - **Print**: the product as saved, in the Print window.
  - **Rename** (F2)
  - **Clone…** and **Create Variant…** (both ask for the new product's name)
  - **Delete** (Del)

  With several products picked, the menu offers to promote or move them all, to apply the costing defaults to them all, or to print them all (**Print 3 Products**).
- **A stage's folder**: **New Product Here**, **New Composite Product Here**, **New Folder**, **Paste** (Ctrl+V; to the top of the stage), **Print Stage**, **Expand All** and **Collapse All**.
- **One of your folders**: **New Product Here**, **New Composite Product Here**, **New Folder** (a folder inside it), **Promote All to Production** (in Innovation, for everything in it and its folders; with a search or filter on, what it shows), **Move to** (any stage, then the top of the stage or any folder but itself and those inside it), **Cut** (Ctrl+X) and **Paste** (Ctrl+V), **Print Folder** (its products and those of every folder inside it), **Rename** (F2), **Delete** (Del), **Expand All** and **Collapse All**.
  - **Delete** deletes the folder, never the products in it. It asks first, saying how many products and folders it holds: they all move up into the folder it was in, or to the top of the stage. If a folder inside it has the same name as one already there, the delete is refused, and the message says which: rename one of them first.
- **The empty space**: **New Product**, **New Composite Product**, **New Folder**, **Print All Products**, **Expand All** and **Collapse All**. These are also buttons under the search box. Each button shows only its icon; point at it to see its name.
- **A product in the Deleted folder**: **Restore** and **Delete Permanently** (Del).

**Names are typed straight into the list**, as in File Explorer:

- **Rename** (or F2, or clicking the product you've already picked once more, slowly: a double-click doesn't rename) turns the product's name into a box with the name selected. Type the new name and press **Enter**, or click away, and it's saved straight away. **Esc** leaves the name as it was. **Rename** on a folder (or F2 with the folder picked) does the same for the folder.
- **New Folder** adds a *New folder* row, its name selected, inside the folder or stage you right-clicked. From the button under the search box, it goes inside the folder you've picked, or the folder of the product you've picked, or at the top of Production. Type its name and press **Enter**: the folder is saved there and picked. **Esc**, an empty name, or clicking away before you've typed one, adds nothing. Two folders side by side can't have the same name, whatever the capitals; the same name in another folder is fine.
- **New Product Here**, **New Composite Product Here**, and **New Product** and **New Composite Product** under the search box, add a *New product* row, its name selected, to the folder the product goes in. Type its name and press **Enter**: the product is saved there and then, with Settings' costing defaults, and opens on the right for you to fill in its recipe, packaging and price. **Esc**, or clicking away before you've typed a name (on the form, or on another product), adds nothing, so no product called *New product* is saved by accident. Clicking the form leaves it on the new product, for you to fill in, name and **Save**. **New Product** and **New Composite Product** under the search box (or in the empty space's menu) put it where the highlighted row in the list is: in that folder (**Production** or **Innovation** itself: at the top of that stage), or in the folder of that product. With nothing highlighted, or the **Deleted** folder or a deleted product, they put it in the stage set in Settings → Costing Defaults, Production unless you change it, at the top of the stage. Until the product is saved, a tag in the header says where it will go, such as **Goes in Production** or **Goes in Production › Rolls**. A product started from a folder has no category until you pick one on its form. New, New Folder and their "Here" forms clear the search and the category filter first, so the new row shows.
- **A composite product can't be saved until it holds two products**, so its name goes into the form instead. Its row keeps the name until you press **Save**.

If the name is already taken, a message says so and the box stays open with what you typed, to change it.

**Clone…** and **Create Variant…** put the new product in Innovation, in the folder with the same path as the source's (made if it isn't there).

A ⚠ after a product's name means its **Current Price** earns less than the **Markup Metric** on its Costing tab, and less than the [Warn Below %](#costing-defaults) if one is set. A section mark (the icon of a recipe section) means one of its recipe sections has neither a **Weight per Piece** nor a **Yield**: that section adds nothing to the product's cost until one is typed, so its costs and suggested price leave it out. The marks are worked out from the products as saved, a moment after the list loads, so a product you've just saved shows its new marks the next time the Products tab opens.

### The header

The header has **Save**, **Undo**, **Redo**, **Revert**, **Print**, **Export to PDF**, **Unbuffered** and **Delete**, as icons: point at one to see its name. The product's name is on its tab, and **New Product** and **New Composite Product** are under the list's search box. Below them are these figures, all **per sellable unit**:

- **Material Cost**: sub-assemblies plus packaging. For a composite product, its products' recipe cost and its own sub-assemblies, plus its own packaging, and its products' packaging too when it includes theirs.
- **Base Cost**: materials plus wastage, labour, overhead and administrative.
- **Current Price**: what the shop charges now.
- **Markup %**: what the current price actually earns on the base cost, with VAT taken out. Compare it with the markup you set on the Costing tab. It shows in amber when it's less than that markup, and in red when the price doesn't even cover the base cost.
- **Last Updated**.

Below the header are the fields you fill in:

- **Product Name**.
- **Category**: pick one, or type a new name to add it.
- **Quantity Required**: how many finished pieces make one sellable unit. A composite product doesn't have one.
- **Current Price**: the price the shop charges now, per sellable unit, VAT included. It's for comparison only and doesn't change the computed selling price.

**Unbuffered** switches every cost in the product's tab to the materials' true costs, without their cost buffers. That covers the figures above, the Cost/g and cost columns, the batch cost and cost per piece, packaging, the Costing tab, the recipe cost of a composite product's products, and the cost column in the packaging pickers. While it's on, the button's name reads **Buffered** and an **Unbuffered costs** badge shows at the left of the header's buttons. Press **Buffered** to go back to buffered costs.

- It only changes what you see. Nothing is saved, **Save** and **Undo** aren't affected, and **Reports** still use buffered costs.
- It's the tab's own: it stays on when the tab shows another product, until you switch it off, close the window or reload the page. A new tab starts on buffered costs, and so does the app.
- **Print** uses whichever costs the tab shows (see [Printing](#8-printing-recipes-and-costings)).

A **Priced below cost** badge in the header means prices have risen past the buffered cost of a purchased material in the recipe or packaging. Every cost here is at buffered costs, so the product is priced under what that material costs. Point at the badge to see which materials. Re-apply their target buffers on Raw Materials, or in Reports' **Cost Buffers**, to carry their new prices in. A composite product's own products aren't checked here; each shows its badge when you open it.

A **No piece size** badge in the header means a section of the recipe has neither a **Weight per Piece** nor a **Yield**, so it adds nothing to the product's cost. Point at the badge to see which sections. Open each one's tab and type either figure; the badge goes once every section has one and you've moved off its tab, as the header's costs take the section in then.

Deleting a product asks first, then moves it to the **Deleted** folder at the bottom of the list. Nothing of it is lost there: its recipe, packaging, costing, stage, folder and category are all kept. Until you restore it, no picker, report, print or Price Simulator includes it, and TinapayanBMS's copy leaves it out.

- **Restore** (right-click it in the Deleted folder) puts it back in its folder and opens it in the preview tab. If its folder was deleted in the meantime, it comes back where that folder's products went. If its category was deleted, it comes back uncategorized.
- **Delete Permanently** asks first and can't be undone. The product's own sub-assemblies are deleted with it. Its linked sub-assemblies stay on the Sub-Assemblies screen. Deleting a composite product leaves the products it holds as they are.
- A deleted product **still holds everything it uses**, so it can come back whole. A sub-assembly it links, an ingredient in its recipe, or a product it holds or whose packaging it links can't be deleted until you delete the deleted product permanently. **Dependents** tiles and refusal messages name it with "(deleted)" after its name. Clicking its tile picks it in the Deleted folder.
- A deleted product keeps its name, so no other product can take that name until it's deleted permanently.
- A product can't be deleted, to the Deleted folder or permanently, while another product's packaging links it, or while a composite product holds it. That includes a deleted product.

### Recipe tab: Recipe Structure

The recipe has a tab for every section: each sub-assembly, named by its role, then **Packaging**, which every product has. A new product's recipe has only the Packaging tab, and opens on it.

- **Add Sub-Assembly** opens a menu with three choices:
  - **New sub-assembly**: type its lines in here.
  - **Link an existing one**: use a sub-assembly from the Sub-Assemblies screen. It follows every change made to it where it's kept.
  - **Copy an existing one**: start from an existing sub-assembly's lines and role, including another product's own. The copy is a new sub-assembly.
- **Delete Sub-Assembly** takes the section showing out of the recipe when you Save. A linked sub-assembly stays on the Sub-Assemblies screen. It isn't offered on the Packaging tab.

Add Sub-Assembly is only for recipe sections. Packaging is added on the Packaging tab.

#### A section's form

Each section's tab shows its form:

- **Role**, and an optional **Name**. The name tells apart two sections in the same role, such as *Filling: Ube* and *Filling: Cheese*, here and wherever the saved product shows: Reports, printouts and the Price Simulator.
- **Existing Sub-Assembly** picker, when you're linking or copying.
- **The lines table**: **Ingredient**, **Grams**, the Baker's % or Absolute %, **Cost/g**, **Cost** and **Base**. Under it are **Insert Line**, **Remove Line** and the section's cost per piece. The Ingredient list also has the recipe's other sections, such as *Sponge (section)* (see below).
- **Batch parameters**:
  - **Weight/piece (g)** and **Yield (pcs)**: type either one.
  - **Total weight**: the batch, including add-ins and any section on its lines, which the pieces share out, less what other sections use of it
- **Cost/piece** and **Batch cost** tiles.

You don't need a piece size to add a section or to save it. Its cost per piece reads 0 until you enter one. A new or copied section in a product-owned role already has one: a Yield of 1 (see [Piece size](#piece-size-weight-per-piece-and-yield)).

A section's changes go into the recipe when you leave its tab, or when you press Save. **Undo** after leaving the tab takes the change back in one step and shows that section's tab again, as it was before you typed.

#### A section on another section's lines

Some sections go into another: a sponge or a tangzhong into the dough, say. On the dough's tab, pick **Sponge (section)** in a line's Ingredient list, just above the ingredients, and type its grams, or its Baker's %, as for any line:

- The list only has sections whose role has **Into other sections** ticked in Settings → Roles (see [Roles](#roles)), so a Filling or a Topping isn't offered unless you tick it for that role. A role ticked while a product is open shows up in the list when you come back to Products. A line that already holds a section keeps showing it even if its role is unticked behind the app's back, but **Save** then says what's wrong.

- The line costs its grams at the sponge's cost per gram, and the dough's **Total weight**, **Batch cost** and pieces take it in, as any line's.
- Change the sponge on its own tab, and the dough's line follows its cost.
- The sponge is then **locked** as the dough's ingredient. Its lines and add-ins can't be changed, it has no pieces of its own whatever piece size it had, and its tab says *Locked as an ingredient*. The dough counts its cost, and it isn't flagged for having no piece size.
- Press **Unlock** on the sponge's tab to change its lines, or to give it pieces of what the dough leaves: its **Yield** then shares out the rest. With a Yield and nothing left, it's flagged for having no piece size. **Lock** makes it only the dough's ingredient again. Each is undone like any change, and Save keeps it. Taken off the dough's lines and saved, it's no longer locked; used again after that, it starts locked.
- The sponge keeps its own tab and lines, and can be linked or the product's own, like any section. A section can be on several sections' lines, and one on another's that's on a third's, but never back round on its own, so the list only offers the sections that can go in. A section is never part of the Base.
- On a linked section, whose lines are kept on Sub-Assemblies, put the sponge in its **Add-Ins** instead.
- A section another uses can't be deleted from the recipe: take it off the other's lines first. Deleting the dough takes its line of the sponge with it.

Before 24 November 2026 a section could instead go whole into another, with a **Goes into** picker. The update took that setting away: put the section on the other's lines with its whole batch's grams, and save.

Printouts show the sponge among the dough's lines too. The History tab lists a section's line added, changed or taken off, as any line's.

#### Linked sections

A linked section has a badge saying where it's from. Click the badge to open the sub-assembly on its own screen. The pencil beside the badge lets you **link another sub-assembly in its place**, keeping this product's piece size and add-ins.

- **Editing the lines** of a linked section changes the sub-assembly itself. Save asks first when other products also use it, because their recipes and costs change too. If its lines have been saved from anywhere else since you opened the product (the Sub-Assemblies screen, another product's tab, another PC), Save is refused rather than write over them, and what you typed stays. A new name alone doesn't refuse it.
- **The Add-Ins table** under the linked recipe's lines holds this product's own extras on top of the linked recipe, such as a colouring, a flavouring or an inclusion. Add-ins go into the batch but don't change the shared sub-assembly. An ingredient the linked recipe already has can't be added as an add-in.
- **The add-ins badge**, over the top right corner of the linked recipe's lines, level with the badge saying where the section is from, tells you whether the product has add-ins: it reads **No add-ins**, or the count, such as **2 add-ins**. When there are some, click the badge (or Tab to it and press Enter) to jump down to the Add-Ins table. The **Go to top** badge over the Add-Ins table's top right corner takes you back up to the linked recipe's lines.
- **Copy** makes a copy for this product alone, with the same lines, base ticks and typed percentages. Save keeps the copy named after this product and uses it instead. In a product-owned role the copy is the product's own. Otherwise it's saved on the Sub-Assemblies screen and linked.
- **Unlink** makes the section this product's own copy, whatever its role. The original on the Sub-Assemblies screen stays as it is.

#### The product's own sections

- **Move to Sub-Assemblies** is the reverse of Unlink. Save puts the section on the Sub-Assemblies screen, keeping its name and lines, and this product links it there. Other products can then link it too.
- **Move to In-House** makes a made ingredient from the section, so other recipes can use it as one ingredient, the way a topping uses Butter Icing. It asks for the ingredient's **Name**, which starts as the name the section is saved under (such as *Cheese Roll Filling*), and its **Category**, one of the Made categories, starting on **In-House** when there is one. If there's no Made category yet, add one on the Raw Materials screen first, with **New Made Category…**. The name can't be one another raw material already has.

  Until you save, a **Moves to Sub-Assemblies and In-House on Save, as …** badge shows and you can still change the lines. Undo or Revert keeps the section this product's own. When you save:

  - The section moves to the Sub-Assemblies screen, as **Move to Sub-Assemblies** does, keeping its name, lines and baker's percentages. This product links it there at the same piece size, so it costs what it did. From then on its lines are changed on the Sub-Assemblies screen, or through this product's linked section.
  - The new made ingredient goes in the category you picked, made from that sub-assembly, with no add-ins and a **Weight Loss %** of 0.
  - If the filling or topping really does lose weight while it's made, set its Weight Loss % on the Raw Materials screen afterwards. Every product that uses the made ingredient then follows.

  Move to In-House isn't offered for a section that's already just one made ingredient.


#### Packaging

The **Packaging** tab is on every product, with two tables, both always there: **Packaging per Piece** and **Bulk Packaging**.

**Packaging per Piece** is what one sellable unit takes, such as its bag and label. Type it straight into the table. Its columns are **Material**, **Qty**, **Unit** (the base unit or one of the material's alternative units), **Unit Cost** and **Cost per Unit**. **Insert Line** puts a blank line in above the selected one, and **Remove Line** takes the selected one out. While the table is empty, a line under it says what to add.

Beside the heading, **Per piece** (or the unit the product is sold in) shows what these lines cost one sellable unit. The bulk packaging's share isn't in it: that's under the Bulk Packaging table. The Costing tab's **Packaging** holds both.

To use packaging that already exists, use the buttons at the top right of the tab:

- **Link** opens a picker of the **packaging sets** on the Sub-Assemblies screen, with its list open and the cursor in it, so you can start typing a name straight away. Pick one, and its lines show in the table in place of the ones there. They can't be changed here: they follow every change made to the set. If there's no packaging set yet, the screen says so.
- **Copy** opens a picker of the packaging sets and every other product, ready to type into in the same way. Pick one, and its lines come in as this product's own, in place of the ones there, to change as you like.
- The **✕** beside the picker closes it without changing anything. A product with no packaging per piece can't be picked: the screen says so, and you can pick another.
- Under linked lines, **Make a Copy** turns them into this product's own lines so you can change them, and **Remove Link** takes the link and its lines out, leaving no packaging per piece.
- **Undo** puts back what the table held before a Link, a Copy or a Remove Link.

**Bulk Packaging**, under the packaging per piece, is for packaging several products share, such as a box that holds 6 and the tape that closes it. It doesn't need any packaging per piece.

- **Products per pack**, beside its heading, is how many products one bulk pack holds: a whole number, 1 or more.
- Its table lists what one pack takes: **Material**, **Qty**, **Unit**, **Unit Cost** and **Cost per Pack**. Type each quantity for the whole pack, such as 1 box and 30 cm of tape. The quantities stay as you type them.
- Under the table, the caption shows the sum: the pack's cost, divided by the products it holds, is what each product carries, as in "Pack cost 12.60 ÷ 6 products = 2.10 per unit". That share joins the **Packaging** figure on the Costing tab. While the table is empty, the caption says what to add instead.
- **Insert Line** puts a blank line in above the selected one, and **Remove Line** takes the selected one out.
- Save refuses bulk lines with no **Products per pack**. Take every line out to have no bulk packaging.
- A material can be both one of the packaging lines and one of the bulk lines, such as tape on each product's bag and on the box.
- **Link**, beside **Products per pack**, uses a packaging set from the Sub-Assemblies screen as what one bulk pack takes. It opens a picker of the packaging sets in its place, as the packaging per piece's Link does. Pick one, and its lines show in the Bulk Packaging table, read-only, and follow every change made to the set. Each of the set's quantities is for one whole pack. **Products per pack** stays this product's own, so one box set can hold 6 of one product and 4 of a bigger one. The badge beside the heading names the set; click it to open the set on the Sub-Assemblies screen.
- Under linked bulk lines, **Make a Copy** turns them into this product's own lines so you can change them, and **Remove Link** takes the link and its lines out, leaving no bulk packaging. **Undo** puts back what the table held before a Link, a Make a Copy or a Remove Link.
- The packaging per piece's **Link**, **Copy**, **Make a Copy**, **Remove Link** and **Move to Sub-Assemblies** leave the bulk packaging as it is. **Clone** copies it, or links the same packaging set; **Create Variant** doesn't, since a variant starts with no packaging. Restoring a version from the History tab brings back the bulk lines it showed as this product's own.
- A packaging set that a product's bulk packaging links can't be deleted from the Sub-Assemblies screen until that product stops linking it.

**Move to Sub-Assemblies**, at the top right of the Packaging tab while the packaging per piece is this product's own lines, works as it does for the product's own sections. Save puts the lines on the Sub-Assemblies screen as a packaging set named after the product, such as *Ensaymada Packaging*, and this product links it there. The cost doesn't change. Until you save, a **Moves to Sub-Assemblies on Save** badge shows and you can still change the lines; Undo or Revert keeps them the product's own. Afterwards other products can link the set too, its lines are changed on the Sub-Assemblies screen, and **Make a Copy** makes them this product's own again.

Packaging quantities are for one sellable unit. They aren't multiplied by Quantity Required. If you change Quantity Required, check the packaging yourself.

#### A composite product's Products tab

A composite product's recipe opens on its **Products** tab. After it come its own sections, such as a Topping, then **Packaging** when it has packaging.

The **Products** table is typed into, like the Packaging table:

- **Product**: type part of a name and pick it. The list leaves out the product itself, and any composite that holds it.
- **Qty**: how many of that product go in, as a whole number. It counts the product's sellable units: pieces for a product sold by the piece, and whole boxes for one like *Ensaymada Box of 8*. To put in loose pieces, pick the product sold by the piece.
- **Recipe Cost**: what one of that product brings, which is its recipe (ingredients) cost for one sellable unit.
- **Packaging Cost**: shown only while **Include each product's packaging** is ticked. It's that product's own packaging for one sellable unit, the **Packaging** figure on its Costing tab.
- **Cost**: Qty × Recipe Cost. While **Include each product's packaging** is ticked, it's Qty × (Recipe Cost + Packaging Cost).

Each product goes on one line. Drag lines into order, and use **Remove Line** to take one out. The figure at the top right is the total of the Cost column, without the composite's own sections.

**Include each product's packaging**, on the same row as Remove Line, is off for a new composite. Tick it when the products go into the composite already in their own packaging, such as an Ensaymada in its bag. The **Packaging Cost** column then appears, and each line's **Cost** adds its packaging in. On the Costing tab, that packaging goes into the composite's **Packaging** row, not into its products' recipe cost, so Labor, Overhead and Admin aren't taken of it. The products listed under **Products (ingredients)** there still show their recipe cost only. The Packaging tab still shows only the composite's own packaging. Ticking or unticking the box is one step for **Undo**, and **Save** keeps it. A printed composite's Products table has the Packaging Cost column too while the box is ticked.

**Add Sub-Assembly** works as it does for any product, but a composite is offered only the **product-owned** roles. Its packaging is on its Packaging tab, as any product's is. A section's piece size is for one whole composite: a Topping with a **Weight/piece** of 100 g puts 100 g on each box. **Delete Sub-Assembly** takes the section showing out.

Save needs at least **two different products** in a composite.

### Costing tab

On the left are the product's four metrics, **Labor Metric**, **Overhead Metric**, **Admin Metric** and **Markup Metric**. A new product's metrics start with the Costing Defaults, or its category's own where it sets them, and change when you pick another category until you type into them. The VAT is the same for every product, so it isn't set here: the summary on the right shows what it adds to the price, and it's changed only in Settings → Costing Defaults.

Each metric has two boxes. The first is its percentage, shown to one decimal with its % sign. The second is what that percentage comes to in pesos for one sellable unit, shown to two decimals. Type in either box and the other one follows. For example, with ₱20.00 of sub-assemblies, typing **5.00** in Labor Metric's peso box sets its percentage to **25.0%**.

- Labor, Overhead and Admin are a percentage of the **sub-assemblies (ingredients) cost**.
- Markup is a percentage of the **base cost**.
- Only the percentage is saved. If the recipe's cost changes later, the percentage stays and the peso amount changes with it.
- A product with nothing in its recipe yet has no cost to take a percentage of, so its peso boxes stay at 0.00. Type the percentage instead.

Under each metric is its share of the price.

On the right, the **Costing Summary** lists each component of the base cost with its amount and its **% of Price**:

- **Sub-assemblies (ingredients)**, with each sub-assembly listed under it. For a composite product this row is **Products (ingredients)**, with each product and its quantity listed under it, such as *Ensaymada × 6*. When the composite has sections of its own, the row is **Products and sub-assemblies (ingredients)** and lists them after the products. Its Labor, Overhead and Admin are a percentage of that row.
- **Wastage**, labelled with the [Wastage %](#wastage) the product is priced with: its category's own, or the shop's if its category doesn't set one. It's set in Settings (Product Categories, or Costing Defaults), not here. Picking another **Category** on the product changes it at once.
- **Packaging**. For a composite product that includes its products' packaging, this row is **Packaging (own and products')**.
- **Labor**, **Overhead** and **Administrative**, each labelled with its percentage
- **Subtotal (base cost)**

Under the subtotal, after a gap and a heading row of its own, the same table puts the price the costing suggests beside the price the shop charges now, so you can compare the two. Its columns line up with the breakdown's: **Suggested Price** under the amounts, **Current Price** under **% of Price**.

- **Suggested Price**: the price the costing works out, for the unit the figures are for (one piece, a unit of 8 pieces when Quantity Required is 8, or one unit of a composite product). Point at the heading to see which, such as *Suggested Selling Price per Unit of 8 Pieces*.
- **Current Price**: the product's **Current Price**, as typed in the header.

Each column shows, for its price:

| Row | Suggested Price | Current Price |
|---|---|---|
| **Markup** | The Markup Metric, then the markup in pesos over the base cost, such as *(30.0%) 30.00* | What the store price earns on the base cost once VAT is taken out, then what that leaves over the base cost in pesos, such as *(20.0%) 20.00* |
| **VAT** | The VAT added on top | The VAT inside the store price |
| **Selling price** | The suggested price | The store price |

For example, with a base cost of ₱100.00, a Markup Metric of 30% and 12% VAT, the suggested price is ₱145.60: ₱30.00 of markup and ₱15.60 of VAT. A store price of ₱134.40 is ₱120.00 before VAT, so it earns ₱20.00 (20%) and holds ₱14.40 of VAT.

The Markup's percentages are in lighter text, so the amounts stand out. The store price's Markup turns **amber** when it earns less than the Markup Metric (and less than the [Warn Below %](#costing-defaults), if one is set), and **red** when it doesn't cover the base cost, as the header's Markup % does. With no Current Price entered, its column shows dashes. When Settings sets a [price rounding](#costing-defaults), a last row shows the suggested price rounded up, such as *Rounded up to the next 0.25*.

Everything updates as you type, before you save. See [How costs are worked out](#12-how-costs-are-worked-out).

**Export to Excel**, at the Costing Summary's top right, saves the product's costing to an Excel workbook, named after the product and today's date (such as *Asado Roll 60g Costing 2026-11-19.xlsx*). It's the product as the screen shows it, changes you haven't saved and **Unbuffered** included, and it has the same checks as **Print**. It has a **Costing** sheet, laid out as the Costing tab is: each part of the breakdown down to the base cost with its share of the price, then, after a blank row and a heading row of their own, the **Suggested Price** under the amounts beside the **Current Price** under **% of Price**, each with its Markup (such as *(30.0%) 30.00*, still a number: the percentage is only how the cell shows it), VAT and selling price; a **Recipe** sheet, each section's ingredients with their grams, percentage, cost per gram and cost; and a **Packaging** sheet when the product has packaging. The figures are real numbers, to add up or chart in Excel.

### Clone tab

The Clone tab is shown for saved products only. Type a **New Product Name**, then choose:

- **Clone**: the new product links the same sub-assemblies from the Sub-Assemblies screen at the same piece sizes, gets its **own copy** of this product's own sub-assemblies, and gets its **own copy of the packaging**.
- **Create Variant**: the new product **links every sub-assembly this product uses** at the same piece sizes: the ones it links from the Sub-Assemblies screen (a dough, sponge or batter) and this product's own (a filling, topping and so on). So it follows every change made to them. It gets **no packaging**, neither linked nor copied. Use this for a smaller or bigger version of the same product, then give it its own packaging.

A section that's this product's own shows in the variant with a badge such as "Ensaymada Big Filling, from Ensaymada Big". Click it to open the product the section belongs to. You can change the section's lines in the variant too: Save writes them to that product's sub-assembly. That counts as a save of that product: its tab shows the new lines, or, if you have unsaved changes on it, refuses its next Save as it does when another PC has saved it. **Make a Copy** gives the variant its own copy instead. Once a variant drops a linked section, it can't link it again.

While a variant links one of this product's own sub-assemblies, this product can't remove that sub-assembly and can't be deleted. Make a copy of it in the variant, or remove it there, first.

The category, Quantity Required, Current Price and costing percentages are copied with both. The new product starts in Innovation, opens in a tab of its own, and you can change its piece sizes. Both copy the product as saved: if it has changes you haven't saved, you're asked about them first. Its History tab starts with one version that says where it came from (see [History tab](#history-tab)).

A composite product's clone or variant is a composite too, holding the same products at the same quantities. A clone copies its own sections and a variant links them, as for any product.

### History tab

The History tab keeps a product's earlier versions, so you can see how its recipe and price changed and go back to an old one. It's shown for saved products only.

**When a version is recorded.** Each **Save** that changes the product's recipe, packaging, costing percentages, Quantity Required or Current Price records a version. A Save that only renames the product, or changes its description or category, doesn't. Nor does a change in what its materials cost: that's what the **Today** columns show. Nor does a change in the shop's VAT or wastage (Settings, or the product's category), but when one has changed between two versions, their list of changes ends with it, such as *VAT % (Settings): 12 → 10*, since it moves the price too. The first time you save a product that has no versions yet, two are recorded: how it was before ("Before version history began") and how it is now.

**Changes made from somewhere else are recorded too**, with a note that says where they came from:

- Saving a sub-assembly on the **Sub-Assemblies** screen records a version of every product it changes ("House Dough changed on Sub-Assemblies"). The green notice names them: "House Dough was saved. A version was recorded for Cheese Roll and Pandesal."
- Saving a product whose changes reach other products records a version of each of those too ("Changed when Cream Loaf Mini was saved"). That happens when you change the lines of a sub-assembly it shares with other products, or packaging another product links.
- A product with no versions yet gets two, as on its own first Save: how it was before, and how it is now.

**A clone or a variant starts with one version**, the product as it was made, noted with where it came from: "Cloned from Almond Cheese Bread, version 4" or "Variant of Almond Cheese Bread, version 4". The version number is there when the product it came from matched its latest version.

**The table** lists the versions, newest first:

| Column | What it shows |
|---|---|
| Version | 1 for the first, then 2, 3 and so on. |
| Saved | When the Save was made. |
| Note | Anything you want to remember about it, such as "Cheaper butter". Click the cell and type; it's saved when you leave the cell, up to 200 characters. |
| Base Cost Then, Suggested Price Then | What the product cost, and the price it worked out, when it was saved. |
| Base Cost Today, Suggested Price Today | The same recipe at what its materials cost now. |
| Current Price | The price you charged then. |

The figures follow the screen's buffered or unbuffered costs, as the Costing tab does.

**Changes.** Pick a version to see what changed in it, one line each, such as "Dough: Bread Flour 500 g → 450 g", "Packaging: Paper Bag added, 1 pc" or "Markup %: 30 → 35". Use **Compare With** to choose what it's compared with:

- **The version before it**: what that Save changed.
- **The product as saved now**: everything that's different between that version and today's recipe.
- **Any other version**, listed by number and date, such as "Version 2, Oct 17, 2026, 8:03 AM": everything that's different between the two. The list always reads from the older one to the newer one, whichever of the two you picked in the table.

**Cost Over Time**, beside the changes, is a chart of each version's **Base Cost**, **Suggested Price** and **Current Price** when it was saved, oldest on the left (V1, V2, and so on). Point at a dot to see its figure. A version with no Current Price leaves a gap in that line. The chart appears once a product has two versions, and follows the screen's buffered or unbuffered costs.

**Print Version** shows the picked version's whole recipe in the **Print** tab: every section with its ingredients, the packaging and the costing, at what its materials cost when it was saved. The title says which version it is ("Almond Cheese Bread (Version 2)"), and the footer says it's from the version history. You can print it from there, or just read it. **Print** in the header does the same while the History tab is showing. Nothing in the form changes.

**Restore** puts the picked version's sections, packaging, products (for a composite), costing percentages, Quantity Required and Current Price back into the form. The product's name, description, category and stage stay as they are. Nothing is saved yet: press **Save** to keep it, or **Undo** to put the form back as it was. Saving a restored version records it as a new version.

- A section that was the product's own comes back with its old lines.
- A section that links a sub-assembly links it again **as it is now**. Restore never changes a sub-assembly that other products may share. If its lines have changed since, the green notice says so.
- If that sub-assembly has become this product's own since (its role was made **Product-owned** in Settings), it comes back as the product's own section, with its lines as they are now, and the green notice says so.
- A linked sub-assembly that has been deleted since comes back as a new section with the old lines.
- Packaging links what it linked, if that still exists. Otherwise the lines it showed come back as the product's own.
- If a material, role or product the version needs has been deleted, Restore refuses and names it, and nothing in the form changes.

A product moved to the Deleted folder keeps its history. **Delete Permanently** deletes the history with it.

---

## 8. Printing recipes and costings

Press **Print** in a product's header to open the **Print** window over the Products screen. It shows the product's recipe and costing on a sheet of paper, as its tab shows them, including changes you haven't saved yet. On the History tab, it shows the version picked there, at its costs then (see [History tab](#history-tab)).

- **Print** (the list at the top left) chooses the products to print. With none ticked it reads **This product** and prints the product of the Products tab showing, as described above. Open it, type part of a name in **Search products** to find a product, and tick it; tick as many as you like, from any folders, or **Select All** for every product the list shows. When the list closes, the pages show the products ticked, as saved, in the Products list's order. Untick them all and it goes back to **This product**. One product ticked prints on its own, with no contents list.
- **Categories** (the list beside it) narrows **Print** to the products of the categories you tick, such as **Breads**; **No category** is the products without one. A product ticked under **Print** that isn't in them is unticked. To print every bread, tick **Breads** here, then **Select All** under **Print**. With no category ticked (**All categories**), **Print** lists every product.
- **Include**: tick **Recipe**, **Costing** or both. Without the costing, the recipe prints with no costs, which is useful for the kitchen. A composite product's recipe prints as its products table, then its own sections. The ticks stay as you leave them for the next time you print on this computer, for your Windows account. To leave out single columns, such as Cost/g, or the figure tiles, see [Settings → Printing](#printing).
- **Sub-assembly total** (beside **Costing**) prints the recipe with its costs but without the costing's breakdown: each ingredient's **Cost/g** and **Cost**, each packaging material's **Unit Cost** and **Cost**, each table's **Total** and each sub-assembly's cost per piece, then one line at the end, **Total sub-assembly cost**, for one piece (or pack). The unit prices print here even when [Settings → Printing](#printing) leaves them out of the full costing. A composite's line names what it holds, such as **Total products cost**. Ticking it ticks **Recipe** and clears **Costing**; ticking **Costing** clears it, since the costing has the same total at its top. It stays as you leave it, like the other ticks.
- Each printed section lists its ingredients with their **Grams** and **Baker's %** (or **Absolute %**), and with the costing their **Cost/g** and **Cost**. The **Base** ticks aren't printed.
- The printed costing is laid out as the Costing tab is: the breakdown down to the **Subtotal (base cost)**, then, in the same table, the **Suggested Price** under the amounts beside the **Current Price** under **% of Price**, each with its Markup (its percentage in brackets, lighter, then the amount, such as *(30.0%) 30.00*), VAT and selling price, and the rounded suggested price when Settings sets a price rounding. The current price's Markup prints amber below the Markup Metric and red below the base cost. A Price Simulator report keeps its own layout (see [Printing a Price Simulator report](#printing-a-price-simulator-report)).
- **Print** (the button at the top right) opens Windows' print window. Choose the printer there. The pages are already laid out on the paper, orientation and margins set in [Settings → Printing](#printing), which the print window starts with.
- **Export to PDF** (beside **Print**) saves the pages the preview shows as a PDF file, with no print window: the products ticked, the parts included, a production copy. Choose where to save it: the file is named after the set (such as *Breads.pdf*) or the product to start with. A message says where it went.
- **Production copy** prints the recipe for the production floor: no costs at all (Include's ticks are set aside while it's ticked), larger type, a **Done** column with a box to tick beside each ingredient and packaging line as it's weighed, and lines for **Weighed by**, **Checked by** and **Date** at the end. *Production copy* is printed under each product's title. Like Include's ticks, it stays as you leave it for your Windows account.
- **Draft**: a product printed with changes not saved yet, or never saved, has a red banner under its title saying so, and on paper a faint **DRAFT** across every page, so it isn't taken for the recipe as it stands. Save it on the Products screen and print again for a clean copy.

To print a set of products, right-click in the Products list and choose **Print Folder**, **Print Stage** or **Print All Products**. Products picked in the list (Ctrl-click each, or Shift-click for a run) print from their right-click menu too (**Print 3 Products**). Each opens the Print window with those products ticked under **Print**, in place of any ticked before, and every category offered. A folder's pages keep its name as their title while its products are the ones ticked. **Print** in the Products header unticks them all, back to **This product**.

A set prints each product **as saved**, with a contents list first, giving the **Page** each product starts on, and each product starting on a new page. The Print window shows the same: the contents on a sheet of its own, then a sheet for each product. A sheet there is as long as its product, so a long recipe is one tall sheet on the screen that prints on two pages or more: a dashed line marks where each page after the first starts, with its number (**Page 2**) at the right. If the product open on the Products screen has unsaved changes, the Print window warns you that it's printed as saved.

Under each product's title are when it was last updated, when it was printed, and whether it includes unsaved changes. Each page has its number at the bottom. Each page after a product's first starts with the product's name, such as *Asado Roll 60g (continued)*, so a page that comes loose can be put back. A table too long for its page ends there with **Continued on page 3** (the next page's number) under its last row, and goes on at the top of that page under the product's name and the table's section, such as *Asado Roll 60g · Filling (continued)*, then its column headings again. Its **Total** prints once, at the end of the table, with at least its last row. A table that would leave only its first row on a page starts on the next page. The pages are worked out for the paper, orientation and margins in [Settings → Printing](#printing) (A4, portrait, normal margins until you change them), at the print window's own scale: choose another paper size, scale or margins in the print window and the pages may break elsewhere, so a "Continued on page" note can name the wrong page. Change them in Settings instead. The print window can add a line of its own at the top and bottom of each page, with the date and the page's address: untick **Headers and footers** there to leave it out.

While the Products screen shows unbuffered costs, everything printed, a single product or a set, is at the materials' true costs. Each product's page says **Unbuffered costs** under its title and over its costing.

Close the Print window with its **✕** at the top right, or **Esc**: the Products screen is as you left it, changes not yet saved included. Going to another screen (Back, **Alt+Left**, or the activity bar) closes it too.

### Exporting a product to PDF

Press **Export to PDF** in the Products header, beside **Print**, to save the product's recipe and costing as a PDF file without opening the Print window. Choose where to save it: the file is named after the product to start with. A message says where it went.

The PDF has the same pages **Print** shows, on the paper set in [Settings → Printing](#printing) and broken the same way, including changes you haven't saved yet, and always has both the recipe and the costing. On the History tab, it's the version picked there. While the screen shows unbuffered costs, so does the PDF, and with unsaved changes it's marked **Draft**. To leave out the recipe or the costing, to make a production copy, or to put several products in one PDF, use **Export to PDF** on the **Print** tab, which saves what its preview shows.

A production plan prints from TinapayanBMS's **Plan** tab: see [Production Plan](#11-production-plan). A report of what price changes would do prints from the **Price Simulator**: see [Printing a Price Simulator report](#printing-a-price-simulator-report).

---

## 9. Reports

Reports has five tabs along the bottom: **Product Cost Summary**, **Raw Materials Price Summary**, **Cost Buffers**, **Cost Drivers** and **Margin Erosion**. Each is a table you can read but not edit (Cost Buffers has one button that changes buffered costs, after asking). Click a column's heading to sort by it, and click it again to reverse the order. The figures are worked out again each time you open Reports, so they always use today's prices.

### Product Cost Summary

One row for every product, as it's saved:

- **Product**, its **Stage** (Production or Innovation) and its **Category**.
- **Pieces**: how many pieces make one sellable unit (1 for a product sold by the piece). Every amount after it is for one sellable unit. Blank for a composite product, which is one unit made of other products.
- **Ingredients Cost**: the recipe's cost, the **Sub-assemblies (ingredients)** row of the product's Costing tab. For a composite product, it's what its products bring (and its own sub-assemblies).
- **Wastage Cost**: the Costing tab's **Wastage** row, the product's Wastage % (its category's, or the shop's) of the ingredients cost.
- **Packaging Cost**: the **Packaging** row of the Costing tab. For a composite product that includes its products' packaging, theirs is counted too.
- **Labor Cost**, **Overhead Cost** and **Admin Cost**: the Costing tab's Labor, Overhead and Admin metrics in pesos, each a percentage of the ingredients cost.
- **Base Cost** and **Suggested Price**: as the product's Costing tab works them out.
- **Rounded Price**: the suggested price rounded up as Settings → Costing Defaults → **Price Rounding** says; the same as the suggested price with no rounding set. The base cost is the six costs before it added up, and the suggested price is the Costing tab's **Suggested Price**.
- **Current Price**: what the shop charges now, as entered on the product. Blank if none is entered.
- **Markup %**: what the current price earns on the base cost, as the product's header shows it. Blank without a current price.
- **Target Markup %**: the **Markup Metric** on the product's Costing tab, what you're aiming for.
- **Short By**: how many percentage points **Markup %** is under **Target Markup %**. Blank when it isn't flagged short (under both its Target Markup % and any [Warn Below %](#costing-defaults)), or there's no current price.
- **No Piece Size**: the product's recipe sections that have neither a weight per piece nor a yield, such as **Topping**. Each adds nothing to the product's costs until one is typed. Blank when every section has one.

Sort by **Markup %** to see which products earn the least, or by **Short By** to see which fall furthest short of what you're aiming for. Sort by **No Piece Size** to bring the products missing a piece size together.

To show only some products, click the funnel in the **Product**, **Stage** or **Category** heading. Untick what you don't want to see, or untick **(Select All)** and tick only what you do, then click **OK**. The search box at the top finds a name in a long list. A funnel with a dot shows that its column is filtered, and filters in more than one column work together: **Stage** set to Production and **Category** set to Breads shows only the breads in production. **Clear Filter** at the top of the list shows everything in that column again. The filters stay as you set them, for you on this computer: Reports opens with them the next time you open the app.

To choose which columns show, click **Columns** at the top right and untick the ones you don't need, such as **Labor Cost**, **Overhead Cost** and **Admin Cost**. Tick one again to bring it back. Press **OK** to apply your choice. **Product** is always shown, so it isn't in the list. A hidden column's filter still applies: you can filter **Stage** to Production and then hide the Stage column. Like the filters, your choice of columns, and the column you sorted by, are kept for you on this computer: Reports opens with them the next time you open the app.

The headings wrap onto two lines to leave room for all the columns. On a smaller screen, such as a 1366-pixel laptop, the table scrolls sideways, and the **Product** column stays in place.

Double-click a product to open it on the **Products** screen.

### Raw Materials Price Summary

One row for every purchased raw material, ingredients and packaging alike. Made and linked ingredients aren't listed, since they have no price of their own.

- **Raw Material**, its **Type** and its **Category**.
- **Price** and **Unit**: the latest price from the material's price history, and the unit it's for, such as a sack or a kilogram.
- **Change %**: how much that price moved the material's cost from the price before it. It's worked out per gram (or per base unit, for packaging), so it's right even when the two prices are for different units. Blank if the material has only one price.
- **Since**: the day the latest price started.

A material that has never been priced shows blank figures.

Double-click a material to open it on the **Raw Materials** screen.

### Cost Buffers

Every purchased material with a price, and how its fixed **buffered cost** stands against the brand it's costed at now (its [Basis](#basis) brand, or the dearest). Prices rising don't move the buffered cost, so they wear the buffer down: this tab finds the materials where that has happened.

- **Raw Material**, **Type**, **Category** and **Per**: the base unit the costs are per, such as *g*.
- **Brand Cost** and **Buffered Cost**: the true cost (the price of the brand it's costed at) and the buffered cost, per base unit.
- **Buffer %**: how far the buffered cost now sits above the brand it's costed at. Below 0 means a price has gone above it.
- **Target %**: the Cost Buffer % last typed for the material.
- **Short By**: how many percentage points the buffer is under its target. Blank when it isn't.

**Only below target**, ticked to start with, shows only the materials at least a tenth of a point under their target. Untick it to see them all.

**Re-apply Target Buffer** puts each material's target buffer back on top of the brand it's costed at: its buffered cost becomes that brand's price plus the target. It does this for the materials you've picked (click one, or several with **Ctrl** or **Shift**), or with none picked, for every one shown that's below target. It asks first. The new buffered costs reach every made and linked ingredient, recipe and product using them, as a Save on Raw Materials does. Double-click a material to open it on Raw Materials.

### Cost Drivers

The purchased materials that make up most of what the products in **Production** cost, one sellable unit of each, at buffered costs. A material counts however it gets into a product: in a recipe, as an add-in, inside a made ingredient, as the ingredient a linked part is cut from, or as packaging.

- **Products**: how many products in Production use it.
- **Cost in Products**: what one of each of those products spends on it, added up.
- **Share %**: that, as a share of all their material costs, one of each.
- **Cumulative %**: this row's share and every one above it. Where it passes 80%, the materials above are the few that matter most when prices rise.
- **Largest Share %** and **Largest In**: the most it makes up of a single product's material cost, and which product.

It's sorted by Cost in Products, the most first. It doesn't know how many of each product you sell, so treat it as a guide. Double-click a material to open it.

### Margin Erosion

How each product's markup at its **Current Price** has moved since a day you pick in **Since** (30 days ago to start with). It compares the product's latest version saved on or before that day, at its costs and price then, with the product now.

- **Version** and **Saved**: the version it's compared with.
- **Base Cost Then** and **Now**, **Price Then** and **Now**, **Markup % Then** and **Now**.
- **Change**: the markup now less then, in percentage points. Below 0 means it earns less now.

**Only dropped**, ticked to start with, shows only the products whose markup fell. A product isn't listed when it had no version by that day (its history began later) or no Current Price then or now. Double-click a product to open it.

### Production Results

Production Results moved to TinapayanBMS's **Plan** tab on 2026-10-31, with the saved plans whose results it adds up (see [Production Plan](#11-production-plan)).

### Exporting to Excel

Each tab has **Export to Excel** at its top right. It saves the table to an Excel workbook (an .xlsx file) exactly as it's shown:

- in the order you sorted it;
- with only the rows your filters leave;
- with only the columns showing (Product Cost Summary's **Columns** list decides which).

A **Save** window asks where to put the file, suggesting a name after the report and today's date, such as *Product Cost Summary 2026-10-03.xlsx*. A green notice says where it went. **Cancel** saves nothing.

The figures are real numbers in Excel, kept to every decimal the app works with, and shown to 2 decimal places as on the screen. So you can add them up or chart them in Excel.

If a file of that name is open in Excel, the app can't replace it. Close it in Excel first, or save under another name.

### Printing a report

Each tab also has **Print** beside **Export to Excel**. It opens the Print window over Reports, with the table as it's shown, as Export to Excel takes it: in your sort, with your filters, and with only the columns showing. Above the table are the report's name, what it shows, how many rows, and when it was printed. **Print** there opens Windows' print window, **Export to PDF** saves it as a PDF file (named after the report and today's date), and its **✕** or **Esc** closes it, back to Reports.

It prints on the paper in [Settings → Printing](#printing), with the shop's heading, and a table longer than a page goes on with its name and column headings at the top of the next. A wide table such as Product Cost Summary fits better on **Landscape** paper, or with fewer columns ticked under **Columns**.

**A raw materials price list** is the **Raw Materials Price Summary** printed: filter its **Category** or **Type** from the funnel in the heading to print, say, the flours, or only the packaging.

---

## 10. Price Simulator

The **Price Simulator** shows what raw material price changes would do to every product, before they happen. It never changes a real price, and nothing else in the app changes. The changes you try last until you close the window or reload the page, unless you save them as a scenario.

### Price Changes

The top table holds the changes to try, one row per raw material:

- **Raw Material**: pick a purchased ingredient or packaging material. Type to search the list.
- **Per** and **Current Price**: the unit it's bought in and what one costs now. These come from the brand it's costed at on **Cost Setup** (its Basis brand, or the dearest), which is the price its cost comes from. A material with no price yet shows its base unit and no price.
- **New Price** or **Change %**: type either one, and the other is worked out. For example, a sack of flour at 1,250 up **10**% shows a new price of 1,375. A material with no price yet needs its new price typed.

**Remove Line** takes the picked row away, and **Clear All** takes them all away, so every figure is today's again.

A change moves the material's cost and its buffered cost by the same amount, as typing its Cost Buffer % again after a real price change would. It reaches every made ingredient using the material and every linked ingredient cut from it, as a real price change does. For example, a rise in eggs reaches egg white and the custard made with it.

### Scenarios

A scenario is the price changes in the table, saved under a name such as "Supplier A quote, Nov", so you can open them again another day. Scenarios are kept in the database, so everyone using the app sees them and backups include them.

The **Scenarios** list, at the left of the Price Changes table, shows each scenario's **Saved** date and time and its **Name**, newest first. The open scenario is highlighted.

- **Click a scenario** to open it. Its changes replace the table's. If the table has changes that aren't saved, you're asked first, and **No** leaves the table as it was.
- **Save Scenario** asks for a name and saves the table's changes under it. To update the scenario that's open, press **Save Scenario** and keep its name. Typing the name of another scenario replaces that scenario's changes, after asking. Saving moves a scenario to the top of the list.
- **Delete Scenario** deletes the open scenario, after asking. Its changes stay in the table.
- **Clear All** empties the table and closes the open scenario.

A scenario keeps what you typed. A **Change %** is applied to the price as it is when you open the scenario, and a **New Price** stays as you saved it. For example, flour saved at up **10**% and bought at 1,300 by the time you open it shows 1,430. Deleting a raw material takes it out of every scenario, and a material that's no longer bought is left out when the scenario opens.

### Products

The lower table shows every product for one sellable unit, at buffered costs, now and with the changes. To see them at the materials' true costs, without their cost buffers, see [Buffered and unbuffered costs](#buffered-and-unbuffered-costs) below.

- **Raw Material Cost** and **Simulated Raw Material Cost**, and the **Change %** between them: the ingredients and packaging, before wastage, labor, overhead and administrative. This is the Products screen's **Material Cost**.
- **Base Cost** and **Simulated Base Cost**, and the **Change %** between them.
- **Suggested Price** and **Simulated Suggested Price**: the Costing tab's Suggested Price.
- **Current Price**: what you charge now, VAT included, as entered on **Products**. Blank when none is entered.
- **Markup %** and **Simulated Markup %**: what the product's current price earns, now and with the changes. Blank for a product with no current price.
- **Price to Keep Markup**: the price that earns today's **Markup %** with the changes. It moves with the base cost: a base cost up 5% gives a price 5% higher. A product no change reaches keeps its current price. Blank for a product with no current price. The figure isn't rounded, so round it to a price you'd charge.

A composite product moves with the products it holds. Tick **Only affected products** to hide the products no change reaches. Sort by **Simulated Markup %** to see which products would earn the least. Filter by product or category from the funnel in its heading, and double-click a product to open it.

**Export to Excel** saves the products as the table shows them, and the price changes, on two sheets of one workbook.

### Buffered and unbuffered costs

The figures start at the materials' buffered costs, as everywhere else in the app. **Unbuffered**, at the top of the **Products** table, switches every figure, now and simulated, to the materials' true costs. An **Unbuffered costs** badge then appears next to the **Products** heading, and the button reads **Buffered**, which switches back. It works the same way as the **Unbuffered** button on the Products screen.

- A price change moves a material's true cost and its buffered cost by the same percent, so a 10% rise in flour is a 10% rise either way.
- **Print Report** and **Export to Excel** follow the switch. Each page of an unbuffered report says **Unbuffered costs**. The workbook is named **Price Simulator (Unbuffered)**, with a **Products (Unbuffered)** sheet.
- Nothing is saved. A scenario doesn't remember the switch, switching doesn't count as a change to the open scenario, and the screen starts on buffered costs each time you open the window or reload the page.

### Printing a Price Simulator report

**Print Report** opens the Print window, titled **Print Preview**, with each product's recipe and costing at the new prices, next to today's figures, on the paper set in [Settings → Printing](#printing). **Print** there opens Windows' print window, and **Export to PDF** beside it saves the pages as a PDF file. Its **✕** or **Esc** closes it, back to the Price Simulator as you left it.

To print only some products, click one in the **Products** table, or several with **Ctrl** or **Shift** held, before pressing **Print Report**. The products you picked stay picked while you change prices. At the top of the preview, the **Print** picker chooses:

- **Selected products**: only the ones you picked. The preview opens on this choice when you've picked any.
- **All products shown**: every product the table shows, with **Only affected products** and the column filters applied, in the table's order.

The first page lists the **Price Changes** tried, then every product with its **Base Cost**, **Simulated Base Cost**, **Change %** and the page it starts on. Each product then starts on a new page, with:

- **Price Changes Applied**: the changes that reach the product. That includes changes reaching it through a made or linked ingredient, or through the products a composite holds. A product no change reaches says so.
- **Recipe**: each sub-assembly's ingredients and the packaging, at the new prices. A line a change moves is in the accent colour, with what it costs today in the **Cost Now** column.
- **Costing**: the Base Cost, Suggested Price and Markup at Current Price with the changes, with today's figure under each. Under the **Current Price** of a product a change reaches is the price to keep today's markup, such as **To keep markup 61.28**. The breakdown has **Now**, **Simulated** and **Change** columns for every part, up to the Suggested Selling Price.

**Include** chooses **Recipe**, **Costing** or both. Without the costing, the report prints the recipes with no costs and no price changes.

## 11. Production Plan

Production planning moved to **TinapayanBMS**, the app that weighs ingredients out ahead of mixing, on 2026-10-31, and this screen was taken out of TinapayanRMS. TinapayanBMS's **Plan** tab does everything it did: the plan of products and units, half batches and the day, the Mixing Sheet with Use the spare, the Purchase Requisition, the plan's sales and profit, saved plans, what was made and left over, Production Results, printing and Export to Excel. It works from a copy of this app's recipes, ingredients, prices and costs, which it brings up to date through the Recipe API (see [Share the recipes with TinapayanBMS](#share-the-recipes-with-tinapayanbms)). Change a recipe or a price here, then press **Sync** in TinapayanBMS.

## 12. How costs are worked out

### The cascade

When a cost changes, the app carries the change upwards:

```
A purchased ingredient's price changes
  → every made or linked ingredient that uses it (all the way up the chain)
    → every sub-assembly with those ingredients in its lines

A sub-assembly's lines change
  → every made ingredient made from it, and on up the same way
      → every product that uses those sub-assemblies
        → every composite product that holds those products

A packaging material's price changes
  → every product and packaging set that uses it
    → every composite product that includes those products' packaging
```

This happens as soon as you save. A product you had open on the Products screen shows the new figures when you go back to it.

### Raw materials

- **Purchased**: cost per base unit = the price of the brand in Basis (the dearest brand unless one is picked) ÷ the base units in its purchase unit. The buffered cost is a fixed amount. It's set from the Cost Buffer % on the first price, or whenever you type the percentage again.
- **Made**: final weight = the grams of its sub-assembly's lines and its add-ins × (1 − Weight Loss %). Cost per gram = their total cost ÷ final weight. The buffered cost uses their buffered costs.
- **Linked**: cost per gram = linked-to ingredient's cost per gram × Cost Share % ÷ Weight Share %.

### Sub-assemblies

- Each line's cost = grams × the ingredient's buffered cost per gram.
- Batch weight and batch cost = the sum of the lines.

### A product's sections

- Cost per piece = batch cost × weight per piece ÷ batch weight (including add-ins). This is the same as batch cost ÷ yield.
- A section on another's lines costs that one its grams at its own cost per gram: its batch cost ÷ its batch weight. Locked, it has no pieces of its own; unlocked, its pieces are of what's left: its batch weight less what the others use of it.

### The selling price (cost-plus)

All figures below are per sellable unit.

1. **Sub-assemblies** = the sum of the sections' cost per piece × Quantity Required.
2. **Wastage** = the product's category's Wastage % (Settings → Product Categories), or the shop's (Settings → Costing Defaults) when its category doesn't set one, **of the sub-assemblies alone**.
3. **Packaging** = the sum of the packaging lines, which are already per sellable unit, plus the bulk packaging's share: the cost of one bulk pack's lines ÷ **Products per pack**. A ₱12.00 box and ₱0.60 of tape for 6 products adds ₱2.10.
4. **Labor**, **Overhead** and **Administrative** = their percentage **of the sub-assemblies alone**. Packaging and wastage aren't included.
5. **Base cost** = sub-assemblies + wastage + packaging + labour + overhead + administrative.
6. **Price before VAT** = base cost × (1 + Markup %).
7. **Selling price** = price before VAT × (1 + VAT %).

**Markup % at the current price**, in the header, works backwards from the current price: (current price ÷ (1 + VAT %) − base cost) ÷ base cost.

### A worked example

Say a product's sections cost **20.00** a piece, its packaging costs **2.00**, and it's sold by the piece. The shop's wastage is 3% and its VAT 12%, and the product's costing is Labor 25%, Overhead 10%, Administrative 5% and Markup 30%.

| Step | Working | Amount |
|---|---|---|
| Sub-assemblies | 20.00 × 1 | 20.00 |
| Wastage | 3% of 20.00 | 0.60 |
| Packaging | | 2.00 |
| Labor | 25% of 20.00 | 5.00 |
| Overhead | 10% of 20.00 | 2.00 |
| Administrative | 5% of 20.00 | 1.00 |
| **Base cost** | 20 + 0.60 + 2 + 5 + 2 + 1 | **30.60** |
| Markup | 30% of 30.60 | 9.18 |
| Price before VAT | | 39.78 |
| VAT | 12% of 39.78 | 4.77 |
| **Selling price** | | **44.55** |

If the shop actually charges **45.00**, the header's Markup % is (45.00 ÷ 1.12 − 30.60) ÷ 30.60 = **31.30%**.

A **section's cost per piece**, for comparison: a 1,000 g dough costing 50.00, at 30 g a piece, yields 33.33 pieces, and each piece costs 50.00 × 30 ÷ 1,000 = **1.50**.

### A composite product

A composite product is priced like any other product, with its products in place of sub-assemblies:

1. **Products** = the sum of its lines. Each line is its Qty × the product's recipe cost for one sellable unit, which is the **Sub-assemblies** figure on that product's own Costing tab. The products' packaging isn't included here. The composite's own sections, such as a Topping, are added at their cost per piece, since one piece is one whole composite.
2. **Packaging** = the composite's own packaging. With **Include each product's packaging** ticked, add each line's Qty × the product's **Packaging** figure on its own Costing tab. A composite held in another brings all of its packaging this way, its products' included if it includes theirs.
3. **Wastage**, **Labor**, **Overhead** and **Administrative** = their percentage of step 1: its products, and its own sections if it has any. Each product brings its recipe cost before its wastage, so the wastage is added once, here, on the whole of step 1.
4. Then the base cost, markup, VAT and selling price, as above.

For example, a *Pasalubong Box* holds 6 Ensaymada, whose recipe costs **2.50** each, and 4 Cheese Rolls at **5.00** each. It has a gift box costing **12.00**, and the same percentages as the worked example above.

| Step | Working | Amount |
|---|---|---|
| Products | 6 × 2.50 + 4 × 5.00 | 35.00 |
| Wastage | 3% of 35.00 | 1.05 |
| Packaging | the gift box | 12.00 |
| Labor | 25% of 35.00 | 8.75 |
| Overhead | 10% of 35.00 | 3.50 |
| Administrative | 5% of 35.00 | 1.75 |
| **Base cost** | 35 + 1.05 + 12 + 8.75 + 3.50 + 1.75 | **62.05** |
| Markup | 30% of 62.05 | 18.62 |
| Price before VAT | | 80.67 |
| VAT | 12% of 80.67 | 9.68 |
| **Selling price** | | **90.34** |

With the same percentages as its products and no packaging or sections of its own, a composite is priced at exactly what its products are priced at together. If its products have packaging of their own, that holds only with **Include each product's packaging** ticked.

Now suppose each Ensaymada goes into the box in its own bag, costing **1.50**, and **Include each product's packaging** is ticked. Packaging becomes 12.00 + 6 × 1.50 = **21.00**. Wastage, Labor, Overhead and Administrative stay at 1.05, 8.75, 3.50 and 1.75, since packaging isn't part of step 1. So the base cost is 35 + 1.05 + 21 + 8.75 + 3.50 + 1.75 = **71.05**, the price before VAT is 71.05 × 1.30 = 92.37, and the selling price is 92.37 × 1.12 = **103.45**.

Give the same box an icing topping that costs **5.00** for one box, and step 1 becomes 35.00 + 5.00 = **40.00**. Wastage, Labor, Overhead and Administrative are then taken of 40.00. A composite held in another composite brings its whole step 1, topping included.

### Rounding

The app keeps costs at full precision and rounds them only for display. So a total can differ from the sum of the rounded figures above it by a cent.

---

## 13. How to…

### Record a price increase

1. Open **Raw Materials** and pick the material.
2. On **Cost Setup**, change the brand's **Purchase Price**, or add a row for a new brand.
3. The buffered cost stays where it was. To carry the new price into recipes with the buffer on top, **type the Cost Buffer % again**, or press **Re-apply Target**. Alternatively, type a new Buffered Cost. After a round of price rises, **Reports → Cost Buffers** finds every material whose buffer they wore down, and re-applies them all at once.
4. Press **Save**. The change is logged in **Price History** and carried up to every product that uses the material.
5. Check the effect on the material's **Dependents** tab, or open a product and look at its Costing tab.

### Start a new product from an existing one

- For **a different product that shares some recipes**, use **Clone** (Clone tab, or right-click → **Clone…**). It gets its own packaging.
- For **a size or pack variation** that should follow the original's recipes, use **Create Variant**. Then adjust its piece sizes, give it its packaging, and set its **Quantity Required** if it's sold as a pack.
- To **reuse just one dough or filling**, use **Add Sub-Assembly → Link an existing one** in the new product.

### See which products use an ingredient

- **Raw Materials** → pick the ingredient → **Dependents** tab.
- For a shared sub-assembly: **Sub-Assemblies** → pick it → **Dependents**.

### Check whether a product's price still earns enough

Open the product and compare the header's **Markup %**, which is what the current price earns, with the **Markup Metric** on the Costing tab, which is what you're aiming for: the header's figure is amber when it's short. In the Products list, a ⚠ marks every product that's short. Both wait for the **Warn Below %** (Settings → Costing Defaults, or the product's category): with one set, a product a hair under its target isn't marked. For an overview of all products, look at **Reports → Product Cost Summary** and sort by **Short By**.

### Bring back a deleted product

Open the **Deleted** folder at the bottom of the Products list, right-click the product and choose **Restore**. It goes back to its folder as it was when you deleted it.

### Go back to an earlier recipe

1. Open the product and its **History** tab.
2. Pick the version, and read its **Changes** to check it's the one you want. Set **Compare With** to **The product as saved now** to see everything that would change, or press **Print Version** to see its whole recipe.
3. Press **Restore**. The version is now in the form, not yet saved.
4. Check the Recipe and Costing tabs, then press **Save**. Or press **Undo** to put the form back.

### Move a product into production

Right-click it in the Products list and choose **Promote to Production**, or drag it into a Production folder. It goes in the folder with the same path in Production, made if it isn't there. Right-click an Innovation folder and choose **Promote All to Production** to promote every product in that folder and the folders in it.

### Plan a day's baking

Plan it in TinapayanBMS's **Plan** tab: see [Production Plan](#11-production-plan).

### See what a price rise would do

1. Open the **Price Simulator** from the activity bar.
2. In the top table, pick the raw material and type its **Change %**, such as **8**, or its **New Price**.
3. Tick **Only affected products** and sort by **Simulated Markup %**.
4. Read **Price to Keep Markup** for what to charge to keep each product's markup. Change a product's **Current Price** on **Products**.
5. To see why a product moves, Ctrl-click it and any others, press **Print Report**, and read each product's ingredients and costing, now and with the change. Print it, or save it as a PDF.
6. To keep the changes for another day, press **Save Scenario** and name it. Click it in the **Scenarios** list to open it again.
7. To make the price real, record it on **Raw Materials** as in [Record a price increase](#record-a-price-increase). The Price Simulator never changes a price itself.

### Back up to a USB stick

To have it done every day, set the USB stick as **Also Copy To** on the **Database** tab and leave it plugged in. To copy one now:

1. Put in the USB stick.
2. Open **Settings**, then the **Database** tab.
3. Press **Back Up Now**. The new backup is picked in the list.
4. Press **Copy To…**, pick the USB stick, and press **Save**.

### Restore a backup

1. Open **Settings**, then the **Database** tab.
2. Pick the backup in the list. For a backup on a USB stick, press **Restore from File…** and pick the file instead.
3. Press **Restore**, read the message, and press **Yes**.
4. Wait while it restores. The window then starts its screens afresh, with the backup's data.

If the data restored wasn't the one you wanted, the **Before restore** backup holds what was there just before. Restore that one to undo it.

### Send a report to Excel

1. Open **Reports** and pick the tab.
2. Sort, filter and choose the columns as you want them in the file.
3. Press **Export to Excel**, choose a folder and a name, and press **Save**.

### Print the recipes of a whole folder

Right-click the folder in the Products list and choose **Print Folder**: it prints the folder's products and those of every folder in it. Untick **Costing** for kitchen copies.

### Print a few products together

1. Open the **Print** tab (press **Print** in the Products header).
2. Open the **Print** list at the top left, type part of a name to find a product, and tick it. Do the same for each product you want; they can be in different folders. To choose among one kind only, tick its category under **Categories** first.
3. Close the list (click elsewhere, or press **Esc**). The pages show the products ticked. Press **Print** at the top right.

### Make a composite product, such as a gift box of several products

1. Under the Products list's search box, press **New Composite Product**. Or right-click a folder in the list and choose **New Composite Product Here**.
2. Type its name into the *New composite product* row the list shows, and press **Enter**. It's saved once you press **Save** in step 6.
3. On the **Products** tab, add a line for each product: pick the product, then type how many go in under **Qty**.
4. On the **Packaging** tab, type the box into **Packaging per Piece**, with anything wrapping the pieces inside it, or **Link** a packaging set that has them. The products' own packaging isn't included. If the pieces go in already in their own packaging, tick **Include each product's packaging** under the Products table instead of listing it again.
5. For a topping or filling over the whole composite, use **Add Sub-Assembly** in that role. Its **Weight/piece** is what one whole composite takes.
6. Check the **Costing** tab, then press **Save**. A composite needs at least two different products.

### Give a product a box for a pack of several pieces

1. On **Sub-Assemblies**, right-click the **Packaging** folder, choose **New Packaging Set Here**, type its name and press **Enter**. List the box and the bags one pack takes, then Save.
2. Make the pack product. For example, use **Create Variant** of the single and set **Quantity Required** to 8.
3. On its **Packaging** tab, press **Link** and pick the set. Save.

Or type the packaging straight into the pack product's Packaging tab, then press **Move to Sub-Assemblies** and Save. The set is made from those lines and linked for you.

### Add a box several products share

For products packed several to a box, whether or not each is in its own wrapper first:

1. Open the product on its **Packaging** tab, which every product has. Under **Packaging per Piece** is **Bulk Packaging**.
2. In **Products per pack**, type how many products one box holds, such as 6.
3. In the Bulk Packaging table, add what one box takes: the box itself (Qty 1), and anything else for the whole box, such as its tape. Or, if the box is kept as a packaging set on the Sub-Assemblies screen, press **Link** beside Products per pack and pick the set.
4. Check the caption under the table, then Save. Each product now carries a sixth of the box's cost, on top of any packaging of its own.

### Install or update TinapayanRMS

TinapayanRMS is installed from its source folder, on the PC that holds its database, for the Windows user who'll use it. That PC needs what building the app needs (see the developer manual's setup): the .NET SDK, its MAUI workload, PostgreSQL with the app's sign-in, and the Syncfusion licence key.

1. Close TinapayanRMS if it's open. If you forget, the script closes it for you, unless it's asking whether to save changes: then it stops, and you answer, close the app and run it again.
2. Open PowerShell, go to the TinapayanRMS folder, and run `powershell -ExecutionPolicy Bypass -File scripts\Install-TinapayanRMS.ps1`. It builds the app, which takes a few minutes, copies it to `%LocalAppData%\Programs\TinapayanRMS`, and puts a **TinapayanRMS** shortcut in the Start menu and on the Desktop. Add `-NoDesktopShortcut` to leave the Desktop as it is.
3. Open the app. If this version changes the data, the first start updates it, after a backup (see [What an update does](#what-an-update-does)).

Run the same command again to update the app after the code changes. How the lists were left, your Vim keys setting, the backups and the database all stay as they are. To remove the app, run `scripts\Uninstall-TinapayanRMS.ps1` the same way.

### Install TinapayanRMS from its installer (MSI)

The installer, a file named like `TinapayanRMS-1.0.414-x64.msi`, puts TinapayanRMS on a PC for every Windows user, in `C:\Program Files\TinapayanRMS`, with a **TinapayanRMS** shortcut in everyone's Start menu and on everyone's Desktop. Whoever looks after the app makes it from the source folder (the developer manual says how). The PC it goes on needs no build tools, only:

- the **.NET 11 Runtime (x64)**, from Microsoft's .NET download page. Without it the installer stops before changing anything and says where to get it;
- PostgreSQL, and, for each Windows user who'll use the app, the app's sign-in in that user's password file, as for the other way of installing. On a new PC, `scripts\Setup-Database.ps1` sets both up (see [Move TinapayanRMS to a new PC](#move-tinapayanrms-to-a-new-pc)). The app's database itself needn't be there: the app makes it the first time it opens.

1. Close TinapayanRMS if it's open. If it's still open, the installer offers to close it. If it has changes you haven't saved, it stays open and asks about them instead, and the installer can't close it: answer the app, which then closes. If the installer asks to restart Windows at the end, restart before you open the app again.
2. Open the `.msi` file and agree when Windows asks to let it make changes. It needs an administrator.
3. Open the app. If this version changes the data, the first start updates it, after a backup (see [What an update does](#what-an-update-does)).

To update, on this PC or another, open the newer installer the same way: it replaces the version installed. It won't install over a newer version. Each user's lists, Vim keys setting, backups and the data stay as they are. To remove the app, go to **Settings > Apps > Installed apps**, find **TinapayanRMS**, and choose **Uninstall**.

### What an update does

A new version of the app sometimes needs to add to how the data is kept, such as the table for the password. You don't have to do anything for it:

- **The first time the new version opens**, before it asks for the password, it sees the data is from an earlier version. It takes a backup of it, listed in **Settings → Database** as **Before update**, then brings the data up to the new version. The password box says so while it works; it takes a few seconds. This happens once: the next start opens as usual.
- **On a PC where the app's data isn't there yet**, the app makes it, empty, and asks you to set a password. Then set things up as in [First-time setup](#3-first-time-setup), or restore a backup in Settings → Database.
- **If several PCs use the same data**, update them all. The first one to open the new version updates the data, and a PC still on the older version then won't open, saying the data was updated by a newer version: install the newer version there too.
- **If the backup can't be taken**, nothing is changed and the password box says why. Nothing is updated without a backup to go back to.

### Go back from an update that went wrong

If a new version doesn't work, you can go back to the version before it until it's fixed. The older version won't open data the newer one has updated, and its installer won't install over the newer one. So the data goes back to its **Before update** backup, and the older version is installed again, in one step:

1. Close TinapayanRMS on every PC that uses this data.
2. On the PC that holds the database, open PowerShell **as an administrator**, go to the TinapayanRMS folder, and run `powershell -ExecutionPolicy Bypass -File scripts\Undo-Update.ps1 -Msi <the older installer>`. It says when the backup it puts back was taken, and asks before it changes anything. Type `y`.
3. Open the app. It opens the data as it was just before the update.

Anything saved after the update isn't in the restored data. It isn't lost: the data as it was is kept, under another name that the script shows (such as *tinapayanrms_before_undo_20261126_101500*), for whoever looks after the app to copy back what's needed. When the fixed version comes, install it as usual: it updates the data again, after another **Before update** backup.

Without `-Msi`, the script puts the data back only, and you install the older version yourself before anyone opens the app: uninstall TinapayanRMS in **Settings > Apps**, then open the older installer. A copy installed with `scripts\Install-TinapayanRMS.ps1` is installed again by running that script from the older version's source.

### Move TinapayanRMS to a new PC

1. Install PostgreSQL 18 on the new PC.
2. Copy the newest backup from the old PC to the new one: the folder **Also Copy To** copies to, or **Copy To…** in **Settings → Database**. If the old PC won't start, the copy in the **Also Copy To** folder is the one to use.
3. Open PowerShell on the new PC, as the Windows user who'll use the app, go to the TinapayanRMS folder, and run `powershell -ExecutionPolicy Bypass -File scripts\Setup-Database.ps1 -Backup <the backup file>`. It asks for the password of PostgreSQL's *postgres* account, chosen when PostgreSQL was installed. It sets up the app's sign-in to PostgreSQL and restores the backup as the app's data.
4. Install TinapayanRMS from its installer, and open it. If the backup is from an earlier version, the first start updates it.

Each other Windows user of the PC who'll use the app runs `Setup-Database.ps1` too, without `-Backup`. It asks for the app's sign-in password, which is in the first user's `%APPDATA%\postgresql\pgpass.conf`. To start with no data at all, run it without `-Backup`: the app makes an empty database the first time it opens.

### Share the recipes with TinapayanBMS

TinapayanBMS, the app for weighing ingredients out ahead of mixing, keeps a copy of TinapayanRMS's recipes, ingredients, prices and costs, and plans production from it. It copies them through the **Recipe API**, a small program that runs in the background on the PC that holds the TinapayanRMS database. The Recipe API only reads. It never changes a recipe, a price or anything else.

1. On the PC that holds the TinapayanRMS database, open PowerShell **as an administrator**, go to the TinapayanRMS folder, and run `powershell -ExecutionPolicy Bypass -File scripts\New-DatabaseReader.ps1`, then `powershell -ExecutionPolicy Bypass -File scripts\Install-RecipeApi.ps1 -ReadOnlyUser tinapayanrms_reader`. The first makes a database sign-in that can read and never change anything, which the Recipe API then uses. You only need it once on that PC. The second installs the Recipe API, starts it, and starts it again each time you sign in to Windows. Running it as an administrator also lets the other PCs on the shop's network reach it. The first time, it also makes the Recipe API's **key**, a long password for apps, and shows it at the end. Copy it for the next step.
2. In TinapayanBMS's **Settings**, type the Recipe API's address. Use `http://localhost:5290` when TinapayanBMS runs on the same PC, or `http://` and that PC's name or network address followed by `:5290` when it runs on another PC. Paste the key into **Key**. Then press **Save and Test**.

The Recipe API answers only an app that gives it the key, so other programs on the shop's network can't read your recipes, costs and prices. Every copy of TinapayanBMS uses the same key. To copy it again, run this in PowerShell on the PC with the Recipe API, then paste it where it's needed:

```powershell
(Get-Content "$env:LOCALAPPDATA\Programs\TinapayanRMS.RecipeApi\recipeapi.json" -Raw | ConvertFrom-Json).RecipeApi.Key | Set-Clipboard
```

Running the install script again, to update the Recipe API, keeps the same key, and the read-only sign-in, so `-ReadOnlyUser` is only needed the first time. Removing the Recipe API removes its key too, so after installing it again TinapayanBMS needs the new key in its Settings.

When TinapayanBMS can't get through, what it says points to the reason:

- **"TinapayanRMS didn't accept the key":** the key in its Settings isn't the Recipe API's. Copy it again as above.
- **"answered with an error (503)":** the Recipe API is running but can't use the database. Check that PostgreSQL is running on that PC, then try again.
- **"answered with an error (403)":** the Recipe API has no key, so it only answers on its own PC. Run the script in step 1 again. It makes a new key.
- **"answered with an error (500)":** something unexpected went wrong in the Recipe API.

The Recipe API's log, in the `logs` folder of `%LocalAppData%\Programs\TinapayanRMS.RecipeApi`, says more about an error.

The Recipe API runs only while someone is signed in to Windows on that PC. TinapayanBMS keeps working from its own copy of the recipes while it isn't running; it just doesn't see changes made here until it can sync again. To remove the Recipe API, run `scripts\Uninstall-RecipeApi.ps1` the same way.

---

## 14. Troubleshooting and questions

**"Something can't be deleted."**
It's still in use. The message says where. For example:
- A category, unit or role that something is in.
- An ingredient used in a recipe or as a made ingredient's add-in, or linked by a part.
- A sub-assembly a product links, or that a made ingredient is made from.
- A product whose packaging another product links.
- A product that a composite product holds.

Move or remove what uses it first. When the message names a product with "(deleted)" after it, that product is in the Products list's **Deleted** folder. It still holds what it uses until you restore it and take it out there, or right-click it and choose **Delete Permanently**.

**"A deleted product is named …"**
A product in the **Deleted** folder keeps its name. Restore it, or delete it permanently, before giving its name to another product.

**"A composite product needs at least two different products."**
A composite is made of two or more different products. For more of one product, give that line a bigger **Qty**. For a pack of one product, use **Quantity Required** on a product of its own instead.

**"… can only have sections in product-owned roles …"**
A composite product's own sections can only be in a role with **Product-owned** ticked in Settings ▸ Roles, such as Topping or Filling. Pick one of those, or tick Product-owned for the role. Settings also won't untick Product-owned, nor the Sub-Assemblies screen move a sub-assembly out of such a role, while a composite has a section there: the message names it.

**"… can't be on Dough's lines: Settings → Roles doesn't let a … section go into another …"**
A line of a section holds another section whose role doesn't have **Into other sections** ticked in Settings ▸ Roles. Tick it for that role, or delete the line. Settings won't untick it while a product has a section in the role on another's lines, nor will the Sub-Assemblies screen move such a sub-assembly into a role without it: the message names the products. Take the section off those lines first, or pick a role that has it.

**A product can't go into a composite.**
The product picker leaves out the composite itself, and any composite that already holds it. Otherwise the composite would end up holding itself.

**"Couldn't reach the database."**
The app keeps its data in PostgreSQL on this computer, and it isn't answering. Check that the **postgresql-x64-18** service is running in Windows' **Services**, then try again. The screen you're on keeps working: switch to another tab and back, or pick the record again, once the database is running.

**"The database didn't answer in time."** or **"The database was busy with another change."**
Nothing was saved. Try again.

**A yellow bar under the title bar says the database is out of date for this version of TinapayanRMS.**
The app was updated, but the update to its database wasn't applied. Close the app and ask whoever installs it to apply the database update. Until they do, some screens show an error.

**"Couldn't write the file. If it's open in another program, such as Excel, close it there and try again."**
The file you're saving over is open, usually in Excel. Close it there and try again, or save under another name.

**"Windows didn't allow the app to use that file or folder. Pick another one."**
The folder is protected, such as a system folder. Save in Documents, on the Desktop or on a USB stick instead.

**"That file or folder isn't there any more. It may have been moved or deleted."**
The file or folder was moved or deleted after you picked it, or the USB stick it was on was taken out. Pick it again, or pick another one.

**"Couldn't read or write the file: …"**
Windows couldn't finish with the file, for the reason it gives, such as a full disk. Put that right or pick another place, then try again.

**"PostgreSQL's pg_dump reported a problem: …"** or **"PostgreSQL's pg_restore reported a problem: …"**
PostgreSQL's own tool stopped a backup or restore, for the reason it gives. No backup was made and the data wasn't changed. Ask whoever set up the computer to look into it. *No password supplied* means this Windows user has no password file for the database.

**The app doesn't open.**
Something the app needs before it can open is missing or wrong, such as its settings file. The log says what. Ask whoever installs the app to look into it, and give them the log.

**"Backups need PostgreSQL's pg_dump and pg_restore, and they weren't found on this computer."**
Backups are taken by PostgreSQL's own tools, which come with PostgreSQL. Ask whoever set up the computer to install PostgreSQL's command-line tools, or to tell the app where they are.

**"That file isn't a backup of a whole database, so it can't be restored."**
The file picked isn't one of the app's backups, or it was damaged, for example by a USB stick pulled out while copying. Nothing was changed. Try another backup.

**"That backup was made by a newer version of PostgreSQL than this computer has, so it can't be restored here."**
The backup came from a computer with a newer PostgreSQL. Ask whoever set up this computer to update PostgreSQL, then restore it again. Nothing was changed.

**"That backup was made by a later version of TinapayanRMS, so this version can't use it."**
Update the app on this computer to the version the backup came from, then restore it again. Nothing was changed.

**"Another program kept using the database, so it couldn't be restored."**
Something else, such as another copy of the app or pgAdmin, had the database open. Close it, then restore again. Nothing was changed.

**"The backups folder … was made by another Windows user, without the access everyone needs."**
Ask whoever looks after the computer to give **Users** **Modify** on that folder, in the folder's **Properties**, **Security** tab.

**"A number is too large to save."**
A figure has more digits than the app keeps, for example a price typed with too many zeros. Check what you typed.

**A number box doesn't take what I typed.**
A number box takes only digits, commas between thousands and one decimal point, so a letter or symbol doesn't appear when you press it (see [Editing and saving](#editing-and-saving)). It takes numbers under a billion (999,999,999.9999 at most). If you type more digits than that, for example by holding a key down, the box keeps the last number it could take and shows it when you leave the box. Check the figure and type it again.

**A screen says "This screen ran into a problem it didn't expect".**
Something happened on that screen that the app didn't expect. It's in the log file, which helps whoever looks after the app. Press **Open It Again**: the screen comes back as it was, with any changes you hadn't saved. Going to another screen and back does the same.

**A bar at the bottom of the window says something went wrong, and that the page needs reloading.**
Something happened outside the screens that the app didn't expect. It's in the log file. Press **Reload**. What you had saved is safe; what you hadn't saved is lost.

**Opening the app brings up the window that's already open, not a new one.**
Only one copy of TinapayanRMS opens at a time on a PC, so that two copies don't run the same backups or overwrite each other's settings. Another PC can still use the same database. If both change the same product, raw material or sub-assembly, the second **Save** is refused (see below).

**Opening the app says it's open in another Windows sign-in on this PC.**
Someone else signed in to Windows on this PC has TinapayanRMS open. Only one copy can be open on the PC, for the same reason as above. Sign in as them and close it there, or ask them to, then open it again.

**"Someone else saved … after you opened it, so your changes weren't saved."**
This product, raw material or sub-assembly was saved while you had changes on it, and saving yours would have overwritten what was saved. Usually that's another PC using the same database. A product is also saved by its variant: a variant's **Save** that changes a section of this product's own saves this product too, even from another tab on this PC (see [Clone tab](#clone-tab)). What you typed is still on the form, and **Save** keeps refusing it. Note what you changed. Then close the item's tab with its **✕** and answer **No** when asked about saving your changes. Open the item again: it now shows what was saved. On the Products screen, **Revert** does the same without closing the tab. Make your changes again and press **Save**.

**"…'s lines were saved after you opened this product, so your changes weren't saved."**
You changed a linked sub-assembly's lines in a product's recipe, and that sub-assembly's lines were saved meanwhile: on the Sub-Assemblies screen, from another product's tab or on another PC. What you typed is still on the form. Note it, press **Revert** to see the lines saved, then make your changes again and press **Save**.

**"… became this product's own after you opened this product, …"** or **"… moved to the Sub-Assemblies screen after you opened this product, …"**
**Product-owned** was turned on or off in Settings ▸ Roles, after you opened the product, for the role of one of its sections or of one you had taken out, and you had changed the recipe. Nothing was saved, and what you typed is still on the form. The message ends with what to do:

- **"… press Save again."** You hadn't changed that section (Product-owned was changed on another PC), and the product now shows it as it is now. Press **Save** again: you don't need **Revert**.
- **"… press Revert …"** You had changed that section, or taken it out. Note what you typed, press **Revert** to see the section as it is now, then make your changes again and press **Save**. Revert puts the whole product back as it was last saved, so note every change you want to keep, not only the section's.

**The password is forgotten.**
Clear it, and the app asks for a new one. On the PC that holds the database, signed in to Windows as the app's user, open PowerShell, go to the TinapayanRMS folder, and run `powershell -ExecutionPolicy Bypass -File scripts\Reset-Password.ps1`. It asks first; answer **y**. The next time the app opens, or its window is locked, it asks you to set a new password. Nothing else changes, the auto-lock's minutes included.

**"Too many wrong tries: wait 30 seconds, then try again."**
Five wrong passwords in a row make the next try wait half a minute. Check Caps Lock while you wait.

**"The database has been updated by a newer version of TinapayanRMS than this one…"**
Another PC, or another copy, with a newer version has already updated the data, and this copy can't use it. Nothing was changed. Install the newer version on this PC (see [What an update does](#what-an-update-does)).

**The password box shows an error instead of asking for the password.**
The app couldn't reach or update its data: the message says why, such as the database being off, or a backup before an update that couldn't be taken. Once that's put right, press **Try Again**. The app never opens without the password, even then.

**Opening the app says TinapayanRMS couldn't start.**
Something stopped it before its window opened. The message says what, and where the log file is: show both to whoever looks after the app.

**Opening the app does nothing.**
The copy that's open has stopped responding, or is busy with something long. Give it a moment. If its window doesn't come back, end **TinapayanRMS.Maui** in Task Manager, then open the app again. Anything it hadn't saved is lost.

**"That name is already in use."** or **"That abbreviation is already in use."**
Names must be unique within each list: raw materials, sub-assemblies, products, categories, roles and units. A unit's abbreviation must be unique too. Upper and lower case count as the same. Spaces at the start or end of a name don't count either: they're taken off when it's saved.

**Letters don't type, or a table cell won't start an edit when I type.**
The Vim keys are on, and the top right of the window says NORMAL. Click the field or cell, or press **i**, to type. Turn them off in Settings → Keyboard. See [Vim keys](#vim-keys).

**A figure is grey and can't be typed into.**
It's worked out by the app, or fixed by a rule. Examples are a standard unit's multiplier, the Absolute %, or a linked ingredient's buffer.

**A product's cost didn't change after a price change.**
Purchased materials are costed at their **buffered cost**, which stays fixed until you type the Cost Buffer % again. See [Record a price increase](#record-a-price-increase).

**I changed Quantity Required and the packaging looks wrong.**
Packaging quantities are per sellable unit and aren't rescaled. Update the Packaging tab by hand, or link a packaging set made for that pack size.

**A message asks "Save the changes to …?" when I close a tab.**
The product, sub-assembly or raw material in that tab has changes you haven't saved. **Yes** saves them, **No** throws them away, and **Cancel** keeps the tab open. See [Editing and saving](#editing-and-saving). Going to another screen or another record doesn't ask: the changes wait in their tab, marked with a dot. Closing the window asks the same, for each tab with changes, and so does Windows closing the app to sign out, shut down or install an update. Right after **Save**, it doesn't ask: the app waits for the save to go through first. Reloading the page (**F5**) with changes not saved asks whether to leave instead.

**The window shows a notice about a Syncfusion licence.**
The app needs a licence key for its screen components, set on the computer by whoever installed it. Ask them to check it.

**Where are the log files?**
In the **logs** folder of the app's data folder, *%LocalAppData%\TinapayanRMS\Maui\logs*. There's one file per day, kept for 31 days. They're useful to send along when reporting a problem.
