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

### Baker's % and Absolute %

Each recipe line also shows a percentage.

- **Baker's %**: tick the **Base** box on one or more lines, usually the flour. Every line is then shown as a percentage of the ticked lines' total weight. You can type either the grams or the baker's %, and the other is worked out. You can tick a line's Base while you're still typing the line. The empty row at the bottom shows no box until you start typing into it.
- **Absolute %**: with nothing ticked, each line is shown as its share of the whole batch. This figure is read-only.

The column heading changes to show which one you're looking at. Only the grams are ever used for costing.

---

## 2. Getting around the app

### The main window

- **The window** opens in the middle of the screen the mouse pointer is on. On a screen too small for it, it opens as large as the screen leaves room for, above the taskbar. Only one copy of the app opens at a time: opening it again brings the window that's open to the front.
- **The title bar**, along the top, has the menu button and the app's name on the left, and **Find**, **Back**, **Forward**, then **Minimize**, **Maximize** and **Close** on the right. Drag the title bar by any empty part of it to move the window, drag it to the top of the screen or double-click it to make the window fill the screen, and right-click it for Windows' own window menu. While the window fills the screen, **Maximize** becomes **Restore Down**.
- **Menu button (☰)**, at the top left, opens the navigation menu: **Raw Materials**, **Sub-Assemblies**, **Products**, **Reports** and **Price Simulator**. **Settings** is at the bottom of the menu. Click anywhere outside the menu to close it.
- **Tabs**, under the title bar, show the screens you have open. Click a tab to go to its screen.
  - **Raw Materials**, **Sub-Assemblies** and **Products** have a tab from the start. **Reports**, **Price Simulator**, **Print** and **Settings** get one the first time you open them.
  - The **✕** on a tab closes it. Nothing is lost: the screen keeps the record it had open, and its tab comes back when you open the screen again from the menu. The last tab left has no ✕.
  - The **Settings** tab is the last one. Clicking it opens Settings on its first tab, **Units of Measure**.
  - Each screen keeps its place, such as the record you were looking at, until you close the app. A screen with changes you haven't saved asks about them before you leave it (see [Editing and saving](#editing-and-saving)).
