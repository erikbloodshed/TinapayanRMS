# TinapayanRMS User Manual

TinapayanRMS keeps the recipes and costs of the bakeshop's products in one place. You record what raw materials cost. The app then works out what every recipe, every finished product and every selling price comes to. When a price changes, it carries the change up to every product that uses that material.

This manual is for the people who use the app day to day. It doesn't assume any technical knowledge.

> **Amounts are shown without a currency symbol.** Every amount in the app is in pesos. Costs are shown to 4 decimal places where small figures matter, such as the cost per gram, and to 2 decimal places elsewhere.

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

### Cost buffer and buffered cost

A **cost buffer %** is a cushion on top of what a purchased material really costs. It covers price swings, waste and variation in yield.

The **buffered cost** is the cost with that cushion on top. **Recipes and products are always costed at buffered costs.** To see what a product costs without the cushion, press **Unbuffered** in the Products header (see [The header](#the-header-1)), or on the Price Simulator (see [Buffered and unbuffered costs](#buffered-and-unbuffered-costs)).

For a purchased material, the buffered cost is a **fixed amount**. A new brand price doesn't move it on its own. See [Cost buffer](#cost-buffer) for how to update it.

### Wastage

**Wastage %** is a cost allowance for what's lost making a product: dough left in the mixer, a filling that cooks down, a tray that burns. It's one figure for the whole shop, set in Settings → Costing Defaults (3% unless you change it there). Every product adds it to its costing as a percentage of its **sub-assemblies (ingredients) cost**: 3% of 100.00 of ingredients is 3.00 more. Packaging isn't wasted, so it's left out.

Wastage is a cost, not lost weight. A batch's pieces share out the whole batch, and the wastage goes on the product's Costing tab. A composite product adds it once, on everything it holds, so its products' ingredients are never charged wastage twice.

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
- **Stage**: every product is either in **Research and Development** (still being worked out) or in **Production** (made and sold). A new product goes in Production, unless you change that in Settings → Costing Defaults.
- **Clone** and **Create Variant** start a new product from an existing one. See [Clone tab](#clone-tab).
- **Scaling** works out the ingredients and packaging for any number of sellable units. See [Scaling tab](#scaling-tab).
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

### Baker's % and Absolute %

Each recipe line also shows a percentage.

- **Baker's %**: tick the **Base** box on one or more lines, usually the flour. Every line is then shown as a percentage of the ticked lines' total weight. You can type either the grams or the baker's %, and the other is worked out. You can tick a line's Base while you're still typing the line. The empty row at the bottom shows no box until you start typing into it.
- **Absolute %**: with nothing ticked, each line is shown as its share of the whole batch. This figure is read-only.

The column heading changes to show which one you're looking at. Only the grams are ever used for costing.

---

## 2. Getting around the app

### The main window

- **Menu button (☰)**, at the top left, opens the navigation menu: **Raw Materials**, **Sub-Assemblies**, **Products**, **Reports**, **Production Plan** and **Price Simulator**. **Settings** is at the bottom of the menu. Click anywhere outside the menu to close it.
- **Tabs**: each screen opens in a tab along the top. The app starts with **Raw Materials**, **Sub-Assemblies** and **Products** open; open the others from the menu. A tab you close can be opened again from the menu. Each tab keeps its place, such as the record you were looking at, until you close the app. A tab with changes you haven't saved asks about them before you leave it (see [Editing and saving](#editing-and-saving)).
- **Back and Forward** (the arrows at the top right, next to the minimize button, or **Alt+Left** and **Alt+Right**) move between the screens and records you've looked at, like a web browser.
- Changes made on one tab show up on another tab the next time you switch to it.

### Lists as folder trees

The **Raw Materials**, **Sub-Assemblies** and **Products** lists are trees of folders, like File Explorer:

- **Search** at the top of the list narrows it to matching names and opens the folders that hold them.
- **Drag** one or more items onto a folder to move them there. A notice under the list says what moved, with an **Undo** button.
- **Right-click** an item, a folder or the empty space for a menu of what you can do there. Some menu items have keyboard shortcuts: **F2** renames and **Del** deletes.
- Folders you leave open or closed are the same the next time you use the app.

### Editing and saving

- **One Save saves the whole record**, including everything on its tabs. Until you press **Save**, nothing you type is stored.
- **Undo** (**Ctrl+Z**) and **Redo** (**Ctrl+Y**) step back and forth through the changes you've made since opening the record.
- **Revert**, on the Products screen only, puts the whole product back as it was last saved.
- **Pickers** (the boxes with a ˅ arrow, such as Category, Role or Existing Sub-Assembly or Packaging) can be typed into. Typing narrows the list to matching names and highlights the best match: the name you typed in full, or else the first name that starts with what you typed, or else the first that contains it. **Enter** or **Tab** picks the highlighted name, so "marg" and Enter picks Margarine. **Down** and **Up** move the highlight to the other names. Clicking or tabbing into a picker selects its text, so what you type replaces it. Click again to put the cursor where you click. A picker that can't be left empty, such as a raw material's **Type** or **Source**, shows its value again if you delete its text and move on.
- **New** starts a blank record. **Delete** asks before deleting anything.
- **Changes you haven't saved are never left behind without asking.** When a product, sub-assembly or raw material, new or saved, has changes you haven't saved and you go somewhere else, a message asks **Save the changes to …?** It asks when you go to another tab (or close the one showing), pick another item in the list, press **New**, click a **Used By** tile or another link, press **Back** or **Forward**, use **Clone** or **Create Variant**, or close the app.
  - **Yes** saves them, then goes on. If the save is refused, for example because a field is missing, you stay, and the red notice says why.
  - **No** throws them away, then goes on. If you were leaving the tab, the item is put back as it was last saved, so the changes aren't there when you come back.
  - **Cancel** keeps them, and you stay where you were.
  - Going between **Products** and the **Print** tab doesn't ask, since Print shows the product with your changes. Nor does **Copy** on the Sub-Assemblies screen, since the copy holds them.
- A green notice confirms a save. A red notice explains anything the app refused, such as a missing field, a name already taken, or something that can't be deleted because it's in use. It also says when the app couldn't reach its database, or the database didn't answer in time. Nothing is saved then, and the screen keeps working: once the database answers again, try again, or pick the record again.
- If something goes wrong that the app didn't expect, a message says what happened and that the app is still running, and the details go in the [log files](#14-troubleshooting-and-questions). Nothing you haven't saved is lost.

### Working in tables

- Most tables are typed straight into. Click a cell and type. The empty row at the bottom adds a new line.
- On that bottom row, **Tab** from its last cell adds the line, as **Enter** does, and opens the first cell of the next empty row, so you can type one line after another without reaching for the mouse.
- Pickers inside tables, such as the ingredient or material pickers, suggest names as you type.
- To change a line's ingredient, material or product, type over it. The line keeps what it had until you pick another, so its cost and the totals don't change while you type. Text that names nothing in the list leaves the line as it was, whether you press Esc, Tab, Enter or click away. To empty the cell, delete all its text.
- **Drag a row**, by its left edge or any cell you aren't typing in, to change the order of recipe lines, add-ins and packaging lines. The order is saved.
- **Insert Line** puts a blank line in above the selected one, so you don't have to add it at the bottom and drag it up. The new line's Ingredient cell opens, ready to type. With no line selected, or the empty row at the bottom selected, the new line goes in at the bottom. You can also press **Insert** while the table has the cursor and you aren't typing in a cell. It's on the ingredient tables: a sub-assembly's lines on the Sub-Assemblies screen, and a product section's lines and add-ins (the add-ins button is **Insert Add-In**). A line you leave blank does no harm, because Save leaves it out. **Undo** or **Remove Line** takes it away.
- **Remove Line** removes the selected line.
- Figures shown in grey are worked out by the app, so they can't be typed into.

### Vim keys

If you'd rather keep your hands on the keyboard, the app can be driven with keys like those of the Vim text editor. They're off until you turn them on in **Settings → Keyboard**, and the setting is only for you, on this computer.

With them on, the keys work in two modes. The mode shows at the top right of the window, before the Back arrow:

- **NORMAL**: letters move around the app and run commands. They don't type.
- **INSERT**: what you type goes into the field or table cell, as usual.

Clicking a field or cell, tabbing into it, or picking it by its label (**f**, below) puts you in INSERT, ready to type. **Esc** stops typing and goes back to NORMAL.

**Ctrl+[** does everything Esc does, as in Vim, so you don't have to reach for the Esc key. It works wherever Esc does: in a field or cell, on the labels, the command bar and the list of keys, and to close Settings or a small window.

While a picker's list is open, **Ctrl+N** and **Ctrl+P** move down and up it, like the Down and Up arrows. That covers the list under a field you're typing a name into and the one in a table cell. With no list open, they do nothing.

- In a table cell, Esc keeps what you typed. Without Vim keys, Esc in a cell throws the typing away.
- In a table, INSERT lasts from cell to cell. After **Tab**, **Enter** or an arrow takes you to the next cell, just type: there's no need to press **i** again. Tabbing into a table from a field you were typing in keeps INSERT too. Press **Esc** when you're done, and the letters move around the table again.
- In Settings, or a small window that asks for a name, the first Esc stops typing, and a second Esc closes the window.

**Moving around**

- **j** and **k**: down and up. In a list or table that's a row. In a form, it's the next or previous field, like Tab and Shift+Tab.
- **h** and **l**: left and right. In a table that's a cell. In the Raw Materials, Sub-Assemblies and Products lists, **h** closes a folder and **l** opens it. In a field, they move the cursor.
- **gg** and **G**: the first and last row.
- **0** and **$**: the first and last column of a table, or the start and end of a field.
- **J** and **K**: leave the list or table, for the control after it or before it.
- **gt** and **gT**: the next and previous tab.
- **H** and **L**: Back and Forward, like Alt+Left and Alt+Right.
- **/**: the search box of the list on screen.
- **f**: puts a small yellow label on everything you can click. Type a label to click its item. Esc takes the labels away.

**Typing and changing**

- **i** or **a**: start typing in the field or cell. **I** and **A** start at the beginning or end of a field. In a table, they start in the row's first or last column.
- **cc**: type over a field or cell. Its text is selected, so what you type replaces it.
- **o**: start a new line in a table, in the empty row at the bottom.
- **O**: start a new line above the one under the cursor, as **Insert Line** does. It works in the ingredient tables that have that button.
- **dd**: delete the table line under the cursor. In the Raw Materials and Products lists, it deletes the row, as the row's menu does, and asks first.
- **r**: rename the list row, as the row's menu does.
- **u**: undo. **Ctrl+R**: redo.

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

Type **:**, then a command, then **Enter**. A bar opens at the bottom of the window for it, and Esc closes it. Enter closes it too and runs the command. If the command can't run, for example **:w** when there's nothing to save, the bar says why. That message goes away at your next key or click, or after a few seconds.

- **:w** saves.
- **:new** starts a new one.
- **:delete** deletes the one open, asking first.
- **:e** and a name opens the one whose name matches. For example, **:e pandesal** opens Pandesal on the Products screen.
- **:revert** throws away a product's changes since the last Save.
- **:raw**, **:sub**, **:products**, **:reports**, **:plan**, **:simulator** and **:print** switch screens.
- **:settings** opens Settings.
- **:help** lists every key.

A command can be shortened as long as it stays clear, as in **:del**, **:pro** and **:rep**.

**?** shows the keys for wherever you are. **Esc**, **q** or **?** closes the list.

---

## 3. First-time setup

On a new database, set things up in this order. Each step uses what the one before it made.

1. **Units of measure** (Settings → Units of Measure): the units you buy and use things in: g, kg, piece, pack, bag, cm, roll, and so on.
2. **Material categories** (Settings → Material Categories): how raw materials are grouped, such as Flour, Dairy, In-House, Boxes and Ribbons. Each category is for ingredients, made ingredients or packaging.
3. **Product categories** (Settings → Product Categories): how products are grouped, such as Breads, Cakes and Pastries.
4. **Roles** (Settings → Roles): the parts of a recipe. Dough, Filling, Topping, Inclusion, Finishing and Packaging are there from the start.
5. **Costing defaults** (Settings → Costing Defaults): the stage a new product goes in, the labour, overhead, administrative, markup and VAT percentages that it starts with, the cost buffer a new raw material starts with, and the shop's wastage.
6. **Raw materials**: every purchased ingredient and packaging material with its prices, then the linked ingredients.
7. **Sub-assemblies**: the doughs, fillings and packaging sets shared by more than one product, and the recipes of the shop's made ingredients, such as a syrup or an icing.
8. **Made ingredients**, on the Raw Materials screen, each made from one of those sub-assemblies.
9. **Products**.

You don't have to finish each list before moving on. You can add a category or unit later, and most pickers let you add a new category just by typing its name.

---

## 4. Settings

Open **Settings** from the bottom of the navigation menu. It has seven tabs. Each list tab works the same way: pick a row to edit it, or press **New**. Then fill in the form and press **Save**, or press **Delete**. Press **Close**, the **✕** at its top right, or **Esc** to close Settings. The button beside the ✕ makes the window fill the screen, and pressing it again returns the window to its normal size. You can also drag the title bar to move the window.

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

Each category has a **Name**. Products don't need a category, and those without one are listed under **Uncategorized**. Deleting a category leaves its products uncategorized.

### Roles

Each role has:

- **Name**: what the section is called in a recipe, such as Dough or Filling.
- **Order**: where the role's sections go in a recipe. Lower numbers come first. A new role goes last. To change a role's place, pick it in the list and press **Move Up** or **Move Down** above the list. The roles are numbered 1, 2, 3… again after a move or a delete, so there are no gaps.
- **Product-owned**: a section made in a product in this role stays that product's own. It's edited in the product's recipe instead of on the Sub-Assemblies screen, and other products can copy it but not link it, except its variants. Turning this on or off for a role that's already used asks first, and says how many sub-assemblies will move.
  - These are also the only roles a composite product can have sections in. So Product-owned can't be turned off while a composite has a section in the role; the message names the composites. Take the section out there first.
  - A section you make in a product in this role starts at a Yield of 1, so its Weight/piece is the whole batch (see [Piece size](#piece-size-weight-per-piece-and-yield)).

The **Packaging** role is special. Sub-assemblies in it are packaging sets. It can be renamed but not deleted, and it can't be product-owned.

A role can't be deleted while any sub-assembly uses it.

### Costing Defaults

**New Products In** is where **New** on the Products screen files a product when you save it: **Production** (the default) or **Research and Development**. **New Product Here** still files it in the folder you picked it on, and a clone or variant still goes in Research and Development.

Under it are the five percentages a **new** product's costing starts with:

- **Labor %**, **Overhead %** and **Administrative %** (office, permits, bookkeeping): each is a percentage of the product's sub-assemblies (ingredients) cost.
- **Markup %**: added on the base cost (materials, wastage, labour, overhead and administrative).
- **VAT %**: added on top of the marked-up price.

Under them, **Wastage %** is the shop's [wastage](#wastage): a percentage of every product's sub-assemblies (ingredients) cost, added to its costing. It starts at 3%, and can be anything from 0 to 100. Unlike the other figures here, it isn't where a new product starts: **every** product is priced with it, saved ones included, so changing it changes every product's costs and suggested price.

**Price Rounding** rounds a suggested price up to a price you'd charge: up to the next 0.25, 0.50, 1, 5 or 10, or **None** (the default). It's always rounded up, so the rounded price still earns at least the markup. The exact figures don't change: the rounded price is shown beside them, under the **Total Selling Price** on a product's Costing tab (*Rounded up to the next 0.25: 43.25*), in the **Rounded Price** column of Reports' Product Cost Summary, and in the Price Simulator.

**Cost Buffer %** is what a new raw material's [cost buffer](#cost-buffer) starts with on the Raw Materials screen. It starts at 10%, and can be anything from 0 to 100. It only matters for a purchased material: a linked ingredient takes the buffer of the one it's linked to, and a made ingredient has none of its own.

Each product keeps its own stage and percentages, and each raw material its own cost buffer, from its first Save. Changing those defaults doesn't change anything saved. Changing the Wastage % does: it reprices every product. A new product you haven't saved yet moves to the new stage, since you didn't pick one for it. A new raw material you haven't saved moves to the new cost buffer, unless you've typed one.

**Stored Costs**, to the right of the defaults, has a **Recalculate Costs** button. The app keeps every made and linked ingredient's cost, and every sub-assembly's total weight and cost, up to date as you save. You only need this button if the database was changed outside the app, for example by a script. It works all of those costs out again from what they're made of, and a green notice says how many were out of date. Purchased materials keep their prices.

### Keyboard

**Vim keys** turns on the keys described in [Vim keys](#vim-keys). The change takes effect at once.

The setting is kept for you on this computer, not in the bakeshop's data. Turning it on doesn't change the app for anyone else, or for you on another computer.

### Database

Backups of all of the app's data: every raw material, price, sub-assembly, product and setting.

- **Folder** shows where the backups are kept, *C:\ProgramData\TinapayanRMS\Backups*. **Open Folder** shows it in File Explorer. Everyone who uses the app on this computer shares the folder.
- The list shows every backup, newest first: when it was **Taken**, its **Kind** and its **Size (MB)**.
  - **Manual**: taken with **Back Up Now**.
  - **Automatic**: the app takes one by itself the first time it's opened each day. The newest 14 are kept, and older automatic ones are deleted.
  - **Before restore**: the safety backup a restore takes of the data it replaces.
  - **Copied in**: a backup brought in with **Restore from File…**.
  - Only automatic backups are ever deleted by the app. Delete others in File Explorer when you no longer need them.
- **Back Up Now** takes a backup straight away.
- **Copy to…** copies the backup picked in the list somewhere else, such as a USB stick. Do this now and then: backups kept only on this computer are lost with it if it breaks.
- **Restore** puts the backup picked in the list back, in place of all of the app's data. It asks first, saying when the backup was taken. Then it takes a safety backup of the data as it is, puts the backup back, and the app closes and opens again. Anything saved since the backup was taken is lost, and so is anything not yet saved on another screen.
  - A backup from an earlier version of TinapayanRMS is brought up to date as it's restored.
  - A backup from a later version is refused, and nothing changes. Update the app first.
  - A backup is checked and brought up to date before it takes the data's place. If it's refused, or anything goes wrong part-way, the data stays as it was.
- **Restore from File…** restores a backup file from elsewhere, such as a USB stick or another computer. Pick the file, and it's copied into the folder and restored as above.

A backup or a restore can take a little while. The tab says what it's doing until it's done.

Backups are *.backup* files. The *.bak* files the app made before 21 October 2026, when it kept its data in SQL Server, aren't listed and can't be restored. Everything they held was carried over into the app's data on that day.

---

## 5. Raw Materials

The Raw Materials screen has the list of materials on the left, and the selected material's details on the right.

### The list

The list has two top-level folders, **Ingredients** and **Packaging**, with a folder for each category inside them. Made categories sit inside **Ingredients**, among the ingredient ones. Every material is in a category.

Right-click menus:

- **A material**: **Move to** (another category of the same type: a made ingredient's are the Made ones), **Rename…** (F2) and **Delete** (Del).
- **A type's folder**: **New Ingredient Here** or **New Packaging Material Here**, **New Category…**, **New Made Category…** (Ingredients only), **Expand All** and **Collapse All**.
- **A category's folder**: **New … Here**, **New Category…**, **Rename Category…** (F2) and **Delete Category** (Del). In a Made category's folder, **New Made Ingredient Here** starts a made ingredient in that category, and **New Category…** adds another Made category. You can only delete a category when it's empty. Otherwise the menu shows how many materials it holds.
- **The empty space**: **New Raw Material**, **New Ingredient Category…**, **New Made Category…**, **New Packaging Category…**, **Expand All** and **Collapse All**.

The same commands are buttons under the search box: **New Raw Material**, **New Category** (its arrow offers an ingredient, a Made or a packaging category), **Expand All** and **Collapse All**. Each button shows only its icon; point at it to see its name.

Dragging materials onto another category moves them straight away. A material can't be dragged into a category of another type, so a made ingredient only drops into a Made category, and nothing else does.

### The header

At the top are the material's name and **New**, **Save**, **Undo**, **Redo** and **Delete**, then these figures:

- **Base Unit**: *gram (g)* for an ingredient.
- **Current Cost per …**: the true cost. For a purchased material it's the dearest brand's price, per the unit it's shown in.
- **Buffered Cost per …**: the cost with the buffer on top. This is the figure recipes use.
- **Total Weight** and **Total Cost**, for a made ingredient only: what its batch yields after weight loss, and what that batch costs.
- **Last Updated**: when the material was last saved, and when its cost last changed.

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
| **Buffer %** | Read-only. How far the buffered cost is above this brand's price. |

The material's **current cost** comes from the **dearest** brand: its price divided by the base units in its purchase unit. For example, a 25 kg bag at 1,250.00 costs 1,250 ÷ 25,000 = 0.05 per gram.

<a id="cost-buffer"></a>**Cost buffer.** Under the brand table are **Cost Buffer %**, **Buffered Cost** and **Per**:

- A new material's **Cost Buffer %** starts at the default in Settings → Costing Defaults, 10% unless you change it there. Set it to 0 for no buffer.
- Typing the **Cost Buffer %** works out the **Buffered Cost** as the dearest brand plus that percentage.
- Typing the **Buffered Cost** works out the percentage.
- **Per** chooses the unit the buffered cost is shown and typed in, such as per gram or per bag. It's always stored per base unit.

The buffered cost stays **fixed** when brand prices change. The percentage then follows, showing how far the fixed amount now sits above the dearest price. If the dearest brand goes above the buffered cost, a warning says so. **To put the buffer back on top of a new price, type the Cost Buffer % again**, then Save.

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

### Used By tab

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

### The list

The list has a folder for each role, in the order set in Settings → Roles, with the sub-assemblies in that role inside it, by name. Each folder shows how many it holds. All the folders start open. When a product saves a new section, it's saved here under a name made from the product and the role, such as *Cheese Roll Dough*. If the section has its own name, that's added after them, as in *Cheese Roll Filling: Ube*.

Right-click menus:

- **A sub-assembly**: **Move to** (another role), **Rename…** (F2), **Make In-House Ingredient…** and **Delete** (Del). A packaging set has no **Move to** or **Make In-House Ingredient…**: it always stays in the Packaging role.
- **A role's folder**: **New Sub-Assembly Here** (**New Packaging Set Here** on the Packaging role), **New Role…**, **Rename Role…** (F2), **Delete Role** (Del), **Expand All** and **Collapse All**. You can only delete a role when no sub-assembly here is in it. Otherwise the menu shows how many it holds. The Packaging role can't be deleted.
- **The empty space**: **New Sub-Assembly**, **New Role…**, **Expand All** and **Collapse All**.

The same commands are buttons under the search box. Each button shows only its icon; point at it to see its name.

Dragging sub-assemblies onto another role's folder changes their role straight away, and every product that links them follows. A recipe can't go into the Packaging role, and a packaging set can't leave it. A move is refused, with a message saying why, when a product that links the sub-assembly would end up with two sections in that role it can't tell apart, or when a composite product links it and the new role isn't product-owned.

**Rename…** saves the new name straight away, and so does a move. If the sub-assembly is open in the form, its name or role there changes too, and anything else you've changed stays to be saved.

A new role from the list goes after the others and isn't product-owned. Change its place and **Product-owned** in Settings → Roles.

#### Make In-House Ingredient

**Make In-House Ingredient…** adds a made ingredient on the Raw Materials screen that's made from this recipe, so other recipes can use it as one ingredient. It asks for the ingredient's **Name**, which starts as the sub-assembly's name, and its **Category**, one of the Made categories, starting on **In-House** when there is one. If there's no Made category yet, add one on the Raw Materials screen first, with **New Made Category…**. The name can't be one another raw material already has. Press **OK** and it's saved straight away:

- The new made ingredient is made from this sub-assembly, with no add-ins and a **Weight Loss %** of 0, so it costs per gram what the recipe does.
- The sub-assembly stays here exactly as it is, and you still change its lines here. The products that link it don't change.
- If the recipe really does lose weight while it's made, set its Weight Loss % on the Raw Materials screen afterwards. Every product that uses the made ingredient then follows.

If the sub-assembly is open in the form with changes you haven't saved, save or undo them first: the made ingredient is made from it as it's saved. To remove the made ingredient again, delete it on the Raw Materials screen.


### The form

The form has two cards. The header at the top holds the name and **New**, **Copy**, **New Product**, **Save**, **Undo**, **Redo** and **Delete**, then:

- **Batch Weight**, **Batch Cost** and **Cost per g**, worked out from the lines as you type.
- **Name** and **Role**.

The card under it has two tabs along its bottom. Each scrolls while the header stays put:

- **Recipe**: the **Lines**, with **Ingredient**, **Grams**, the Baker's % or Absolute %, **Cost/g**, **Cost** and **Base** (tick it for baker's percentages).
- **Used By**: a tile for each product that links this sub-assembly. Click one to open it.

**Scale**, under the lines, resizes the recipe. Pick **the whole batch** or **the base (ticked lines)**, type the grams it should weigh, and press **Scale**: every line is multiplied by the same amount, so each keeps its percentage. For example, a 1,200 g batch scaled to 2,400 g doubles every line, and a dough whose ticked flour is 1,000 g, scaled on the base to 500 g, halves. It's one step for **Undo**, and **Save** keeps it. Scaling the base needs at least one line's **Base** ticked. It isn't offered for a packaging set.

**Copy** starts a new, unsaved sub-assembly with the same role and lines, named "… Copy". Rename it if you like, then press Save. The original stays as it was.

**New Product** starts a new product from the sub-assembly picked in the list. The Products screen opens with a new, unsaved product named after the sub-assembly, and its recipe already **links** the sub-assembly. Type the piece size (and rename the product if you like), then press Save there. New Product isn't offered for a packaging set. If the sub-assembly has changes you haven't saved, save or undo them first, because the product links the sub-assembly as it's saved.

Changing a sub-assembly here changes every product that links it, and every made ingredient made from it. A sub-assembly that a product links, or that a made ingredient is made from, **can't be deleted**. The message names them.

The **Used By** tab shows a tile for each product that links the sub-assembly, then one for each made ingredient made from it. Click a product's tile to open it on the Products screen, and a made ingredient's to open it on the Raw Materials screen. Saving lines here refuses a line that's a made ingredient made from this sub-assembly, directly or further down, because that ingredient would contain itself.

### Packaging sets

Pick the **Packaging** role to make a packaging set. The lines table then holds packaging materials instead: **Material**, **Qty**, **Unit**, **Unit Cost** and **Cost per Set**, with a **Set Cost** total.

A product whose packaging links a set takes one whole set per sellable unit. For example, *Ensaymada Box of 8* can link a set holding 8 bags and 1 box.

A sub-assembly can't be switched between a recipe role and the Packaging role once it's saved.

---

## 7. Products

The Products screen has the product list on the left. On the right are the selected product's header, its details, and five tabs: **Recipe**, **Costing**, **Scaling**, **Clone** and **History**.

### The list

The top-level folders are **Production** and **Research and Development**. Each has a folder for every product category, plus **Uncategorized**. Drag products between folders to change their stage or category.

Below them is the **Deleted** folder 🗑, where deleted products wait, by name, until you restore them or delete them permanently. Clicking a deleted product doesn't open it. It can't be dragged, and nothing can be dropped on the folder.

Right-click menus:

- **A product**:
  - **Promote to Production** or **Send Back to Research and Development**
  - **Move to** (any stage and category)
  - **Rename…** (F2)
  - **Clone…** and **Create Variant…** (both ask for the new product's name)
  - **Delete** (Del)

  With several products picked, the menu offers to promote or move them all.
- **A stage's folder**: **New Product Here**, **New Composite Product Here**, **New Category…**, **Print Stage**, **Expand All** and **Collapse All**.
- **A category's folder**: **New Product Here**, **New Composite Product Here**, **Promote All to Production**, **Print Folder**, **New Category…**, **Rename Category…** and **Delete Category**.
- **The empty space**: **New Product**, **New Composite Product**, **New Category…**, **Print All Products**, **Expand All** and **Collapse All**. These are also buttons under the search box. Each button shows only its icon; point at it to see its name.
- **A product in the Deleted folder**: **Restore** and **Delete Permanently** (Del).

**New Product Here** and **New Composite Product Here** file the new product in that folder when you save it. **New** from the header puts it in the stage set in Settings → Costing Defaults, Production unless you change it.

A ⚠ after a product's name means its **Current Price** earns less than the **Markup Metric** on its Costing tab. The marks are worked out from the products as saved, a moment after the list loads, so a product you've just saved shows its new mark the next time the Products tab opens.

Each product's name starts with a small full-colour picture of what it is: a round bread for **Breads**, a loaf for **Loaves**, a strawberry cake for **Cakes**, a cupcake for **Pastries** and a roll cake for **Rolls**. A composite product always has a bag of breads, whatever its category. A product in any other category, or in none, has no picture.

### The header

The header has the product's name and **New**, **Save**, **Undo**, **Redo**, **Revert**, **Print**, **Unbuffered** and **Delete**. **New** starts a product. The arrow beside it also offers **New composite product**. Below them are these figures, all **per sellable unit**:

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

**Unbuffered** switches every cost on the Products screen to the materials' true costs, without their cost buffers. That covers the figures above, the Cost/g and cost columns, the batch cost and cost per piece, packaging, the Costing tab, the recipe cost of a composite product's products, and the cost column in the ingredient and packaging pickers. While it's on, the button reads **Buffered** and an **Unbuffered costs** badge shows beside the product's name. Press **Buffered** to go back to buffered costs.

- It only changes what you see. Nothing is saved, **Save** and **Undo** aren't affected, and **Reports** still use buffered costs.
- It stays on when you open another product, until you switch it off or close the app. The app always starts on buffered costs.
- **Print** uses whichever costs the screen shows (see [Printing](#8-printing-recipes-and-costings)).

Deleting a product asks first, then moves it to the **Deleted** folder at the bottom of the list. Nothing of it is lost there: its recipe, packaging, costing, stage and category are all kept. Until you restore it, no picker, report, print, Price Simulator or Production Plan includes it.

- **Restore** (right-click it in the Deleted folder) puts it back in its folder and opens it. If its category was deleted in the meantime, it comes back in **Uncategorized**.
- **Delete Permanently** asks first and can't be undone. The product's own sub-assemblies are deleted with it. Its linked sub-assemblies stay on the Sub-Assemblies screen. Deleting a composite product leaves the products it holds as they are.
- A deleted product **still holds everything it uses**, so it can come back whole. A sub-assembly it links, an ingredient in its recipe, or a product it holds or whose packaging it links can't be deleted until you delete the deleted product permanently. **Used By** tiles and refusal messages name it with "(deleted)" after its name. Clicking its tile picks it in the Deleted folder.
- A deleted product keeps its name, so no other product can take that name until it's deleted permanently.
- A product can't be deleted, to the Deleted folder or permanently, while another product's packaging links it, or while a composite product holds it. That includes a deleted product.

### Recipe tab: Recipe Structure

The recipe has a tab for every section: each sub-assembly, named by its role, then **Packaging** when the product has packaging. A new product's recipe is empty.

- **Add Sub-Assembly** opens a menu with three choices:
  - **New sub-assembly**: type its lines in here.
  - **Link an existing one**: use a sub-assembly from the Sub-Assemblies screen, a packaging set, or another product's packaging. It follows every change made to it where it's kept.
  - **Copy an existing one**: start from an existing sub-assembly's lines and role, including another product's own. The copy is a new sub-assembly.
- **Delete Sub-Assembly** takes the section showing out of the recipe when you Save. A linked sub-assembly stays on the Sub-Assemblies screen.

#### A section's form

Each section's tab shows its form:

- **Role**, and an optional **Name**. The name tells apart two sections in the same role, such as *Filling: Ube* and *Filling: Cheese*.
- **Existing Sub-Assembly or Packaging** picker, when you're linking or copying.
- **The lines table**: **Ingredient**, **Grams**, the Baker's % or Absolute %, **Cost/g**, **Cost** and **Base**. Under it are **Insert Line**, **Remove Line** and the section's cost per piece.
- **Batch parameters**:
  - **Weight/piece (g)** and **Yield (pcs)**: type either one
  - **Total weight**: the batch, including add-ins, which the pieces share out
- **Cost/piece** and **Batch cost** tiles.

You don't need a piece size to add a section or to save it. Its cost per piece reads 0 until you enter one. A new or copied section in a product-owned role already has one: a Yield of 1 (see [Piece size](#piece-size-weight-per-piece-and-yield)).

A section's changes go into the recipe when you leave its tab, or when you press Save.

#### Linked sections

A linked section has a badge saying where it's from. Click the badge to open the sub-assembly on its own screen. The pencil beside the badge lets you **link another sub-assembly in its place**, keeping this product's piece size and add-ins.

- **Editing the lines** of a linked section changes the sub-assembly itself. Save asks first when other products also use it, because their recipes and costs change too.
- **The Add-Ins table** under the linked recipe's lines holds this product's own extras on top of the linked recipe, such as a colouring, a flavouring or an inclusion. Add-ins go into the batch but don't change the shared sub-assembly. An ingredient the linked recipe already has can't be added as an add-in.
- **The add-ins badge**, over the top right corner of the linked recipe's lines, level with the badge saying where the section is from, tells you whether the product has add-ins: it reads **No add-ins**, or the count, such as **2 add-ins**. When there are some, click the badge (or Tab to it and press Enter) to jump down to the Add-Ins table. The **Go to top** badge over the Add-Ins table's top right corner takes you back up to the linked recipe's lines.
- **Copy** makes a copy for this product alone. Save keeps the copy named after this product and uses it instead. In a product-owned role the copy is the product's own. Otherwise it's saved on the Sub-Assemblies screen and linked.
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

To add packaging, use **Add Sub-Assembly** and pick the **Packaging** role:

- With **New sub-assembly**, type how much of each material one sellable unit takes. The lines go on the **Packaging** tab.
- With **Link an existing one**, pick a **packaging set** or another product's packaging. The Packaging tab then shows the linked lines, and **Make a Copy** turns them into this product's own lines so you can change them.

The Packaging tab's table is typed into. Its columns are **Material**, **Qty**, **Unit** (the base unit or one of the material's alternative units), **Unit Cost** and **Cost per Unit**.

**Move to Sub-Assemblies**, at the top right of the Packaging tab while the packaging is this product's own lines, works as it does for the product's own sections. Save puts the lines on the Sub-Assemblies screen as a packaging set named after the product, such as *Ensaymada Packaging*, and this product links it there. The cost doesn't change. Until you save, a **Moves to Sub-Assemblies on Save** badge shows and you can still change the lines; Undo or Revert keeps them the product's own. Afterwards other products can link the set too, its lines are changed on the Sub-Assemblies screen, and **Make a Copy** makes them this product's own again.

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

**Add Sub-Assembly** works as it does for any product, but a composite is offered only the **product-owned** roles and **Packaging**. A section's piece size is for one whole composite: a Topping with a **Weight/piece** of 100 g puts 100 g on each box. **Delete Sub-Assembly** takes the section showing out, or the packaging on the Packaging tab.

Save needs at least **two different products** in a composite.

### Costing tab

On the left are the product's four metrics, **Labor Metric**, **Overhead Metric**, **Admin Metric** and **Markup Metric**, then **VAT %**. A new product starts with the Costing Defaults.

Each metric has two boxes. The first is its percentage, shown to one decimal with its % sign. The second is what that percentage comes to in pesos for one sellable unit, shown to two decimals. Type in either box and the other one follows. For example, with ₱20.00 of sub-assemblies, typing **5.00** in Labor Metric's peso box sets its percentage to **25.0%**.

- Labor, Overhead and Admin are a percentage of the **sub-assemblies (ingredients) cost**.
- Markup is a percentage of the **base cost**.
- Only the percentage is saved. If the recipe's cost changes later, the percentage stays and the peso amount changes with it.
- A product with nothing in its recipe yet has no cost to take a percentage of, so its peso boxes stay at 0.00. Type the percentage instead.

Under each metric is its share of the price. VAT has a percentage box only, with its amount in pesos and its share of the price under it.

On the right, the **Costing Summary** lists each component with its amount and its **% of Price**:

- **Sub-assemblies (ingredients)**, with each sub-assembly listed under it. For a composite product this row is **Products (ingredients)**, with each product and its quantity listed under it, such as *Ensaymada × 6*. When the composite has sections of its own, the row is **Products and sub-assemblies (ingredients)** and lists them after the products. Its Labor, Overhead and Admin are a percentage of that row.
- **Wastage**, labelled with the shop's [Wastage %](#wastage): a percentage of the row above. It's set in Settings → Costing Defaults, not here.
- **Packaging**. For a composite product that includes its products' packaging, this row is **Packaging (own and products')**.
- **Labor**, **Overhead** and **Administrative**, each labelled with its percentage
- **Subtotal (base cost)**
- **Markup**
- **VAT**
- **Total Selling Price**

Everything updates as you type, before you save. See [How costs are worked out](#12-how-costs-are-worked-out).

### Scaling tab

The Scaling tab works out how much of everything a production run takes. Type how many to make in **Sellable Units**: boxes for a product like *Ensaymada Box of 8*, pieces for a product sold by the piece. When a unit holds more than one piece, the pieces it comes to show beside the box, such as **= 160 pieces**. The number isn't saved, and it stays when you open another product.

The table below it lists, with each row's **Amount** and **Cost**:

- **Each section**, such as *Dough*, made in **whole batches**. Beside its name is how many batches that is and how many pieces they make, with the pieces to spare: *2 batches · 200 pcs (40 extra)*. Its amount is the weight of those batches. Under it are its ingredients, then its add-ins, each for that many batches. A section with no piece size yet has nothing to scale, and says so.
- **A made ingredient** opens into what it's made from, for the grams the recipe takes, allowing for its weight loss. A made ingredient inside it opens in turn.
- **Packaging**: each material for that many sellable units, in the unit it's typed in on the Packaging tab.
- **A composite product** lists each of its products first, such as *Ensaymada × 6*, with how many of it that comes to. Under each is that product's own recipe scaled the same way, as it's saved. Its packaging is included only when the composite includes its products' packaging. The composite's own sections and packaging come after its products.

**Total Cost**, at the top right, adds up the table. Because each section is made in whole batches, it includes the extra pieces, so it can be more than the Costing tab's figures times the number of units. Costs follow **Unbuffered**, and the table follows changes to the recipe, packaging and Quantity Required as you make them, before you save.

**From What You Have** works the other way round: how many you can make from what there is of one ingredient. Pick the ingredient (any in the recipe, including what its made ingredients are made of), type how much there is, and pick its unit: grams, or one of the ingredient's own units, such as a kilogram or a bag. The tab says how many sellable units that makes and how much of it they use, such as *Makes 168 pcs, using 5,000 g of the 5,000 g*, or that there isn't enough for one. Because sections are made in whole batches, it's the most units whose batches the amount covers. Press **Scale to 168** to put that number in Sellable Units. Nothing here is saved.

Press **Print** while this tab is showing to print the scaled recipe instead of the recipe and costing. See [Printing](#8-printing-recipes-and-costings).

### Clone tab

The Clone tab is shown for saved products only. Type a **New Product Name**, then choose:

- **Clone**: the new product links the same sub-assemblies from the Sub-Assemblies screen at the same piece sizes, gets its **own copy** of this product's own sub-assemblies, and gets its **own copy of the packaging**.
- **Create Variant**: the new product **links every sub-assembly this product uses** at the same piece sizes: the ones it links from the Sub-Assemblies screen (a dough, sponge or batter) and this product's own (a filling, topping and so on). So it follows every change made to them. It gets **no packaging**, neither linked nor copied. Use this for a smaller or bigger version of the same product, then give it its own packaging.

A section that's this product's own shows in the variant with a badge such as "Ensaymada Big Filling, from Ensaymada Big". Click it to open the product the section belongs to. You can change the section's lines in the variant too: Save writes them to that product's sub-assembly. **Make a Copy** gives the variant its own copy instead. Once a variant drops a linked section, it can't link it again.

While a variant links one of this product's own sub-assemblies, this product can't remove that sub-assembly and can't be deleted. Make a copy of it in the variant, or remove it there, first.

The category, Quantity Required, Current Price and costing percentages are copied with both. The new product starts in Research and Development, and you can change its piece sizes. Its History tab starts with one version that says where it came from (see [History tab](#history-tab)).

A composite product's clone or variant is a composite too, holding the same products at the same quantities. A clone copies its own sections and a variant links them, as for any product.

### History tab

The History tab keeps a product's earlier versions, so you can see how its recipe and price changed and go back to an old one. It's shown for saved products only.

**When a version is recorded.** Each **Save** that changes the product's recipe, packaging, costing percentages, Quantity Required or Current Price records a version. A Save that only renames the product, or changes its description or category, doesn't. Nor does a change in what its materials cost: that's what the **Today** columns show. The first time you save a product that has no versions yet, two are recorded: how it was before ("Before version history began") and how it is now.

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
- A linked sub-assembly that has been deleted since comes back as a new section with the old lines.
- Packaging links what it linked, if that still exists. Otherwise the lines it showed come back as the product's own.
- If a material, role or product the version needs has been deleted, Restore refuses and names it, and nothing in the form changes.

A product moved to the Deleted folder keeps its history. **Delete Permanently** deletes the history with it.

---

## 8. Printing recipes and costings

Press **Print** in the Products header to open the **Print** tab. It shows a preview of the product's recipe and costing as the screen shows them, including changes you haven't saved yet. On the Scaling tab, it prints the scaled recipe instead (see [Printing a scaled recipe](#printing-a-scaled-recipe)). On the History tab, it prints the version picked there, at its costs then (see [History tab](#history-tab)).

- **Include**: tick **Recipe**, **Costing** or both. Without the costing, the recipe prints with no costs, which is useful for the kitchen. A composite product's recipe prints as its products table, then its own sections.
- **Print**: the drop-down chooses **This product**, the folder last asked for, or **All products**.
- The preview's own controls set the printer, paper, orientation and margins.

To print a set of products, right-click in the Products list and choose **Print Folder**, **Print Stage** or **Print All Products**.

A set prints each product **as saved**, with a contents page and each product starting on a new page. If the product open on the Products screen has unsaved changes, the Print tab warns you that it's printed as saved.

The footer on each page shows the product, when it was printed, whether it includes unsaved changes, and the page number.

While the Products screen shows unbuffered costs, everything printed, a single product or a set, is at the materials' true costs. Each product's page says **Unbuffered costs** under its title and over its costing.

Close the Print tab with its ✕.

A production plan prints from the **Production Plan** screen's own **Print**: see [Production Plan](#11-production-plan). A report of what price changes would do prints from the **Price Simulator**: see [Printing a Price Simulator report](#printing-a-price-simulator-report).

### Printing a scaled recipe

If the Products screen is showing the [Scaling tab](#scaling-tab) when you press **Print**, the Print tab shows the **scaled recipe** instead of the recipe and costing. It has the **Sellable Units**, the pieces they come to and the **Total Cost**, then the tab's table: each section in whole batches, with its ingredients indented under it, made ingredients opened into what they're made from, and the packaging. Each row has its **Amount** and **Cost**.

**Include** then has a single tick box, **Costs**. Clear it to print only the amounts, for example for the kitchen.

The Print tab always follows the Products screen. If you go back to Products, change to another tab and show the Print tab again, it shows the recipe and costing again.

---

## 9. Reports

Reports has two tabs along the bottom. Each is a table you can read but not edit. Click a column's heading to sort by it, and click it again to reverse the order. The figures are worked out again each time you open Reports, so they always use today's prices.

### Product Cost Summary

One row for every product, as it's saved:

- **Product**, its **Stage** (Production or Research and Development) and its **Category**.
- **Pieces**: how many pieces make one sellable unit (1 for a product sold by the piece). Every amount after it is for one sellable unit. Blank for a composite product, which is one unit made of other products.
- **Ingredients Cost**: the recipe's cost, the **Sub-assemblies (ingredients)** row of the product's Costing tab. For a composite product, it's what its products bring (and its own sub-assemblies).
- **Wastage Cost**: the Costing tab's **Wastage** row, the shop's Wastage % of the ingredients cost.
- **Packaging Cost**: the **Packaging** row of the Costing tab. For a composite product that includes its products' packaging, theirs is counted too.
- **Labor Cost**, **Overhead Cost** and **Admin Cost**: the Costing tab's Labor, Overhead and Admin metrics in pesos, each a percentage of the ingredients cost.
- **Base Cost** and **Suggested Price**: as the product's Costing tab works them out.
- **Rounded Price**: the suggested price rounded up as Settings → Costing Defaults → **Price Rounding** says; the same as the suggested price with no rounding set. The base cost is the six costs before it added up, and the suggested price is the Costing tab's **Total Selling Price**.
- **Current Price**: what the shop charges now, as entered on the product. Blank if none is entered.
- **Markup %**: what the current price earns on the base cost, as the product's header shows it. Blank without a current price.
- **Target Markup %**: the **Markup Metric** on the product's Costing tab, what you're aiming for.
- **Short By**: how many percentage points **Markup %** is under **Target Markup %**. Blank when it isn't short, or there's no current price.

Sort by **Markup %** to see which products earn the least, or by **Short By** to see which fall furthest short of what you're aiming for.

To show only some products, click the funnel in the **Product**, **Stage** or **Category** heading. Untick what you don't want to see, or untick **(Select All)** and tick only what you do, then click **OK**. The search box at the top finds a name in a long list. A funnel with a dot shows that its column is filtered, and filters in more than one column work together: **Stage** set to Production and **Category** set to Breads shows only the breads in production. **Clear Filter** at the top of the list shows everything in that column again. The filters stay as you set them, even after you close the app: Reports opens with them the next time.

To choose which columns show, click **Columns** at the top right and untick the ones you don't need, such as **Labor Cost**, **Overhead Cost** and **Admin Cost**. Tick one again to bring it back. The list stays open while you tick, and closes when you click anywhere else. **Product** is always shown, so it isn't in the list. A hidden column's filter still applies: you can filter **Stage** to Production and then hide the Stage column. Like the filters, your choice of columns, and the column you sorted by, are kept for the next time you open the app. They're kept for you on this computer.

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

### Exporting to Excel

Each tab has **Export to Excel** at its top right. It saves the table to an Excel workbook (an .xlsx file) exactly as it's shown:

- in the order you sorted it;
- with only the rows your filters leave;
- with only the columns showing (Product Cost Summary's **Columns** list decides which).

Choose where to save it and press **Save**. The name offered is the report's and today's date, such as *Product Cost Summary 2026-10-03.xlsx*. A notice then says where the file went.

The figures are real numbers in Excel, kept to every decimal the app works with, and shown to 2 decimal places as on the screen. So you can add them up or chart them in Excel.

If a file of that name is open in Excel, the app can't replace it. Close it in Excel first, or save under another name.

---

## 10. Price Simulator

The **Price Simulator** shows what raw material price changes would do to every product, before they happen. It never changes a real price, and nothing else in the app changes. The changes you try last until you close the app, unless you save them as a scenario.

### Price Changes

The top table holds the changes to try, one row per raw material:

- **Raw Material**: pick a purchased ingredient or packaging material. Type to search the list.
- **Per** and **Current Price**: the unit it's bought in and what one costs now. These come from its dearest brand on **Cost Setup**, which is the price its cost comes from. A material with no price yet shows its base unit and no price.
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
- **Suggested Price** and **Simulated Suggested Price**: the Costing tab's Total Selling Price.
- **Current Price**: what you charge now, VAT included, as entered on **Products**. Blank when none is entered.
- **Markup %** and **Simulated Markup %**: what the product's current price earns, now and with the changes. Blank for a product with no current price.
- **Price to Keep Markup**: the price that earns today's **Markup %** with the changes. It moves with the base cost: a base cost up 5% gives a price 5% higher. A product no change reaches keeps its current price. Blank for a product with no current price. The figure isn't rounded.
- **Price to Charge**: **Price to Keep Markup** rounded up as Settings → Costing Defaults → **Price Rounding** says, for a product a change reaches; the current price for one no change reaches. With no rounding set it's the same as Price to Keep Markup.

**Set Current Prices** puts the new prices on the products: it sets each product's **Current Price** to its **Price to Charge**. It takes the products you've picked in the table (click one, or several with **Ctrl** or **Shift**), or with none picked, every product the table shows. Only products a change reaches and whose price would move are changed. It asks first, saying how many. The raw material prices themselves don't change: record those on Raw Materials when they do. A version is recorded for each product changed, noted *Current Price set from the Price Simulator*, so its History tab shows it and **Restore** can put the old price back.

A composite product moves with the products it holds. Tick **Only affected products** to hide the products no change reaches. Sort by **Simulated Markup %** to see which products would earn the least. Filter by product or category from the funnel in its heading, and double-click a product to open it.

**Export to Excel** saves the products as the table shows them, and the price changes, on two sheets of one workbook.

### Buffered and unbuffered costs

The figures start at the materials' buffered costs, as everywhere else in the app. **Unbuffered**, at the top of the **Products** table, switches every figure, now and simulated, to the materials' true costs. An **Unbuffered costs** badge then appears next to the **Products** heading, and the button reads **Buffered**, which switches back. It works the same way as the **Unbuffered** button on the Products screen.

- A price change moves a material's true cost and its buffered cost by the same percent, so a 10% rise in flour is a 10% rise either way.
- **Print Report** and **Export to Excel** follow the switch. Each page of an unbuffered report says **Unbuffered costs**. The workbook is named **Price Simulator (Unbuffered)**, with a **Products (Unbuffered)** sheet.
- Nothing is saved. A scenario doesn't remember the switch, switching doesn't count as a change to the open scenario, and the screen starts on buffered costs each time you open the app.

### Printing a Price Simulator report

**Print Report** opens a print preview with each product's recipe and costing at the new prices, next to today's figures. Print it, or save it as a PDF with **Microsoft Print to PDF**.

To print only some products, click one in the **Products** table, or several with **Ctrl** or **Shift** held, before pressing **Print Report**. The products you picked stay picked while you change prices. At the bottom of the preview, **Print** chooses:

- **Selected products**: only the ones you picked. The preview opens on this choice when you've picked any.
- **All products shown**: every product the table shows, with **Only affected products** and the column filters applied, in the table's order.

The first page lists the **Price Changes** tried, then every product with its **Base Cost**, **Simulated Base Cost**, **Change %** and the page it starts on. Each product then starts on a new page, with:

- **Price Changes Applied**: the changes that reach the product. That includes changes reaching it through a made or linked ingredient, or through the products a composite holds. A product no change reaches says so.
- **Recipe**: each sub-assembly's ingredients and the packaging, at the new prices. A line a change moves is in the accent colour, with what it costs today in the **Cost Now** column.
- **Costing**: the Base Cost, Suggested Price and Markup at Current Price with the changes, with today's figure under each. Under the **Current Price** of a product a change reaches is the price to keep today's markup, such as **To keep markup 61.28**. The breakdown has **Now**, **Simulated** and **Change** columns for every part, up to the Total Selling Price.

**Include** chooses **Recipe**, **Costing** or both. Without the costing, the report prints the recipes with no costs and no price changes.

## 11. Production Plan

**Production Plan** works out what to make and what to buy for a day's or a week's baking, and what it sells for.

### Saved plans

At the top left, **Saved Plans** lists the plans saved under a name, such as *Saturday* or *Fiesta order*, newest first.

- **Save Plan** asks for a name and saves the products in the plan, and whether it's in half batches. With a saved plan open, the name starts as its own, so saving again updates it. Saving under another plan's name asks first, then replaces that plan.
- Click a plan in the list to open it. Its products replace the ones in the table. If the table holds a plan that isn't saved, you're asked first. A product deleted since the plan was saved is left out, and a message says so.
- **Delete Plan** deletes the open plan, after asking. Its products stay in the table.

Saved plans are kept in the database, so everyone using the app on this computer sees them, and they're in every backup. A product deleted for good is taken out of every saved plan.

### The plan

Under it, the **Plan** table lists the products to make:

- **Product**: pick one. Type to search the list. Products in Research and Development can be planned too.
- **Units**: how many sellable units to make. That's pieces for a product sold by the piece, or boxes for a Box of 8.
- **Pieces**: what the units come to. A composite product shows none, since it's one unit of other products.
- **Sales**: what the units sell for, at the product's **Current Price**, with VAT.
- **Profit**: what they earn over the product's base cost (its ingredients, packaging, wastage, labor, overhead and administrative), once the VAT is taken out.

Under the table are the plan's total **Sales**, **Profit** and **Profit Margin** (the profit as a share of the sales before VAT, as on the Products screen's Costing tab). A product with no Current Price is counted at the selling price its costing works out, and a note in orange names it.

A product listed twice is made once, its units added together. **Remove Line** takes the picked line out, and **Clear Plan** empties the table and closes the open saved plan.

The Mixing Sheet and the Purchase Requisition change as you type. A new line on the table's bottom row counts as soon as its product and units are in, before you leave the row. Without Vim keys, press **Esc** twice to throw the new line away, and the tables go back to how they were. With Vim keys on, Esc keeps it, as it keeps whatever you typed in a cell.
The plan in the table is kept for you on this computer, with the saved plan you have open. It's there again the next time you open the app, worked out from the products and prices as they are then. A product deleted since is left out.

### Mixing Sheet

Each mix to make, in whole batches:

- **Mix**: the sub-assembly, such as *Sweet Dough*, or a product's own section, such as *Ube Roll Filling*.
- **For**: the products it's for, and how many pieces each takes from it. A row grows to show the whole list when it runs to more than one line.
- **Batches** and **Weight (g)**: the batches to make, and what they weigh before baking.
- **Spare**: the batches are rounded up, so they make more than the plan needs. Spare is how many more pieces that is, such as **30 pcs**. For a mix shared by several products it says how many more of each, such as *Ube Roll 30 pcs or Cheese Roll 25 pcs*: it's the same dough either way.

**Use the spare**, under the table, has a button for each product whose spare pieces can go into the plan with no more mixing, such as **30 Ube Roll**. Press it to add those units to the product's line in the plan. The figure is in sellable units (whole boxes for a Box of 8), and it's only as many as **every** mix of the product has room for: if its dough has 30 pieces spare but its filling only 10 before it needs another batch, the button says 10. A product whose mixes have no room, such as one with a topping made a batch per piece, gets no button. For a mix shared by several products, each gets a button, but the spare is used once: after you press one, the others work out again.

Click the arrow at the start of a row to open it and see what goes into the mix: each ingredient, its grams **Per Batch**, and the **Total (g)** for all the batches. A row stays open while you change the plan.

Tick **Half batches** at the top when the kitchen can mix half a batch. Each mix is then rounded up to the next half batch instead of a whole one: 1.2 batches are 1.5, not 2. The tick is kept with the plan, and saved with a saved plan.

Products sharing a sub-assembly share its batches: two products needing 0.4 of a batch each make 1 batch between them, not 2. A product that adds its own add-ins to a linked recipe needs batches with the add-ins in, so that's a mix of its own, its name followed by the add-ins.

A made ingredient the mixes use, such as a syrup, is listed under the mixes with the grams to prepare. Open its row to see what it's made from, in grams. A composite product makes the products it holds, times how many of each it holds.

Under the table, a note in orange says what couldn't be planned, such as a section with no weight per piece or yield yet.

### Purchase Requisition

Everything to buy for the plan, ingredients first, then packaging:

- **Needed**, in the material's base unit (grams for an ingredient).
- **Cost**: what the amount needed costs, at buffered costs.
- **Buy** and **Per**: whole units to buy, in the unit of its dearest brand price, such as **2** *sack*. It's rounded up. A material with no price yet shows no Buy.
- **Buy Cost**: what the whole units to buy cost at that brand's price: the money to take to the market.

**Total Cost** and **Buy Cost** for the whole plan are at the top.

A made ingredient is bought as what it's made of. Egg white and yolk are bought as the eggs they come from: 300 g of white and 120 g of yolk take the same eggs, so only the larger amount of eggs is listed, and any whole egg the plan uses comes on top. Packaging is listed for every sellable unit. A composite product's products bring theirs only when it includes its products' packaging.

### Printing and exporting

**Print** opens the plan in a print preview: what to make with its sales and profit, the mixing sheet with each mix's ingredients and spare pieces, and the purchase requisition with its Buy Cost. A saved plan that's open is named in the title. Clear **Costs** at the bottom to print it without the sales, profit and costs, for the kitchen or the market. **Export to Excel** saves the plan, the mixing sheet and the purchase requisition on three sheets of one workbook. Each mix's ingredients are listed under it, grouped so Excel can fold them away.

---

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

This happens as soon as you save. A product already open on another tab shows the new figures when you switch back to it.

### Raw materials

- **Purchased**: cost per base unit = the dearest brand's price ÷ the base units in its purchase unit. The buffered cost is a fixed amount. It's set from the Cost Buffer % on the first price, or whenever you type the percentage again.
- **Made**: final weight = the grams of its sub-assembly's lines and its add-ins × (1 − Weight Loss %). Cost per gram = their total cost ÷ final weight. The buffered cost uses their buffered costs.
- **Linked**: cost per gram = linked-to ingredient's cost per gram × Cost Share % ÷ Weight Share %.

### Sub-assemblies

- Each line's cost = grams × the ingredient's buffered cost per gram.
- Batch weight and batch cost = the sum of the lines.

### A product's sections

- Cost per piece = batch cost × weight per piece ÷ batch weight (including add-ins). This is the same as batch cost ÷ yield.

### The selling price (cost-plus)

All figures below are per sellable unit.

1. **Sub-assemblies** = the sum of the sections' cost per piece × Quantity Required.
2. **Wastage** = the shop's Wastage % (Settings → Costing Defaults) **of the sub-assemblies alone**.
3. **Packaging** = the sum of the packaging lines. They're already per sellable unit.
4. **Labor**, **Overhead** and **Administrative** = their percentage **of the sub-assemblies alone**. Packaging and wastage aren't included.
5. **Base cost** = sub-assemblies + wastage + packaging + labour + overhead + administrative.
6. **Price before VAT** = base cost × (1 + Markup %).
7. **Selling price** = price before VAT × (1 + VAT %).

**Markup % at the current price**, in the header, works backwards from the current price: (current price ÷ (1 + VAT %) − base cost) ÷ base cost.

### A worked example

Say a product's sections cost **20.00** a piece, its packaging costs **2.00**, and it's sold by the piece. The shop's wastage is 3%, and the product's costing is Labor 25%, Overhead 10%, Administrative 5%, Markup 30% and VAT 12%.

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
3. The buffered cost stays where it was. To carry the new price into recipes with the buffer on top, **type the Cost Buffer % again**. Alternatively, type a new Buffered Cost.
4. Press **Save**. The change is logged in **Price History** and carried up to every product that uses the material.
5. Check the effect on the material's **Used By** tab, or open a product and look at its Costing tab.

### Start a new product from an existing one

- For **a different product that shares some recipes**, use **Clone** (Clone tab, or right-click → **Clone…**). It gets its own packaging.
- For **a size or pack variation** that should follow the original's recipes, use **Create Variant**. Then adjust its piece sizes, give it its packaging, and set its **Quantity Required** if it's sold as a pack.
- To **reuse just one dough or filling**, use **Add Sub-Assembly → Link an existing one** in the new product.

### See which products use an ingredient

- **Raw Materials** → pick the ingredient → **Used By** tab.
- For a shared sub-assembly: **Sub-Assemblies** → pick it → **Used By**.

### Check whether a product's price still earns enough

Open the product and compare the header's **Markup %**, which is what the current price earns, with the **Markup Metric** on the Costing tab, which is what you're aiming for: the header's figure is amber when it's short. In the Products list, a ⚠ marks every product that's short. For an overview of all products, look at **Reports → Product Cost Summary** and sort by **Short By**.

### Bring back a deleted product

Open the **Deleted** folder at the bottom of the Products list, right-click the product and choose **Restore**. It goes back to its folder as it was when you deleted it.

### Go back to an earlier recipe

1. Open the product and its **History** tab.
2. Pick the version, and read its **Changes** to check it's the one you want. Set **Compare With** to **The product as saved now** to see everything that would change, or press **Print Version** to see its whole recipe.
3. Press **Restore**. The version is now in the form, not yet saved.
4. Check the Recipe and Costing tabs, then press **Save**. Or press **Undo** to put the form back.

### Move a product into production

Right-click it in the Products list and choose **Promote to Production**, or drag it into a Production folder. Right-click a category folder and choose **Promote All to Production** to promote every product in that folder.

### Plan a day's baking

1. Open **Production Plan** from the menu.
2. For a plan you make often, click it in **Saved Plans**. Otherwise, in the **Plan** table, pick each product and type how many to make.
3. Check the **Mixing Sheet** for how many batches of each mix to make, and read any orange note under it. Open a mix's row to see its ingredients, and look at **Spare**: if a mix makes 30 pieces more than you need, you can add up to 30 to the plan for no extra mixing. **Use the spare** under the table adds them with one press. Tick **Half batches** if the kitchen can mix half a batch.
4. Check **Sales** and **Profit** under the Plan table, and the **Buy Cost** of the Purchase Requisition: the money to take to the market.
5. To keep the plan for another day, press **Save Plan** and name it.
6. Press **Print**, clear **Costs** for the kitchen, and print. Or press **Export to Excel** to send the purchase requisition on.

### See what a price rise would do

1. Open the **Price Simulator** from the menu.
2. In the top table, pick the raw material and type its **Change %**, such as **8**, or its **New Price**.
3. Tick **Only affected products** and sort by **Simulated Markup %**.
4. Read **Price to Keep Markup** for what to charge to keep each product's markup, and **Price to Charge** for it rounded. To charge that, pick the products and press **Set Current Prices**, or change a product's **Current Price** on **Products**.
5. To see why a product moves, Ctrl-click it and any others, press **Print Report**, and read each product's ingredients and costing, now and with the change. Print it, or save it as a PDF.
6. To keep the changes for another day, press **Save Scenario** and name it. Click it in the **Scenarios** list to open it again.
7. To make the price real, record it on **Raw Materials** as in [Record a price increase](#record-a-price-increase). The Price Simulator never changes a price itself.

### Back up to a USB stick

1. Put in the USB stick.
2. Open **Settings**, then the **Database** tab.
3. Press **Back Up Now**. The new backup is picked in the list.
4. Press **Copy to…**, choose the USB stick, and press **Save**.

### Restore a backup

1. Open **Settings**, then the **Database** tab.
2. Pick the backup in the list. For a backup on a USB stick, press **Restore from File…** and pick the file instead.
3. Press **Restore**, read the message, and press **Yes**.
4. Wait while it restores. The app closes and opens again with the backup's data.

If the data restored wasn't the one you wanted, the **Before restore** backup holds what was there just before. Restore that one to undo it.

### Send a report to Excel

1. Open **Reports** and pick the tab.
2. Sort, filter and choose the columns as you want them in the file.
3. Press **Export to Excel**, choose a folder and a name, and press **Save**.

### Print the recipes of a whole category

Right-click the category's folder in the Products list and choose **Print Folder**. Untick **Costing** for kitchen copies.

### Make a composite product, such as a gift box of several products

1. In the Products header, press the arrow beside **New** and choose **New composite product**. Or right-click a folder in the list and choose **New Composite Product Here**.
2. Type its **Product Name**.
3. On the **Products** tab, add a line for each product: pick the product, then type how many go in under **Qty**.
4. Use **Add Sub-Assembly** with the **Packaging** role for the box, and for anything wrapping the pieces inside it. The products' own packaging isn't included. If the pieces go in already in their own packaging, tick **Include each product's packaging** under the Products table instead of listing it again.
5. For a topping or filling over the whole composite, use **Add Sub-Assembly** in that role. Its **Weight/piece** is what one whole composite takes.
6. Check the **Costing** tab, then press **Save**. A composite needs at least two different products.

### Give a product a box for a pack of several pieces

1. On **Sub-Assemblies**, press **New**, pick the **Packaging** role, and list the box and the bags one pack takes. Save.
2. Make the pack product. For example, use **Create Variant** of the single and set **Quantity Required** to 8.
3. In its recipe, use **Add Sub-Assembly → Link an existing one** with the **Packaging** role, and pick the set. Save.

Or type the packaging straight into the pack product's Packaging tab, then press **Move to Sub-Assemblies** and Save. The set is made from those lines and linked for you.

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

**A product can't go into a composite.**
The product picker leaves out the composite itself, and any composite that already holds it. Otherwise the composite would end up holding itself.

**"Couldn't reach the database."**
The app keeps its data in PostgreSQL on this computer, and it isn't answering. Check that the **postgresql-x64-18** service is running in Windows' **Services**, then try again. The screen you're on keeps working: switch to another tab and back, or pick the record again, once the database is running.

**"The database didn't answer in time."** or **"The database was busy with another change."**
Nothing was saved. Try again.

**A message says the database is out of date for this version of TinapayanRMS.**
The app was updated, but the update to its database wasn't applied. Close the app and ask whoever installs it to apply the database update. Until they do, some screens show an error.

**"Couldn't write the file. If it's open in another program, such as Excel, close it there and try again."**
The file you're saving over is open, usually in Excel. Close it there and try again, or save under another name.

**"Windows didn't allow the app to use that file or folder. Pick another one."**
The folder is protected, such as a system folder. Save in Documents, on the Desktop or on a USB stick instead.

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

**A message says something went wrong, and that the app is still running.**
Something happened that the app didn't expect. It's in the log file, which helps whoever looks after the app. If anything on the screen looks wrong afterwards, save what you can, then close the app and open it again. If such messages keep coming, the app closes by itself.

**"That name is already in use."** or **"That abbreviation is already in use."**
Names must be unique within each list: raw materials, sub-assemblies, products, categories, roles and units. A unit's abbreviation must be unique too. Upper and lower case count as the same.

**Letters don't type, or a table cell won't start an edit when I type.**
The Vim keys are on, and the top right of the window says NORMAL. Click the field or cell, or press **i**, to type. Turn them off in Settings → Keyboard. See [Vim keys](#vim-keys).

**A figure is grey and can't be typed into.**
It's worked out by the app, or fixed by a rule. Examples are a standard unit's multiplier, the Absolute %, or a linked ingredient's buffer.

**A product's cost didn't change after a price change.**
Purchased materials are costed at their **buffered cost**, which stays fixed until you type the Cost Buffer % again. See [Record a price increase](#record-a-price-increase).

**I changed Quantity Required and the packaging looks wrong.**
Packaging quantities are per sellable unit and aren't rescaled. Update the Packaging tab by hand, or link a packaging set made for that pack size.

**A message asks "Save the changes to …?" when I click somewhere else.**
The product, sub-assembly or raw material showing has changes you haven't saved. **Yes** saves them, **No** throws them away, and **Cancel** keeps you where you are. See [Editing and saving](#editing-and-saving).

**The app shows an error about the licence when it starts.**
The app needs a licence key for its screen components, set on the computer by whoever installed it. Ask them to check it.

**Where are the log files?**
In the **logs** folder next to the app. There's one file per day, kept for 31 days. They're useful to send along when reporting a problem.
