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
10. [How costs are worked out](#10-how-costs-are-worked-out)
11. [How to…](#11-how-to)
12. [Troubleshooting and questions](#12-troubleshooting-and-questions)

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

You can also give a material up to **two alternative units**. Each one is a multiple of the base unit. Examples:

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
- **Made**: made in the shop from other ingredients, such as a syrup or an icing. Its cost comes from its **bill of materials** (the ingredients it's made from) and its **weight loss %**, the weight lost while making it.
- **Linked**: a part of another ingredient, such as egg white and egg yolk from a whole egg. Its cost is a share of that ingredient's cost.

### Cost buffer and buffered cost

A **cost buffer %** is a cushion on top of what a purchased material really costs. It covers price swings, waste and variation in yield.

The **buffered cost** is the cost with that cushion on top. **Recipes and products are always costed at buffered costs.** To see what a product costs without the cushion, press **Unbuffered** in the Products header (see [The header](#the-header-1)).

For a purchased material, the buffered cost is a **fixed amount**. A new brand price doesn't move it on its own. See [Cost buffer](#cost-buffer) for how to update it.

### Sub-assemblies and roles

A **sub-assembly** is one part of a recipe that's made as a batch: a dough, a filling, a topping, a glaze. It's a list of ingredients in grams.

Every sub-assembly has a **role** that says what it is in the recipe, such as Dough, Filling, Topping or Packaging. Roles also set the order in which a product's sections appear.

A product uses a sub-assembly in one of two ways:

- **Linked**: the product uses a sub-assembly that's kept on the Sub-Assemblies screen. Every product that links it follows every change made to it. Use this when several products share one dough, for example.
- **Its own**: the sub-assembly belongs to that one product. Other products can copy it but can't link it. A role can be set to be **product-owned** in Settings. A section made in a product in that role is then always the product's own.

A sub-assembly in the **Packaging** role is a **packaging set**, such as a gift box with its ribbon and tag. It holds packaging materials instead of ingredients.

### Products

A **product** is something the shop sells. It is made from:

- its **recipe**: one or more sub-assemblies, each with how much of it one piece takes
- its **packaging**
- its **costing**: the percentages that turn the cost of materials into a selling price

Other terms you'll see on a product:

- **Quantity Required** is how many finished pieces make one sellable unit. It's 1 for a product sold by the piece, and 8 for a "Box of 8". The recipe is always written per piece. The costing is per sellable unit.
- **Stage**: every product is either in **Research and Development** (still being worked out) or in **Production** (made and sold). New products start in Research and Development.
- **Clone** and **Create Variant** start a new product from an existing one. See [Clone tab](#clone-tab).

### Composite products

A **composite product** is made of two or more other products, such as a *Pasalubong Box* of 6 Ensaymada and 4 Cheese Rolls. Its recipe is a list of products and how many of each go in. It has its own packaging and its own costing.

- Each product in it brings its **recipe cost**: what its sub-assemblies (ingredients) cost for one of it, as that product's own Costing tab shows.
- A product's packaging stays out. The composite's own packaging covers the box and anything wrapping the pieces in it.
- It can also have sub-assemblies of its own, such as icing over a whole party box, but only in **product-owned** roles (see [Roles](#roles)). With the usual roles that's Topping, Filling and Pan Greasing, never Dough or Sponge. Its sections are sized for one whole composite.
- A composite can hold another composite, but it can never end up holding itself.
- A composite is always one sellable unit, so it has no **Quantity Required**.
- You choose whether a product is a composite when you make it. That can't change once it's saved.

### Piece size: weight per piece, yield and shrinkage

For each sub-assembly in a product you say how much of the batch one piece takes. Type **either** figure, and the app works out the other:

- **Weight/piece (g)**: what one finished piece weighs
- **Yield (pcs)**: how many pieces one batch makes

**Shrinkage %** is weight the batch loses while it's made, such as a dough baking off or a filling reducing. The pieces come out of what's left, the **final weight**.

The figure you typed stays put. If the batch changes, the other figure is worked out again.

### Baker's % and Absolute %

Each recipe line also shows a percentage.

- **Baker's %**: tick the **Base** box on one or more lines, usually the flour. Every line is then shown as a percentage of the ticked lines' total weight. You can type either the grams or the baker's %, and the other is worked out.
- **Absolute %**: with nothing ticked, each line is shown as its share of the whole batch. This figure is read-only.

The column heading changes to show which one you're looking at. Only the grams are ever used for costing.

---

## 2. Getting around the app

### The main window

- **Menu button (☰)**, at the top left, opens the navigation menu: **Raw Materials**, **Sub-Assemblies**, **Products** and **Reports**. **Settings** is at the bottom of the menu. Click anywhere outside the menu to close it.
- **Tabs**: each screen opens in a tab along the top. A tab you close can be opened again from the menu. Each tab keeps its place, such as the record you were looking at and anything you haven't saved, until you close the app.
- **Back and Forward** (the arrows at the top, or **Alt+Left** and **Alt+Right**) move between the screens and records you've looked at, like a web browser.
- Changes made on one tab show up on another tab the next time you switch to it.

### Lists as folder trees

The **Raw Materials** and **Products** lists are trees of folders, like File Explorer:

- **Search** at the top of the list narrows it to matching names and opens the folders that hold them.
- **Drag** one or more items onto a folder to move them there. A notice under the list says what moved, with an **Undo** button.
- **Right-click** an item, a folder or the empty space for a menu of what you can do there. Some menu items have keyboard shortcuts: **F2** renames and **Del** deletes.
- Folders you leave open are open again the next time you use the app.

### Editing and saving

- **One Save saves the whole record**, including everything on its tabs. Until you press **Save**, nothing you type is stored.
- **Undo** (**Ctrl+Z**) and **Redo** (**Ctrl+Y**) step back and forth through the changes you've made since opening the record.
- **Revert**, on the Products screen only, puts the whole product back as it was last saved.
- **Pickers** (the boxes with a ˅ arrow, such as Category, Role or Existing Sub-Assembly or Packaging) can be typed into. Typing narrows the list to matching names and highlights the best match: the name you typed in full, or else the first name that starts with what you typed, or else the first that contains it. **Enter** or **Tab** picks the highlighted name, so "marg" and Enter picks Margarine. **Down** and **Up** move the highlight to the other names.
- **New** starts a blank record. **Delete** asks before deleting anything.
- A green notice confirms a save. A red notice explains anything the app refused, such as a missing field, a name already taken, or something that can't be deleted because it's in use.

### Working in tables

- Most tables are typed straight into. Click a cell and type. The empty row at the bottom adds a new line.
- Pickers inside tables, such as the ingredient or material pickers, suggest names as you type.
- **Drag a row**, by its left edge or any cell you aren't typing in, to change the order of recipe lines, bill of materials lines and packaging lines. The order is saved.
- **Remove Line** removes the selected line.
- Figures shown in grey are worked out by the app, so they can't be typed into.

---

## 3. First-time setup

On a new database, set things up in this order. Each step uses what the one before it made.

1. **Units of measure** (Settings → Units of Measure): the units you buy and use things in: g, kg, piece, pack, bag, cm, roll, and so on.
2. **Material categories** (Settings → Material Categories): how raw materials are grouped, such as Flour, Dairy, Boxes and Ribbons. Each category is for either ingredients or packaging.
3. **Product categories** (Settings → Product Categories): how products are grouped, such as Breads, Cakes and Pastries.
4. **Roles** (Settings → Roles): the parts of a recipe. Dough, Filling, Topping, Inclusion, Finishing and Packaging are there from the start.
5. **Costing defaults** (Settings → Costing Defaults): the labour, overhead, administrative, markup and VAT percentages that a new product starts with.
6. **Raw materials**: every purchased ingredient and packaging material with its prices, then the made and linked ingredients.
7. **Sub-assemblies**: the doughs, fillings and packaging sets shared by more than one product.
8. **Products**.

You don't have to finish each list before moving on. You can add a category or unit later, and most pickers let you add a new category just by typing its name.

---

## 4. Settings

Open **Settings** from the bottom of the navigation menu. It has five tabs. Each list tab works the same way: pick a row to edit it, or press **New**. Then fill in the form and press **Save**, or press **Delete**. Press **Close**, the **✕** at its top right, or **Esc** to close Settings. The button beside the ✕ makes the window fill the screen, and pressing it again returns the window to its normal size. You can also drag the title bar to move the window.

### Units of Measure

Each unit has a **Name** and an **Abbreviation** (for example *kilogram* and *kg*). Names are always saved in lowercase.

A unit that's in use can't be deleted. This means a price or a packaging line is in it, and the message tells you which. Renaming a unit renames it everywhere.

### Material Categories

Each category has a **Name** and a **Type**: Ingredient or Packaging. An ingredient can only go in an ingredient category, and packaging only in a packaging category.

A category's type can't change while a material is in it. A category with materials in it can't be deleted.

### Product Categories

Each category has a **Name**. Products don't need a category, and those without one are listed under **Uncategorized**. Deleting a category leaves its products uncategorized.

### Roles

Each role has:

- **Name**: what the section is called in a recipe, such as Dough or Filling.
- **Order**: where the role's sections go in a recipe. Lower numbers come first. A new role goes last. To change a role's place, pick it in the list and press **Move Up** or **Move Down** above the list. The roles are numbered 1, 2, 3… again after a move or a delete, so there are no gaps.
- **Product-owned**: a section made in a product in this role stays that product's own. It's edited in the product's recipe instead of on the Sub-Assemblies screen, and other products can copy it but not link it. Turning this on or off for a role that's already used asks first, and says how many sub-assemblies will move.
  - These are also the only roles a composite product can have sections in. So Product-owned can't be turned off while a composite has a section in the role; the message names the composites. Take the section out there first.

The **Packaging** role is special. Sub-assemblies in it are packaging sets. It can be renamed but not deleted, and it can't be product-owned.

A role can't be deleted while any sub-assembly uses it.

### Costing Defaults

These are the five percentages a **new** product's costing starts with:

- **Labor %**, **Overhead %** and **Administrative %** (office, permits, bookkeeping): each is a percentage of the product's sub-assemblies (ingredients) cost.
- **Markup %**: added on the base cost (materials, labour, overhead and administrative).
- **VAT %**: added on top of the marked-up price.

Each product keeps its own percentages from its first Save. Changing the defaults doesn't change any saved product.

---

## 5. Raw Materials

The Raw Materials screen has the list of materials on the left, and the selected material's details on the right.

### The list

The list has two top-level folders, **Ingredients** and **Packaging**, with a folder for each category inside them. Every material is in a category.

Right-click menus:

- **A material**: **Move to** (another category of the same type), **Rename…** (F2) and **Delete** (Del).
- **A type's folder**: **New Ingredient Here** or **New Packaging Material Here**, **New Category…**, **Expand All** and **Collapse All**.
- **A category's folder**: **New … Here**, **New Category…**, **Rename Category…** (F2) and **Delete Category** (Del). You can only delete a category when it's empty. Otherwise the menu shows how many materials it holds.
- **The empty space**: **New Raw Material**, **New Ingredient Category…**, **New Packaging Category…**, **Expand All** and **Collapse All**.

Dragging materials onto another category moves them straight away. A material can't be dragged into the other type.

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
- **Category**: pick one, or type a new name to add it. The app asks before adding.
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
| **Purchase Unit** | The unit you buy it in: the base unit or an alternative unit. Picking a unit the material doesn't have yet adds it as an alternative unit. |
| **Qty per Purchase Unit** | How many base units one purchase unit holds. It's filled in for saved and standard units. |
| **Purchase Price** | What one purchase unit costs. |
| **Buffer %** | Read-only. How far the buffered cost is above this brand's price. |

The material's **current cost** comes from the **dearest** brand: its price divided by the base units in its purchase unit. For example, a 25 kg bag at 1,250.00 costs 1,250 ÷ 25,000 = 0.05 per gram.

<a id="cost-buffer"></a>**Cost buffer.** Under the brand table are **Cost Buffer %**, **Buffered Cost** and **Per**:

- Typing the **Cost Buffer %** works out the **Buffered Cost** as the dearest brand plus that percentage.
- Typing the **Buffered Cost** works out the percentage.
- **Per** chooses the unit the buffered cost is shown and typed in, such as per gram or per bag. It's always stored per base unit.

The buffered cost stays **fixed** when brand prices change. The percentage then follows, showing how far the fixed amount now sits above the dearest price. If the dearest brand goes above the buffered cost, a warning says so. **To put the buffer back on top of a new price, type the Cost Buffer % again**, then Save.

On a material's first price, Save works out the buffered cost from the percentage for you.

Every Save that changes a purchased material's cost adds an entry to its **Price History**.

#### Made ingredients: Bill of Materials

List the ingredients the made ingredient is made from:

| Column | Meaning |
|---|---|
| **Component** | An ingredient: purchased, made or linked. It can't be the ingredient itself. |
| **Grams** | How much of it goes into one batch. |
| **Cost/g** | The component's buffered cost per gram. |
| **Cost** | Grams × Cost/g. |

The header's **Total Weight** is the batch's grams less the **Weight Loss %**. The **Total Cost** is the sum of the Cost column. Its buffered cost per gram is Total Cost ÷ Total Weight.

Made ingredients can be built from other made ingredients to any depth. For example, a caramel can be made from a syrup that's made from sugar. The app refuses a bill of materials that would contain the ingredient itself anywhere down the chain.

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

Add up to two alternative units. Pick the **Alternative Unit** and type its **Multiplier**, which is how many base units it holds. The multiplier of a standard unit is filled in and can't be changed.

The **Unit Conversion Overview** beside the table shows how each unit converts to the base unit.

Correcting a multiplier reprices any brand bought in that unit.

### Usage Example tab

This tab is a calculator. Type a **Use Quantity**, pick a unit and press **Calculate**. It shows the quantity in the base unit, the cost per base unit, the calculated cost, the buffer, and the buffered cost.

### Used By tab

This tab lists every product the material ends up in as tiles, including products that use it indirectly, such as through a made ingredient, a linked part or a packaging set. An ingredient's list includes every composite product holding a product that uses it. Click a tile to open that product.

### Price History tab

This tab is shown for saved purchased materials only. It's a log of every price the material has been costed at: **Date**, **Brand**, **Unit**, **Price** and **Cost / Base Unit**.

**Delete Price** removes a log entry only. It doesn't change the material's cost.

---

## 6. Sub-Assemblies

The Sub-Assemblies screen keeps the sub-assemblies that products **link**: shared doughs, fillings, toppings and packaging sets.

Sections that belong to a single product, in a product-owned role, aren't listed here. They're edited in that product's recipe instead.

### The list

The list shows **Name** and **Role**, with a search box. When a product saves a new section, it's saved here under a name made from the product and the role, such as *Cheese Roll Dough*. If the section has its own name, that's added after them, as in *Cheese Roll Filling: Ube*.

### The form

At the top are the name and **New**, **Copy**, **Save**, **Undo**, **Redo** and **Delete**. Below them:

- **Name** and **Role**.
- **Batch Weight**, **Batch Cost** and **Cost per g**, worked out from the lines as you type.
- **Lines**: **Ingredient**, **Grams**, the Baker's % or Absolute %, **Cost/g**, **Cost** and **Base** (tick it for baker's percentages).
- **Used By**: a tile for each product that links this sub-assembly. Click one to open it.

**Copy** starts a new, unsaved sub-assembly with the same role and lines, named "… Copy". Rename it if you like, then press Save. The original stays as it was.

Changing a sub-assembly here changes every product that links it. A sub-assembly that a product links **can't be deleted**. The message names the products.

### Packaging sets

Pick the **Packaging** role to make a packaging set. The lines table then holds packaging materials instead: **Material**, **Qty**, **Unit**, **Unit Cost** and **Cost per Set**, with a **Set Cost** total.

A product whose packaging links a set takes one whole set per sellable unit. For example, *Ensaymada Box of 8* can link a set holding 8 bags and 1 box.

A sub-assembly can't be switched between a recipe role and the Packaging role once it's saved.

---

## 7. Products

The Products screen has the product list on the left. On the right are the selected product's header, its details, and three tabs: **Recipe**, **Costing** and **Clone**.

### The list

The top-level folders are **Production** and **Research and Development**. Each has a folder for every product category, plus **Uncategorized**. Drag products between folders to change their stage or category.

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
- **The empty space**: **New Product**, **New Composite Product**, **New Category…**, **Print All Products**, **Expand All** and **Collapse All**.

**New Product Here** and **New Composite Product Here** file the new product in that folder when you save it. **New** from the header puts it in Research and Development.

A composite product shows a small icon after its name.

### The header

The header has the product's name and **New**, **Save**, **Undo**, **Redo**, **Revert**, **Print**, **Unbuffered** and **Delete**. **New** starts a product. The arrow beside it also offers **New composite product**. Below them are these figures, all **per sellable unit**:

- **Material Cost**: sub-assemblies plus packaging. For a composite product, its products' recipe cost and its own sub-assemblies, plus its own packaging.
- **Base Cost**: materials plus labour, overhead and administrative.
- **Current Price**: what the shop charges now.
- **Markup %**: what the current price actually earns on the base cost, with VAT taken out. Compare it with the markup you set on the Costing tab.
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

Deleting a product asks first. Its linked sub-assemblies stay on the Sub-Assemblies screen, and its own sub-assemblies are deleted with it. Deleting a composite product leaves the products it holds as they are. A product can't be deleted while another product's packaging links it, or while a composite product holds it.

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
- **The lines table**: **Ingredient**, **Grams**, the Baker's % or Absolute %, **Cost/g**, **Cost** and **Base**. Under it are **Remove Line** and the section's cost per piece.
- **Batch parameters**:
  - **Total weight**: the batch that goes in, including add-ins
  - **Shrinkage %**
  - **Final weight**: total weight less shrinkage
  - **Weight/piece (g)** and **Yield (pcs)**: type either one
- **Cost/piece** and **Batch cost** tiles.

You don't need a piece size to add a section or to save it. Its cost per piece reads 0 until you enter one.

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

#### Packaging

To add packaging, use **Add Sub-Assembly** and pick the **Packaging** role:

- With **New sub-assembly**, type how much of each material one sellable unit takes. The lines go on the **Packaging** tab.
- With **Link an existing one**, pick a **packaging set** or another product's packaging. The Packaging tab then shows the linked lines, and **Make a Copy** turns them into this product's own lines so you can change them.

The Packaging tab's table is typed into. Its columns are **Material**, **Qty**, **Unit** (the base unit or one of the material's alternative units), **Unit Cost** and **Cost per Unit**.

Packaging quantities are for one sellable unit. They aren't multiplied by Quantity Required. If you change Quantity Required, check the packaging yourself.

#### A composite product's Products tab

A composite product's recipe opens on its **Products** tab. After it come its own sections, such as a Topping, then **Packaging** when it has packaging.

The **Products** table is typed into, like the Packaging table:

- **Product**: type part of a name and pick it. The list leaves out the product itself, and any composite that holds it.
- **Qty**: how many of that product go in, as a whole number. It counts the product's sellable units: pieces for a product sold by the piece, and whole boxes for one like *Ensaymada Box of 8*. To put in loose pieces, pick the product sold by the piece.
- **Recipe Cost**: what one of that product brings, which is its recipe (ingredients) cost for one sellable unit.
- **Cost**: Qty × Recipe Cost.

Each product goes on one line. Drag lines into order, and use **Remove Line** to take one out. The figure at the top right is what the products come to together, without the composite's own sections.

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
- **Packaging**
- **Labor**, **Overhead** and **Administrative**, each labelled with its percentage
- **Subtotal (base cost)**
- **Markup**
- **VAT**
- **Total Selling Price**

Everything updates as you type, before you save. See [How costs are worked out](#10-how-costs-are-worked-out).

### Clone tab

The Clone tab is shown for saved products only. Type a **New Product Name**, then choose:

- **Clone**: the new product links the same sub-assemblies at the same piece sizes, and gets its **own copy of the packaging**.
- **Create Variant**: the same, except the packaging is **linked**, so the new product follows every change made to this one's packaging. Use this for a smaller or bigger version of the same product. For a boxed version, re-point its packaging at a packaging set.

With both, the product's own sub-assemblies are copied to the new product. The category, Quantity Required, Current Price and costing percentages are copied too. The new product starts in Research and Development, and you can change its piece sizes.

A composite product's clone or variant is a composite too, holding the same products at the same quantities. Its own sections are copied as any product's are.

---

## 8. Printing recipes and costings

Press **Print** in the Products header to open the **Print** tab. It shows a preview of the product's recipe and costing as the screen shows them, including changes you haven't saved yet.

- **Include**: tick **Recipe**, **Costing** or both. Without the costing, the recipe prints with no costs, which is useful for the kitchen. A composite product's recipe prints as its products table, then its own sections.
- **Print**: the drop-down chooses **This product**, the folder last asked for, or **All products**.
- The preview's own controls set the printer, paper, orientation and margins.

To print a set of products, right-click in the Products list and choose **Print Folder**, **Print Stage** or **Print All Products**.

A set prints each product **as saved**, with a contents page and each product starting on a new page. If the product open on the Products screen has unsaved changes, the Print tab warns you that it's printed as saved.

The footer on each page shows the product, when it was printed, whether it includes unsaved changes, and the page number.

While the Products screen shows unbuffered costs, everything printed, a single product or a set, is at the materials' true costs. Each product's page says **Unbuffered costs** under its title and over its costing.

Close the Print tab with its ✕.

---

## 9. Reports

Reports has two tabs along the bottom. Each is a table you can read but not edit. Click a column's heading to sort by it, and click it again to reverse the order. The figures are worked out again each time you open Reports, so they always use today's prices.

### Product Cost Summary

One row for every product, as it's saved:

- **Product**, its **Stage** (Production or Research and Development) and its **Category**.
- **Pieces**: how many pieces make one sellable unit (1 for a product sold by the piece). Every amount after it is for one sellable unit. Blank for a composite product, which is one unit made of other products.
- **Base Cost** and **Selling Price**: as the product's Costing tab works them out.
- **Current Price**: what the shop charges now, as entered on the product. Blank if none is entered.
- **Markup %**: what the current price earns on the base cost, as the product's header shows it. Blank without a current price.

Sort by **Markup %** to see which products earn the least.

Double-click a product to open it on the **Products** screen.

### Raw Materials Price Summary

One row for every purchased raw material, ingredients and packaging alike. Made and linked ingredients aren't listed, since they have no price of their own.

- **Raw Material**, its **Type** and its **Category**.
- **Price** and **Unit**: the latest price from the material's price history, and the unit it's for, such as a sack or a kilogram.
- **Change %**: how much that price moved the material's cost from the price before it. It's worked out per gram (or per base unit, for packaging), so it's right even when the two prices are for different units. Blank if the material has only one price.
- **Since**: the day the latest price started.

A material that has never been priced shows blank figures.

---

## 10. How costs are worked out

### The cascade

When a cost changes, the app carries the change upwards:

```
A purchased ingredient's price changes
  → every made or linked ingredient that uses it (all the way up the chain)
    → every sub-assembly with those ingredients in its lines
      → every product that uses those sub-assemblies
        → every composite product that holds those products

A packaging material's price changes
  → every product and packaging set that uses it
```

This happens as soon as you save. A product already open on another tab shows the new figures when you switch back to it.

### Raw materials

- **Purchased**: cost per base unit = the dearest brand's price ÷ the base units in its purchase unit. The buffered cost is a fixed amount. It's set from the Cost Buffer % on the first price, or whenever you type the percentage again.
- **Made**: final weight = the bill of materials' grams × (1 − Weight Loss %). Cost per gram = the components' total cost ÷ final weight. The buffered cost uses the components' buffered costs.
- **Linked**: cost per gram = linked-to ingredient's cost per gram × Cost Share % ÷ Weight Share %.

### Sub-assemblies

- Each line's cost = grams × the ingredient's buffered cost per gram.
- Batch weight and batch cost = the sum of the lines.

### A product's sections

- Final weight = batch weight (including add-ins) × (1 − Shrinkage %).
- Cost per piece = batch cost × weight per piece ÷ final weight. This is the same as batch cost ÷ yield.

### The selling price (cost-plus)

All figures below are per sellable unit.

1. **Sub-assemblies** = the sum of the sections' cost per piece × Quantity Required.
2. **Packaging** = the sum of the packaging lines. They're already per sellable unit.
3. **Labor**, **Overhead** and **Administrative** = their percentage **of the sub-assemblies alone**. Packaging isn't included.
4. **Base cost** = sub-assemblies + packaging + labour + overhead + administrative.
5. **Price before VAT** = base cost × (1 + Markup %).
6. **Selling price** = price before VAT × (1 + VAT %).

**Markup % at the current price**, in the header, works backwards from the current price: (current price ÷ (1 + VAT %) − base cost) ÷ base cost.

### A worked example

Say a product's sections cost **20.00** a piece, its packaging costs **2.00**, and it's sold by the piece. Its costing is Labor 25%, Overhead 10%, Administrative 5%, Markup 30% and VAT 12%.

| Step | Working | Amount |
|---|---|---|
| Sub-assemblies | 20.00 × 1 | 20.00 |
| Packaging | | 2.00 |
| Labor | 25% of 20.00 | 5.00 |
| Overhead | 10% of 20.00 | 2.00 |
| Administrative | 5% of 20.00 | 1.00 |
| **Base cost** | 20 + 2 + 5 + 2 + 1 | **30.00** |
| Markup | 30% of 30.00 | 9.00 |
| Price before VAT | | 39.00 |
| VAT | 12% of 39.00 | 4.68 |
| **Selling price** | | **43.68** |

If the shop actually charges **45.00**, the header's Markup % is (45.00 ÷ 1.12 − 30.00) ÷ 30.00 = **33.93%**.

A **section's cost per piece**, for comparison: a 1,000 g dough costing 50.00, with 10% shrinkage, has a final weight of 900 g. At 30 g a piece it yields 30 pieces, and each piece costs 50.00 × 30 ÷ 900 = **1.6667**.

### A composite product

A composite product is priced like any other product, with its products in place of sub-assemblies:

1. **Products** = the sum of its lines. Each line is its Qty × the product's recipe cost for one sellable unit, which is the **Sub-assemblies** figure on that product's own Costing tab. The products' packaging isn't included. The composite's own sections, such as a Topping, are added at their cost per piece, since one piece is one whole composite.
2. **Packaging** = the composite's own packaging.
3. **Labor**, **Overhead** and **Administrative** = their percentage of step 1: its products, and its own sections if it has any.
4. Then the base cost, markup, VAT and selling price, as above.

For example, a *Pasalubong Box* holds 6 Ensaymada, whose recipe costs **2.50** each, and 4 Cheese Rolls at **5.00** each. It has a gift box costing **12.00**, and the same percentages as the worked example above.

| Step | Working | Amount |
|---|---|---|
| Products | 6 × 2.50 + 4 × 5.00 | 35.00 |
| Packaging | the gift box | 12.00 |
| Labor | 25% of 35.00 | 8.75 |
| Overhead | 10% of 35.00 | 3.50 |
| Administrative | 5% of 35.00 | 1.75 |
| **Base cost** | 35 + 12 + 8.75 + 3.50 + 1.75 | **61.00** |
| Markup | 30% of 61.00 | 18.30 |
| Price before VAT | | 79.30 |
| VAT | 12% of 79.30 | 9.52 |
| **Selling price** | | **88.82** |

With the same percentages as its products and no packaging or sections of its own, a composite is priced at exactly what its products are priced at together.

Give the same box an icing topping that costs **5.00** for one box, and step 1 becomes 35.00 + 5.00 = **40.00**. Labor, Overhead and Administrative are then taken of 40.00. A composite held in another composite brings its whole step 1, topping included.

### Rounding

The app keeps costs at full precision and rounds them only for display. So a total can differ from the sum of the rounded figures above it by a cent.

---

## 11. How to…

### Record a price increase

1. Open **Raw Materials** and pick the material.
2. On **Cost Setup**, change the brand's **Purchase Price**, or add a row for a new brand.
3. The buffered cost stays where it was. To carry the new price into recipes with the buffer on top, **type the Cost Buffer % again**. Alternatively, type a new Buffered Cost.
4. Press **Save**. The change is logged in **Price History** and carried up to every product that uses the material.
5. Check the effect on the material's **Used By** tab, or open a product and look at its Costing tab.

### Start a new product from an existing one

- For **a different product that shares some recipes**, use **Clone** (Clone tab, or right-click → **Clone…**). It gets its own packaging.
- For **a size or pack variation** that should follow the original, use **Create Variant**. Then adjust its piece sizes, and its **Quantity Required** if it's sold as a pack.
- To **reuse just one dough or filling**, use **Add Sub-Assembly → Link an existing one** in the new product.

### See which products use an ingredient

- **Raw Materials** → pick the ingredient → **Used By** tab.
- For a shared sub-assembly: **Sub-Assemblies** → pick it → **Used By**.

### Check whether a product's price still earns enough

Open the product and compare the header's **Markup %**, which is what the current price earns, with the **Markup Metric** on the Costing tab, which is what you're aiming for. For an overview of all products, look at **Reports → Product Cost Summary** and sort by **Markup %**.

### Move a product into production

Right-click it in the Products list and choose **Promote to Production**, or drag it into a Production folder. Right-click a category folder and choose **Promote All to Production** to promote every product in that folder.

### Print the recipes of a whole category

Right-click the category's folder in the Products list and choose **Print Folder**. Untick **Costing** for kitchen copies.

### Make a composite product, such as a gift box of several products

1. In the Products header, press the arrow beside **New** and choose **New composite product**. Or right-click a folder in the list and choose **New Composite Product Here**.
2. Type its **Product Name**.
3. On the **Products** tab, add a line for each product: pick the product, then type how many go in under **Qty**.
4. Use **Add Sub-Assembly** with the **Packaging** role for the box, and for anything wrapping the pieces inside it. The products' own packaging isn't included.
5. For a topping or filling over the whole composite, use **Add Sub-Assembly** in that role. Its **Weight/piece** is what one whole composite takes.
6. Check the **Costing** tab, then press **Save**. A composite needs at least two different products.

### Give a product a box for a pack of several pieces

1. On **Sub-Assemblies**, press **New**, pick the **Packaging** role, and list the box and the bags one pack takes. Save.
2. Make the pack product. For example, use **Create Variant** of the single and set **Quantity Required** to 8.
3. In its recipe, use **Add Sub-Assembly → Link an existing one** with the **Packaging** role, and pick the set. Save.

---

## 12. Troubleshooting and questions

**"Something can't be deleted."**
It's still in use. The message says where. For example:
- A category, unit or role that something is in.
- An ingredient used in another's bill of materials, or linked by a part.
- A sub-assembly a product links.
- A product whose packaging another product links.
- A product that a composite product holds.

Move or remove what uses it first.

**"A composite product needs at least two different products."**
A composite is made of two or more different products. For more of one product, give that line a bigger **Qty**. For a pack of one product, use **Quantity Required** on a product of its own instead.

**"… can only have sections in product-owned roles …"**
A composite product's own sections can only be in a role with **Product-owned** ticked in Settings ▸ Roles, such as Topping or Filling. Pick one of those, or tick Product-owned for the role. Settings also won't untick Product-owned, nor the Sub-Assemblies screen move a sub-assembly out of such a role, while a composite has a section there: the message names it.

**A product can't go into a composite.**
The product picker leaves out the composite itself, and any composite that already holds it. Otherwise the composite would end up holding itself.

**"The name is already taken."**
Names must be unique within each list: raw materials, sub-assemblies, products, categories, roles and units. A unit's abbreviation must be unique too. Upper and lower case count as the same.

**A figure is grey and can't be typed into.**
It's worked out by the app, or fixed by a rule. Examples are a standard unit's multiplier, the Absolute %, or a linked ingredient's buffer.

**A product's cost didn't change after a price change.**
Purchased materials are costed at their **buffered cost**, which stays fixed until you type the Cost Buffer % again. See [Record a price increase](#record-a-price-increase).

**I changed Quantity Required and the packaging looks wrong.**
Packaging quantities are per sellable unit and aren't rescaled. Update the Packaging tab by hand, or link a packaging set made for that pack size.

**The app shows an error about the licence when it starts.**
The app needs a licence key for its screen components, set on the computer by whoever installed it. Ask them to check it.

**Where are the log files?**
In the **logs** folder next to the app. There's one file per day, kept for 31 days. They're useful to send along when reporting a problem.