- **Back and Forward** (the arrows at the top right, or **Alt+Left** and **Alt+Right**) move between the screens and records you've looked at, Settings' tabs included, like a web browser. If the cursor was in a box when another record opens, the cursor leaves the box, so that you don't type over the record that just opened. Click the box again to type in it.
- **Find** (the magnifier at the top right, before Back, or **Ctrl+K** from anywhere) looks up any raw material, sub-assembly or product by name and opens it on its own screen. Type part of the name: every word you type has to be in the name, in any order, and capitals and accents don't matter, so `ube ensay` finds *Ube Macapuno Ensaymada*. The closest names come first, with what each one is (Raw Material, Made Ingredient, Sub-Assembly or Product) and its category or role. **Up** and **Down** pick one, **Enter** or a double-click opens it, and **Esc** closes Find (press it twice with the Vim keys on: the first press only leaves Insert). The best 50 matches are listed; type more of the name to narrow them. Your last search is still there the next time you open Find. A sub-assembly that belongs to one product isn't listed: find the product instead. Deleted products aren't listed either. If the screen you're leaving has unsaved changes, you're asked about them first, as with any link between screens.
- Changes made on one screen show up on another the next time you open it.
- **Closing the window** closes the app. What you've saved is kept. If a product, sub-assembly or raw material has changes you **haven't** saved, the app asks whether to save them first, as it does when you go to another screen (see [Editing and saving](#editing-and-saving)).

### Lists as folder trees

The **Raw Materials**, **Sub-Assemblies** and **Products** lists are trees of folders, like File Explorer:

- **Search** at the top of the list narrows it to matching names and opens the folders that hold them.
- **Drag** one or more items onto a folder to move them there. A notice under the list says what moved, with an **Undo** button.
- **Right-click** an item, a folder or the empty space for a menu of what you can do there. Some menu items have keyboard shortcuts: **F2** renames and **Del** deletes.
- Folders you leave open or closed are the same the next time you use the app. A folder that's new since, such as a category or role you've just added, starts open, so whatever you save into it shows. The first time, every folder starts open, apart from the **Deleted** folder on Products.

### Editing and saving

- **One Save saves the whole record**, including everything on its tabs. Until you press **Save**, nothing you type is stored.
- **Undo** (**Ctrl+Z**) and **Redo** (**Ctrl+Y**) step back and forth through the changes you've made since opening the record.
- **Revert**, on the Products screen only, puts the whole product back as it was last saved.
- **Pickers** (the boxes with a ˅ arrow, such as Category, Role or Existing Sub-Assembly) can be typed into. Typing narrows the list to the names that contain what you typed, closest first: the name itself, then names starting with it, then names with a word starting with it, then the rest, so "salt" lists Sea Salt before Unsalted Butter. The closest is highlighted in blue, with a bar down its left side, and the box keeps just what you typed. **Enter** or **Tab** picks the highlighted name, so "marg" and Enter picks Margarine, and so does clicking somewhere else. **Down** and **Up** move the highlight through the list: the first press puts the highlighted name in the box, and each one after moves to the next. Before you type, the highlight is on the name the picker holds, or on an empty picker the first name, which Enter picks but Tab doesn't. The name under the mouse is the same blue, without the bar. **Backspace** takes off a letter at a time, and you can empty the picker to see the whole list. If what you typed matches nothing, or you leave the picker empty where a name is needed (a recipe line's ingredient), it shows what it had again once you leave it. **Esc** closes the list, and the picker keeps what it had. Clicking or tabbing into a picker selects its text, so what you type replaces it.
- **New** starts a blank record. **Delete** asks before deleting anything.
- **Changes you haven't saved are never left behind without asking.** When a product, sub-assembly or raw material, new or saved, has changes you haven't saved and you go somewhere else, a message asks **Save the changes to …?** It asks when you go to another screen (from the tabs, the menu or Find, or by closing the tab showing), pick another item in the list, press **New**, click a **Dependents** tile or another link, go **Back** or **Forward**, use **Clone** or **Create Variant**, or close the window. Reloading the page (**F5**) with changes not saved asks whether to leave instead.
  - **Yes** saves them, then goes on. If the save is refused, for example because a field is missing, you stay, and the red notice says why.
  - **No** throws them away, then goes on. If you were leaving the screen, the item is put back as it was last saved, so the changes aren't there when you come back. If it can't be put back, for example because the database isn't answering, the red notice says why and you stay.
  - **Cancel** keeps them, and you stay where you were.
  - Going between **Products** and the **Print** tab doesn't ask, since Print shows the product with your changes. Leaving Print for any other screen asks about the product's changes, as leaving Products would. Nor does **Copy** on the Sub-Assemblies screen, since the copy holds them.
- A green notice confirms a save. A red notice explains anything the app refused, such as a missing field, a name already taken, or something that can't be deleted because it's in use. It also says when the app couldn't reach its database, or the database didn't answer in time. Nothing is saved then, and the screen keeps working: once the database answers again, try again, or pick the record again.
- If something goes wrong that the app didn't expect, a red notice says what happened, the screen keeps working, and the details go in the [log files](#14-troubleshooting-and-questions). Nothing you haven't saved is lost. If it was worse than that, a bar at the bottom of the window says the page needs reloading: see [Troubleshooting](#14-troubleshooting-and-questions).

### Working in tables

- Most tables are typed straight into. Click a cell and type. The empty row at the bottom adds a new line.
- On that bottom row, **Tab** from its last cell adds the line, as **Enter** does, and opens the first cell of the next empty row, so you can type one line after another without reaching for the mouse.
- Pickers inside tables, such as the ingredient or material pickers, suggest names as you type.
- To change a line's ingredient, material or product, type over it. The line keeps what it had until you pick another, so its cost and the totals don't change while you type. Text that names nothing in the list leaves the line as it was.
- **Drag a row** by its left edge to change the order of recipe lines, add-ins and packaging lines. The order is saved.
- **Insert Line** puts a blank line in above the selected one, so you don't have to add it at the bottom and drag it up. The new line's Ingredient cell takes the cursor, ready to type. With no line selected, the new line goes in at the bottom. You can also press **Insert** while the cursor is in the table. It's on the ingredient tables: a sub-assembly's lines on the Sub-Assemblies screen, a product section's lines and add-ins (the add-ins button is **Insert Add-In**), and a made ingredient's add-ins. A line you leave blank does no harm, because Save leaves it out. **Undo** or **Remove Line** takes it away.
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
- In a small window that asks for a name or a Yes or No, the first Esc stops typing, and a second Esc closes the window. While such a window is open, **j** and **k** move between its buttons.

**Moving around**

- **j** and **k**: down and up. In a list or table that's a row. In a form, it's the next or previous field, like Tab and Shift+Tab.
- **h** and **l**: left and right. In a table that's a cell. In the Raw Materials, Sub-Assemblies and Products lists, **h** closes a folder and **l** opens it. In a field, they move the cursor.
- **gg** and **G**: the first and last row.
- **0** and **$**: the first and last column of a table, or the start and end of a field.
- **J** and **K**: leave the list or table, for the control after it or before it.
- **gt** and **gT**: the next and previous tab along the top, round from the last to the first. They pass over the **Settings** tab, because in Settings they move through its own tabs instead.
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
5. **Costing defaults** (Settings → Costing Defaults): the stage a new product goes in, the labour, overhead, administrative and markup percentages that it starts with, the cost buffer a new raw material starts with, and the shop's VAT and wastage.
6. **Raw materials**: every purchased ingredient and packaging material with its prices, then the linked ingredients.
7. **Sub-assemblies**: the doughs, fillings and packaging sets shared by more than one product, and the recipes of the shop's made ingredients, such as a syrup or an icing.
8. **Made ingredients**, on the Raw Materials screen, each made from one of those sub-assemblies.
9. **Products**.

You don't have to finish each list before moving on. You can add a category or unit later, and most pickers let you add a new category just by typing its name.

---

## 4. Settings

Open **Settings** from the bottom of the navigation menu. It opens as a screen of its own, with a tab at the end of the tab strip, and has seven tabs inside it: **Units of Measure**, **Material Categories**, **Product Categories**, **Roles**, **Costing Defaults**, **Keyboard** and **Database**. Each list tab works the same way: pick a row to edit it, or press **New**. Then fill in the form and press **Save**, or press **Delete**. Leave Settings by clicking another screen's tab, or close its tab with its **✕**.

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

Each category has a **Name**, and can have its own **Wastage %** (0 to 100), which its products are priced with in place of the shop's (see [Wastage](#wastage)). Leave it blank, as it starts, and its products use the shop's Wastage % from Costing Defaults. The list shows each category's Wastage %, blank for those using the shop's. Changing it reprices the category's products, saved ones included. Products don't need a category, and those without one are listed under **Uncategorized**. Deleting a category leaves its products uncategorized.

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

**New Products In** is where **New** on the Products screen files a product when you name it: **Production** (the default) or **Innovation**. **New Product Here** still files it in the folder you picked it on, and a clone or variant still goes in Innovation.

Under it are the four percentages a **new** product's costing starts with:

- **Labor %**, **Overhead %** and **Administrative %** (office, permits, bookkeeping): each is a percentage of the product's sub-assemblies (ingredients) cost.
- **Markup %**: added on the base cost (materials, wastage, labour, overhead and administrative).

**VAT %** is the shop's VAT, added on top of every product's marked-up price. It isn't where a new product starts: **every** product is priced with it, saved ones included, so changing it changes every product's suggested price. A product's Costing Summary shows what it adds to the price, but you change it only here.

Under them, **Wastage %** is the shop's [wastage](#wastage): a percentage of every product's sub-assemblies (ingredients) cost, added to its costing. It starts at 3%, and can be anything from 0 to 100. Like the VAT, it isn't where a new product starts: **every** product is priced with it, saved ones included, unless its category sets its own (Settings → Product Categories), so changing it changes those products' costs and suggested prices.

**Price Rounding** rounds a suggested price up to a price you'd charge: up to the next 0.25, 0.50, 1, 5 or 10, or **None** (the default). It's always rounded up, so the rounded price still earns at least the markup. The exact figures don't change: the rounded price is shown beside them, in the last row of a product's Costing tab, under the **Suggested Price** (*Rounded up to the next 0.25*), in the **Rounded Price** column of Reports' Product Cost Summary, and in the Price Simulator.

**Cost Buffer %** is what a new raw material's [cost buffer](#cost-buffer) starts with on the Raw Materials screen. It starts at 10%, and can be anything from 0 to 100. It only matters for a purchased material: a linked ingredient takes the buffer of the one it's linked to, and a made ingredient has none of its own.

Each product keeps its own stage and percentages, and each raw material its own cost buffer, from its first Save. Changing those defaults doesn't change anything saved. Changing the Wastage % does: it reprices every product. A new product you haven't saved yet moves to the new stage, since you didn't pick one for it. A new raw material you haven't saved moves to the new cost buffer, unless you've typed one.

**Stored Costs**, to the right of the defaults, has a **Recalculate Costs** button. The app keeps every made and linked ingredient's cost up to date as you save. You only need this button if the database was changed outside the app, for example by a script. It works those costs out again from what they're made of, and a green notice says how many were out of date. Sub-assemblies and products need nothing: their costs are always worked out fresh from their lines. Purchased materials keep their prices.

### Keyboard

**Vim keys** turns on the keys described in [Vim keys](#vim-keys). The change takes effect at once.

The setting is kept for you on this computer, not in the bakeshop's data. Turning it on doesn't change the app for anyone else, or for you on another computer.

### Database

Backups of all of the app's data: every raw material, price, sub-assembly, product and setting.

- **Folder** shows where the backups are kept, *C:\ProgramData\TinapayanRMS\Backups*. **Open Folder** shows it in File Explorer. Everyone who uses the app on this computer shares the folder.
- The list shows every backup, newest first: when it was **Taken**, its **Kind** and its **Size (MB)**.
  - **Manual**: taken with **Back Up Now**.
  - **Automatic**: the app takes one by itself each day, while it's open: when it starts, and if it's left open overnight, within the hour after midnight. The newest 14 are kept, and older automatic ones are deleted.
  - **Before restore**: the safety backup a restore takes of the data it replaces.
  - **Copied in**: a backup brought in with **Restore from File…**.
  - Only automatic backups are ever deleted by the app. Delete others in File Explorer when you no longer need them.
- **Also Copy To** is a second folder each automatic backup is copied to as well, such as a USB stick that stays plugged in or a OneDrive folder. Press **Choose…** to pick it: the newest automatic backup is copied there straight away. The newest 14 automatic backups are kept there too; nothing else in the folder is touched. **Stop Copying** stops it, and leaves what was copied. If the folder isn't there when a backup is taken (the USB stick was taken out), the backup is still taken here, and a note in amber says it wasn't copied. The note stays until a copy goes through or you change the folder. The folder is kept for you on this computer.
- If an automatic backup can't be taken, a note in amber under **Folder** says why, and the app tries again within the hour. The note stays until one is taken, even if you close the app and open it again. If they keep failing, and the newest backup is three days old or more, a warning across the top of every screen says so until a backup is taken. Ask whoever set up the app to look into it.
- A file in the folder ending in *.partial* is a backup still being written, or one that was cut short, by a power cut for example. It isn't listed, it can't be restored, and the app deletes one that's left behind after a day.
- **Back Up Now** takes a backup straight away.
- **Copy To…** saves a copy of the backup picked in the list where you choose, such as a USB stick. Do this now and then: backups kept only on this computer are lost with it if it breaks.
- **Restore** puts the backup picked in the list back, in place of all of the app's data. It asks first, saying when the backup was taken. Then it takes a safety backup of the data as it is, puts the backup back, and every screen starts afresh with the restored data. Anything saved since the backup was taken is lost, and so is anything not yet saved on another screen.
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

- **A material**: **Move to** (another category of the same type: a made ingredient's are the Made ones), **Rename** (F2), **Copy** and **Delete** (Del).
- **A type's folder**: **New Ingredient Here** or **New Packaging Material Here**, **New Folder**, **New Made Folder** (Ingredients only), **Expand All** and **Collapse All**.
- **A category's folder**: **New … Here**, **New Folder**, **Rename Category** (F2) and **Delete Category** (Del). In a Made category's folder, **New Made Ingredient Here** starts a made ingredient in that category, and **New Folder** adds another Made category. You can only delete a category when it's empty. Otherwise the menu shows how many materials it holds.
- **The empty space**: **New Raw Material**, **New Ingredient Folder**, **New Made Folder**, **New Packaging Folder**, **Expand All** and **Collapse All**.

The same commands are buttons under the search box: **New Raw Material**, **New Folder** (its arrow offers an ingredient, a Made or a packaging folder), **Expand All** and **Collapse All**. Each button shows only its icon; point at it to see its name.

**Names are typed straight into the list**, as on the Products screen:

- **Rename** (or F2, or clicking the material you've already picked once more, slowly: a double-click doesn't rename) turns its name into a box. Type the new name and press **Enter**, or click away, and it's saved straight away. **Esc** leaves it as it was. **Rename Category** does the same for a category's folder.
- **New Folder** adds a *New folder* row, its name selected. Type the category's name and press **Enter**: the category is saved. **Esc**, an empty name, or clicking away before you've typed one, adds nothing.
- **New**, **New Raw Material** and **New … Here** add a row for the new material, its name selected: in the folder you picked, or at the end of **Ingredients** when it has no category yet. Type its name and press **Enter**. The name goes into the form, and the row keeps it, but **the material isn't saved yet**: fill in the rest (its category, its prices, a packaging material's base unit, a made ingredient's sub-assembly) and press **Save**. **Esc**, or clicking away before you've typed a name, adds nothing: the form stays on the new material.

Dragging materials onto another category moves them straight away. A material can't be dragged into a category of another type, so a made ingredient only drops into a Made category, and nothing else does.

**Copy** starts a new material from the one you right-clicked, named after it with *Copy* on the end (*Disposable Spoon* becomes *Disposable Spoon Copy*, or *Disposable Spoon Copy 2* if that name is taken). Only its type, its category and its base unit come with it. Its prices, alternative units, cost buffer and the rest start empty, as for **New**. A copy of a made ingredient is a made ingredient in the same Made category, but you still pick its sub-assembly. Change the name if you like, fill in the rest, then press **Save**: nothing is saved until you do. If the material showing has unsaved changes, you're asked about them first.

### The header

At the top are the material's name and **New**, **Save**, **Undo**, **Redo** and **Delete**, then these figures:

- **Base Unit**: *gram (g)* for an ingredient.
- **Current Cost per …**: the true cost, as last saved. For a purchased material it's the price of the brand in **Basis** (the dearest brand unless you pick one), per the unit it's shown in.
- **Buffered Cost per …**: the cost with the buffer on top. This is the figure recipes use.
- **Total Weight** and **Total Cost**, for a made ingredient only: what its batch yields after weight loss, and what that batch costs.
- **Last Updated**: when the material was last saved, and when its cost last changed.

**Delete** removes the material with its prices and alternative units. While anything uses it, it stays, and the message names each one and how it uses it: a recipe or packaging set with it in its lines, a product with it in its packaging or as an add-in, a made ingredient with it as an add-in, or an ingredient linked to it. Take it out of those first.

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

### The list

The list has a folder for each role, in the order set in Settings → Roles, with the sub-assemblies in that role inside it, by name. Each folder shows how many it holds. When a product saves a new section, it's saved here under a name made from the product and the role, such as *Cheese Roll Dough*. If the section has its own name, that's added after them, as in *Cheese Roll Filling: Ube*.

Right-click menus:

- **A sub-assembly**: **Move to** (another role), **Rename** (F2), **Make In-House Ingredient…** and **Delete** (Del). A packaging set has no **Move to** or **Make In-House Ingredient…**: it always stays in the Packaging role.
- **A role's folder**: **New Sub-Assembly Here** (**New Packaging Set Here** on the Packaging role), **New Folder**, **Rename Role** (F2), **Delete Role** (Del), **Expand All** and **Collapse All**. You can only delete a role when no sub-assembly here is in it. Otherwise the menu shows how many it holds. The Packaging role can't be deleted.
- **The empty space**: **New Sub-Assembly**, **New Folder**, **Expand All** and **Collapse All**.

The same commands are buttons under the search box. Each button shows only its icon; point at it to see its name.

Dragging sub-assemblies onto another role's folder changes their role straight away, and every product that links them follows. A recipe can't go into the Packaging role, and a packaging set can't leave it. A move is refused, with a message saying why, when a product that links the sub-assembly would end up with two sections in that role it can't tell apart, or when a composite product links it and the new role isn't product-owned.

**Names are typed straight into the list**, as on the Products screen. **Rename** (or F2, or clicking the sub-assembly you've already picked once more, slowly: a double-click doesn't rename) turns its name into a box: press **Enter**, or click away, and the new name is saved straight away, as a move is. If the sub-assembly is open in the form, its name or role there changes too, and anything else you've changed stays to be saved. **Esc** leaves it as it was. **Rename Role** does the same for a role's folder.

**New Folder** adds a *New folder* row after the roles: type the role's name and press **Enter** to add it. **New**, **New Sub-Assembly** and **New … Here** add a row for the new sub-assembly in its role's folder (from **New**, the first role's): type its name and press **Enter**. The name goes into the form, but **it isn't saved yet**: add its lines and press **Save**. **Esc**, or clicking away before you've typed a name, adds nothing: the form stays on the new sub-assembly.

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
- **Dependents**: a tile for each product that links this sub-assembly. Click one to open it.

The tab you were on stays picked when you pick another sub-assembly, and when you go to another screen and come back, as on Products and Raw Materials.

**Scale**, under the lines, resizes the recipe. Pick **the whole batch** or **the base (ticked lines)**, type the grams it should weigh, and press **Scale**: every line is multiplied by the same amount, so each keeps its percentage. For example, a 1,200 g batch scaled to 2,400 g doubles every line, and a dough whose ticked flour is 1,000 g, scaled on the base to 500 g, halves. It's one step for **Undo**, and **Save** keeps it. Scaling the base needs at least one line's **Base** ticked. It isn't offered for a packaging set.

**Copy** starts a new, unsaved sub-assembly with the same role and lines, named "… Copy". Rename it if you like, then press Save. The original stays as it was.

**New Product** starts a new product from the sub-assembly picked in the list. The Products screen opens with a new, unsaved product named after the sub-assembly, and its recipe already **links** the sub-assembly. Type the piece size (and rename the product if you like), then press Save there. New Product isn't offered for a packaging set. If the sub-assembly has changes you haven't saved, save or undo them first, because the product links the sub-assembly as it's saved.

Changing a sub-assembly here changes every product that links it, and every made ingredient made from it. When saving new lines would change what any of those products cost, **Save** asks first and lists each one's base cost now and after, for one sellable unit, such as *Pandesal x 8: 2.58 → 2.32 (-0.26)*, biggest change first. **No** leaves the changes unsaved in the form. When made ingredients are made from it, the question says they change too. A sub-assembly that a product links, or that a made ingredient is made from, **can't be deleted**. The message names them.

The **Dependents** tab shows a tile for each product that links the sub-assembly, then one for each made ingredient made from it. Click a product's tile to open it on the Products screen, and a made ingredient's to open it on the Raw Materials screen. Saving lines here refuses a line that's a made ingredient made from this sub-assembly, directly or further down, because that ingredient would contain itself.

### Packaging sets

Pick the **Packaging** role to make a packaging set. The lines table then holds packaging materials instead: **Material**, **Qty**, **Unit**, **Unit Cost** and **Cost per Set**, with a **Set Cost** total.

A product whose packaging links a set takes one whole set per sellable unit. For example, *Ensaymada Box of 8* can link a set holding 8 bags and 1 box.

A sub-assembly can't be switched between a recipe role and the Packaging role once it's saved.

---

## 7. Products

The Products screen has the product list on the left. On the right are the selected product's header, its details, and four tabs: **Recipe**, **Costing**, **Clone** and **History**.

### The list

The top-level folders are **Production** and **Innovation**. Each has a folder for every product category, plus **Uncategorized**. Drag products between folders to change their stage or category.

Below them is the **Deleted** folder 🗑, where deleted products wait, by name, until you restore them or delete them permanently. Clicking a deleted product doesn't open it. It can't be dragged, and nothing can be dropped on the folder.

Right-click menus:

- **A product**:
  - **Promote to Production** or **Send Back to Innovation**
  - **Move to** (any stage and category)
  - **Apply Costing Defaults…**: sets the product's Labor, Overhead, Admin and Markup percentages to Settings' Costing Defaults, once you confirm. Each product it changes gets a version noted "Costing defaults applied". If the product open on the form has unsaved changes, you're asked to save or discard them first.
  - **Rename** (F2)
  - **Clone…** and **Create Variant…** (both ask for the new product's name)
  - **Delete** (Del)

  With several products picked, the menu offers to promote or move them all, or to apply the costing defaults to them all.
- **A stage's folder**: **New Product Here**, **New Composite Product Here**, **New Folder**, **Print Stage**, **Expand All** and **Collapse All**.
- **A category's folder**: **New Product Here**, **New Composite Product Here**, **Promote All to Production**, **Print Folder**, **New Folder**, **Rename Category** (F2) and **Delete Category** (Del).
- **The empty space**: **New Product**, **New Composite Product**, **New Folder**, **Print All Products**, **Expand All** and **Collapse All**. These are also buttons under the search box. Each button shows only its icon; point at it to see its name.
- **A product in the Deleted folder**: **Restore** and **Delete Permanently** (Del).

**Names are typed straight into the list**, as in File Explorer:

- **Rename** (or F2, or clicking the product you've already picked once more, slowly: a double-click doesn't rename) turns the product's name into a box with the name selected. Type the new name and press **Enter**, or click away, and it's saved straight away. **Esc** leaves the name as it was. **Rename Category** does the same for a category's folder, and renames it in both Production and Innovation.
- **New Folder** adds a *New folder* row, its name selected, to the stage you right-clicked (from the button under the search box, the stage of the row you've picked). Type the category's name and press **Enter**: the category is saved, and gets a folder in both Production and Innovation. **Esc**, an empty name, or clicking away before you've typed one, adds nothing. A name that's already a category picks that category's folder.
- **New Product Here**, **New Composite Product Here** and **New** in the header add a *New product* row, its name selected, to the folder the product goes in. Type its name and press **Enter**: the product is saved there and then, with Settings' costing defaults, and opens on the right for you to fill in its recipe, packaging and price. **Esc**, or clicking away before you've typed a name (on the form, or on another product), adds nothing, so no product called *New product* is saved by accident. Clicking the form leaves it on the new product, for you to fill in, name and **Save**. **New** from the header puts it in the stage set in Settings → Costing Defaults, Production unless you change it, under Uncategorized.
- **A composite product can't be saved until it holds two products**, so its name goes into the form instead. Its row keeps the name until you press **Save**.

If the name is already taken, a message says so and the box stays open with what you typed, to change it.

A ⚠ after a product's name means its **Current Price** earns less than the **Markup Metric** on its Costing tab. The marks are worked out from the products as saved, a moment after the list loads, so a product you've just saved shows its new mark the next time the Products tab opens.

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

**Unbuffered** switches every cost on the Products screen to the materials' true costs, without their cost buffers. That covers the figures above, the Cost/g and cost columns, the batch cost and cost per piece, packaging, the Costing tab, the recipe cost of a composite product's products, and the cost column in the packaging pickers. While it's on, the button reads **Buffered** and an **Unbuffered costs** badge shows beside the product's name. Press **Buffered** to go back to buffered costs.

- It only changes what you see. Nothing is saved, **Save** and **Undo** aren't affected, and **Reports** still use buffered costs.
- It stays on when you open another product, until you switch it off, close the window or reload the page. The app always starts on buffered costs.
- **Print** uses whichever costs the screen shows (see [Printing](#8-printing-recipes-and-costings)).

A **Priced below cost** badge in the header means prices have risen past the buffered cost of a purchased material in the recipe or packaging. Every cost here is at buffered costs, so the product is priced under what that material costs. Point at the badge to see which materials. Re-apply their target buffers on Raw Materials, or in Reports' **Cost Buffers**, to carry their new prices in. A composite product's own products aren't checked here; each shows its badge when you open it.

Deleting a product asks first, then moves it to the **Deleted** folder at the bottom of the list. Nothing of it is lost there: its recipe, packaging, costing, stage and category are all kept. Until you restore it, no picker, report, print or Price Simulator includes it, and TinapayanBMS's copy leaves it out.

- **Restore** (right-click it in the Deleted folder) puts it back in its folder and opens it. If its category was deleted in the meantime, it comes back in **Uncategorized**.
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
- Bulk packaging is this product's own. **Link**, **Copy**, **Make a Copy**, **Remove Link** and **Move to Sub-Assemblies** leave it as it is, and the packaging set never has it. **Clone** copies it; **Create Variant** doesn't, since a variant starts with no packaging.

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

On the left are the product's four metrics, **Labor Metric**, **Overhead Metric**, **Admin Metric** and **Markup Metric**. A new product's metrics start with the Costing Defaults. The VAT is the same for every product, so it isn't set here: the summary on the right shows what it adds to the price, and it's changed only in Settings → Costing Defaults.

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

Under it, a second table puts the price the costing suggests beside the price the shop charges now, so you can compare the two. Its columns are:

- **Suggested Price**: the price the costing works out, for the unit the figures are for (one piece, a unit of 8 pieces when Quantity Required is 8, or one unit of a composite product). Point at the heading to see which, such as *Suggested Selling Price per Unit of 8 Pieces*.
- **Current Price**: the product's **Current Price**, as typed in the header.

Each column shows, for its price:

| Row | Suggested Price | Current Price |
|---|---|---|
| **Markup %** | The Markup Metric | What the store price earns on the base cost, once VAT is taken out |
| **Markup** | The markup in pesos, over the base cost | What the store price leaves over the base cost, once VAT is taken out |
| **VAT** | The VAT added on top | The VAT inside the store price |
| **Selling price** | The suggested price | The store price |

For example, with a base cost of ₱100.00, a Markup Metric of 30% and 12% VAT, the suggested price is ₱145.60: ₱30.00 of markup and ₱15.60 of VAT. A store price of ₱134.40 is ₱120.00 before VAT, so it earns ₱20.00 (20%) and holds ₱14.40 of VAT.

The store price's Markup % and Markup turn **amber** when it earns less than the Markup Metric, and **red** when it doesn't cover the base cost, as the header's Markup % does. With no Current Price entered, its column shows dashes. When Settings sets a [price rounding](#costing-defaults), a last row shows the suggested price rounded up, such as *Rounded up to the next 0.25*.

Everything updates as you type, before you save. See [How costs are worked out](#12-how-costs-are-worked-out).

### Clone tab

The Clone tab is shown for saved products only. Type a **New Product Name**, then choose:

- **Clone**: the new product links the same sub-assemblies from the Sub-Assemblies screen at the same piece sizes, gets its **own copy** of this product's own sub-assemblies, and gets its **own copy of the packaging**.
- **Create Variant**: the new product **links every sub-assembly this product uses** at the same piece sizes: the ones it links from the Sub-Assemblies screen (a dough, sponge or batter) and this product's own (a filling, topping and so on). So it follows every change made to them. It gets **no packaging**, neither linked nor copied. Use this for a smaller or bigger version of the same product, then give it its own packaging.

A section that's this product's own shows in the variant with a badge such as "Ensaymada Big Filling, from Ensaymada Big". Click it to open the product the section belongs to. You can change the section's lines in the variant too: Save writes them to that product's sub-assembly. **Make a Copy** gives the variant its own copy instead. Once a variant drops a linked section, it can't link it again.

While a variant links one of this product's own sub-assemblies, this product can't remove that sub-assembly and can't be deleted. Make a copy of it in the variant, or remove it there, first.

The category, Quantity Required, Current Price and costing percentages are copied with both. The new product starts in Innovation, and you can change its piece sizes. Its History tab starts with one version that says where it came from (see [History tab](#history-tab)).

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
- A linked sub-assembly that has been deleted since comes back as a new section with the old lines.
- Packaging links what it linked, if that still exists. Otherwise the lines it showed come back as the product's own.
- If a material, role or product the version needs has been deleted, Restore refuses and names it, and nothing in the form changes.

A product moved to the Deleted folder keeps its history. **Delete Permanently** deletes the history with it.

---

## 8. Printing recipes and costings

Press **Print** in the Products header to open the **Print** tab. It shows the product's recipe and costing on a sheet of paper, as the Products screen shows them, including changes you haven't saved yet. On the History tab, it shows the version picked there, at its costs then (see [History tab](#history-tab)).

- **Print** (the picker at the top left) chooses **This product**, the folder last asked for, or **All products**.
- **Include**: tick **Recipe**, **Costing** or both. Without the costing, the recipe prints with no costs, which is useful for the kitchen. A composite product's recipe prints as its products table, then its own sections.
- Each printed section lists its ingredients with their **Grams** and **Baker's %** (or **Absolute %**), and with the costing their **Cost/g** and **Cost**. The **Base** ticks aren't printed.
- The printed costing is laid out as the Costing tab is: the breakdown down to the **Subtotal (base cost)**, then the **Suggested Price** beside the **Current Price**, each with its Markup %, Markup, VAT and selling price, and the rounded suggested price when Settings sets a price rounding. The current price's Markup % and Markup print amber below the Markup Metric and red below the base cost. A Price Simulator report keeps its own layout (see [Printing a Price Simulator report](#printing-a-price-simulator-report)).
- **Print** (the button at the top right) opens Windows' print window. Choose the printer, the paper, the orientation and the margins there. To make a PDF, choose **Microsoft Print to PDF** (or **Save as PDF**) as the printer.

To print a set of products, right-click in the Products list and choose **Print Folder**, **Print Stage** or **Print All Products**.

A set prints each product **as saved**, with a contents list first and each product starting on a new page. The Print tab shows the same: the contents on a sheet of its own, then a sheet for each product. A sheet there is as long as its product, so a long recipe is one tall sheet on the screen and prints on two pages or more. If the product open on the Products screen has unsaved changes, the Print tab warns you that it's printed as saved.

Under each product's title are when it was last updated, when it was printed, and whether it includes unsaved changes. Each page has its number at the bottom. The print window can add a line of its own at the top and bottom of each page, with the date and the page's address: untick **Headers and footers** there to leave it out.

While the Products screen shows unbuffered costs, everything printed, a single product or a set, is at the materials' true costs. Each product's page says **Unbuffered costs** under its title and over its costing.

Leave the Print tab with Back (**Alt+Left**), by clicking another screen's tab, or by closing the **Print** tab with its **✕**.

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
- **Short By**: how many percentage points **Markup %** is under **Target Markup %**. Blank when it isn't short, or there's no current price.

Sort by **Markup %** to see which products earn the least, or by **Short By** to see which fall furthest short of what you're aiming for.

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

**Print Report** opens a print preview with each product's recipe and costing at the new prices, next to today's figures. **Print** there opens Windows' print window: print it, or choose **Microsoft Print to PDF** as the printer to make a PDF. **Back** returns to the Price Simulator.

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

Plan it in TinapayanBMS's **Plan** tab: see [Production Plan](#11-production-plan).

### See what a price rise would do

1. Open the **Price Simulator** from the menu.
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

### Print the recipes of a whole category

Right-click the category's folder in the Products list and choose **Print Folder**. Untick **Costing** for kitchen copies.

### Make a composite product, such as a gift box of several products

1. In the Products header, press the arrow beside **New** and choose **New composite product**. Or right-click a folder in the list and choose **New Composite Product Here**.
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
3. In the Bulk Packaging table, add what one box takes: the box itself (Qty 1), and anything else for the whole box, such as its tape.
4. Check the caption under the table, then Save. Each product now carries a sixth of the box's cost, on top of any packaging of its own.

### Install or update TinapayanRMS

TinapayanRMS is installed from its source folder, on the PC that holds its database, for the Windows user who'll use it. That PC needs what building the app needs (see the developer manual's setup): the .NET SDK, its MAUI workload, PostgreSQL with the app's sign-in, and the Syncfusion licence key.

1. Close TinapayanRMS if it's open. If you forget, the script closes it for you, unless it's asking whether to save changes: then it stops, and you answer, close the app and run it again.
2. Open PowerShell, go to the TinapayanRMS folder, and run `powershell -ExecutionPolicy Bypass -File scripts\Install-TinapayanRMS.ps1`. It builds the app, which takes a few minutes, copies it to `%LocalAppData%\Programs\TinapayanRMS`, and puts a **TinapayanRMS** shortcut in the Start menu and on the Desktop. Add `-NoDesktopShortcut` to leave the Desktop as it is.
3. If the update changed the database, the script's last lines say how to apply it. Do that before opening the app, or some screens show an error.

Run the same command again to update the app after the code changes. How the lists were left, your Vim keys setting, the backups and the database all stay as they are. To remove the app, run `scripts\Uninstall-TinapayanRMS.ps1` the same way.

**Taking the old app off.** The older TinapayanRMS this one replaced was put on the PC by hand. Once the new one is installed and working, take the old one off so nobody opens it by mistake:

1. Close the old app.
2. Run `powershell -ExecutionPolicy Bypass -File scripts\Remove-WpfApp.ps1`. It only lists what it finds: the old app's folder, its shortcuts, and whether it's running. The install script runs it too, at the end.
3. If the list is right, run it again with `-Remove` at the end. It deletes the old app and its shortcuts, and keeps its `logs` folder. If it says it found nothing but the old app is still on the PC, add `-Folder` and the folder it's in, such as `-Folder C:\TinapayanRMS`.

It never touches the database or the backups, and it keeps the old app's record of how the lists were left, which the new app copies the first time it opens. A shortcut pinned to the taskbar has to be unpinned by hand: right-click it and choose **Unpin from taskbar**.

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
A number box takes numbers under a billion (999,999,999.9999 at most). If you type more digits than that, for example by holding a key down, the box keeps the last number it could take and shows it when you leave the box. Check the figure and type it again.

**A bar at the bottom of the window says something went wrong, and that the page needs reloading.**
Something happened that the app didn't expect. It's in the log file, which helps whoever looks after the app. Press **Reload**. What you had saved is safe; what you hadn't saved is lost.

**Opening the app brings up the window that's already open, not a new one.**
Only one copy of TinapayanRMS opens at a time, because a save in one copy could overwrite what was saved in the other, with neither saying so. This doesn't stop another Windows user on the same PC, another PC using the same database, or another version of the app on this PC, so take care not to work on the same thing in two of them at the same time.

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

**A message asks "Save the changes to …?" when I click somewhere else.**
The product, sub-assembly or raw material showing has changes you haven't saved. **Yes** saves them, **No** throws them away, and **Cancel** keeps you where you are. See [Editing and saving](#editing-and-saving). Closing the window asks the same. Reloading the page (**F5**) with changes not saved asks whether to leave instead.

**The window shows a notice about a Syncfusion licence.**
The app needs a licence key for its screen components, set on the computer by whoever installed it. Ask them to check it.

**Where are the log files?**
In the **logs** folder of the app's data folder, *%LocalAppData%\TinapayanRMS\Maui\logs*. There's one file per day, kept for 31 days. They're useful to send along when reporting a problem.
