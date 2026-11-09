# The Sub-Assemblies screen

Part of [PLAN.md](../../PLAN.md), the project guide: the Sub-Assemblies screen and the roles it files sub-assemblies under, as they are now. When each part came in, who asked for it and what it replaced are in the [build plan](build-plan.md)'s dated log. "The app's notes" means [maui-app.md](maui-app.md); a section named without a file (Domain Model, Application Architecture, Reactive Costing Cascade) is PLAN.md's.

**Since 2026-11-07 the screen is the Blazor page's** (`Components/Pages/SubAssemblies`), over the same ViewModel, so what it does, and why, is as these notes say. They were written for the WPF view, the screen's first, so where one names a WPF control or attached behaviour (`SfTreeView`, `SfDataGrid`, `ComboBoxAdv`, a `controls:` behaviour), the Blazor page does that part with the app's own components instead: `FolderTree` for the list, `LineGrid` for a table typed into, `PickField` for a picker, `NumberField` and `TextField` for the boxes ([maui-app.md](maui-app.md), "How it's built").

## What it keeps

- **The standalone sub-assemblies, recipes and packaging sets, which no product owns** (`SubAssemblies`/`SubAssembliesViewModel`, a tab and drawer item between Raw Materials and Products). Products link them, and follow every change saved here, or copy them, from their recipe (Domain Model). A sub-assembly in a product-owned role is that product's own, edited in its recipe and never listed here; one a product saved in any other role is listed here, named after the product and the role (`SubAssemblyRules.StandaloneNameFor`).
- **The roles are the list's folders**, ordered and marked product-owned in Settings → Roles (below). The list can add, rename and delete a role too.

## The layout

- **The list beside the form, which is in two cards, as on Products and Raw Materials.** The list (`Pages/SubAssemblies/SubAssemblyList.razor`) is as wide as its tree, 370px, and the form takes the rest.
  - **The header card**, held to `HeaderCardHeight`: the title (`HeaderTitle`: the saved name, "New sub-assembly", or "Copy of …"), then New, Copy, New Product, Save, Undo, Redo and Delete; Batch Weight, Batch Cost and Cost per g (Set Cost for a packaging set), worked out from the lines as they're typed; then Name and Role.
  - **The details card** under it scrolls while the header stays put, with two tabs along its bottom: **Recipe**, the lines, and **Dependents**, bound to `SelectedDetailsTab` (`RecipeTab`, `DependentsTab`), so the screen comes back on the tab it was left on, as Products and Raw Materials do.
- **The lines are typed straight into the table** (`IngredientLineRow`, through the grids' shared drop-down, edit-commit and add-row behaviours in Application Architecture): Ingredient, Grams, the percentage, Cost/g, Cost and Base. Ticking Base makes the percentages baker's, over the ticked lines' total; with nothing ticked they're absolute, each line's share of the batch (`PercentHeader`, `FormulaBaseCaption`). A line typed as a baker's percentage keeps it as the base changes (Domain Model). Lines can be dragged into order and put in above the current one. Remove Line acts on the table showing.
- **Dependents**: a tile for each product linking it, then one for each made ingredient made from it, over a caption saying they follow every change and block Delete (or, with none, how to link it).
  - The tiles sit in a `WrapPanel` with a fixed `ItemWidth`, so they line up as rows and columns; a long name wraps inside its tile, and a row's tiles share its height. Each looks like a file in File Explorer's icon view, an icon over the name, flat until hovered (`UsedByTilesStyle`, shared with Raw Materials' Dependents card).
  - A tile shows only the name, a product's or a made ingredient's: no icon.
  - A product's tile raises its id on `ProductOpenRequests`, and a made ingredient's on `RawMaterialOpenRequests`. `MainWindow` opens it through `ScreenNavigator.OpenOnAsync` (`ProductsViewModel.OpenProductAsync`, `RawMaterialsViewModel.OpenRawMaterialAsync`), which asks about unsaved changes on both screens and records a Back step (Application Architecture). The requests are `Subject`s, not the commands' own results: those arrive on the main-thread scheduler, which never runs in the tests.

## Save and the header's other commands

- **Save** is offered while the form differs from what was saved (`History.IsDirty`).
  - It checks `SubAssemblyValidator`, then that at least one line is complete (`IngredientLineRow.CompleteLines`, or `PackagingLineRow.CompleteLines(rows, "set")` for a set), then asks about the linking products' costs (next bullet).
  - It saves through `VersionedSaves.SaveSubAssemblyAsync`, which writes the sub-assembly (`SaveStandaloneAsync` or `SaveStandalonePackagingAsync`) and a version of each product the save changes in one transaction (Domain Model, product versions).
  - Then `LoadCommand` runs, the saved row is re-selected and the form reloaded from it, and the toast names what was saved and how many products got a version.
  - Writing the lines cascades to every made ingredient made from the sub-assembly, and refuses a line that's a made ingredient made from it, at any depth (Reactive Costing Cascade).
- **What new lines do to the linking products' costs is asked first.** Before a saved recipe's new lines are written, `SharedRecipeImpact` prices each product linking it (`GetStandaloneLinkedByAsync`, deleted ones left out), per sellable unit at buffered costs, as saved and again with the new lines in place of the sub-assembly's (`SavedProductReader.Build` over the detail with its `SubAssemblyLines` swapped).
  - When any base cost moves at 2 places, Save asks (`ConfirmDelete`), naming the first eight, biggest move first ("Pandesal x 8: 2.58 → 2.32 (-0.26)"), and saying when made ingredients made from it change too, without pricing those. No leaves the form unsaved.
  - A packaging set, a new sub-assembly and a change of name or role alone ask nothing. The screen takes the service as an optional last constructor parameter.
  - Test: `SubAssembliesViewModelVersionTests.Saving_new_lines_asks_first_with_what_they_do_to_each_linking_products_base_cost`.
- **Delete** asks first. It's refused, naming them, while a product links the sub-assembly or a made ingredient is made from it (`SubAssemblyRules.EnsureNoMadeIngredientMadeFrom`), and it clears the form only if the form showed that one (`DeleteSubAssemblyAsync`, which the list's Delete calls too).
- **Copy**, enabled while one is picked, turns the form into a new, unsaved sub-assembly holding what it shows, unsaved edits included.
  - It keeps the same role and lines (or a set's materials), is named "<name> Copy" (`SubAssemblyRules.UniqueStandaloneName`, so "Copy 2" and on when that's taken) and titled "Copy of <name>", with nothing selected in the list and Dependents empty.
  - Like New it starts a fresh history measured against a blank form, so Save is offered at once and adds it beside the original (`CopyForm`, over `CaptureSnapshot`/`RestoreSnapshot`).
- **New Product**, enabled while a recipe is picked (never a packaging set), opens Products on a new, unsaved product named after the sub-assembly that **links** it.
  - It refuses while the form has unsaved changes, since the product links the sub-assembly as saved. Otherwise it raises the id on `NewProductRequests`, and `MainWindow` opens Products through `ScreenNavigator.OpenOnAsync` with `ProductsViewModel.StartNewProductFromAsync`. That asks about the product showing there, then only records the id (`_subAssemblyToStartFrom`).
  - The end of the next `LoadCommand`, which showing the tab runs, calls `StartNewProductLinking`, so the sub-assembly is found in a fresh `_linkable` even when it was just made or renamed. It runs New, sets the name, then does what Add Sub-Assembly's Link an existing one does (the add tab, `UsageFormMode.Link`, the sub-assembly as the source, its lines loaded), with no undo step. Save is offered, and the user types the piece size.
  - Tests: two `SubAssembliesViewModelRefreshTests` cases and `ProductsViewModelRefreshTests.StartNewProductFrom_starts_a_new_product_linking_the_sub_assembly_on_the_next_load_and_Save_writes_the_link`.
- **Undo and Redo** step through the form's history (`History`, an `UndoHistory<SubAssemblyFormSnapshot>`, the same shape as Products').
  - A step is one finished change: a cell committed, a line added or removed, a field typed and left. The history is cleared when the form stops being the one it recorded: another sub-assembly picked, New, Copy, or a Save.
  - Ctrl+Z and Ctrl+Y reach it from anywhere on the screen (`UndoKeys`). Unlike Products', an undo doesn't rebase the form onto a settled snapshot.
- **Scale**, a row under the Recipe tab's lines (not for a packaging set), resizes the recipe to a weight.
  - Its picker says what's to weigh the grams typed (`ScaleOptions`: the whole batch, or the base, the ticked lines' total). `ScaleCommand` multiplies every line typed in grams by to ÷ from (`SubAssemblyRules.ScaledGrams`, rounded to the 4 stored decimals), as one undo step. A line typed as a baker's percentage keeps it and follows the scaled base (`RecomputeFormulaBase`).
  - Scaling the base with nothing ticked says so and changes nothing. The grams typed aren't part of the sub-assembly, so they're no edit, and they stay from one sub-assembly to the next.
  - A product's own sections, edited in its recipe, don't have it.
  - Tests: `SubAssemblyRulesTests.ScaledGrams_…` and two in `SubAssembliesViewModelUndoTests`.

## Packaging sets

- **Picking the Packaging role makes the form a packaging set** (`IsPackaging`, following `EditingRole`).
  - The ingredient table, its percentage caption, Scale and the batch figures give way to a packaging table with the Products Packaging tab's columns (Material, Qty, Unit, Unit Cost and Cost per Set) and a Set Cost figure.
  - Its rows are the same `PackagingLineRow`s (`PackagingLines`, `NewPackagingLine`), and Save writes them with `SaveStandalonePackagingAsync`. A form loads whichever kind of lines its role says.
- **A product whose packaging links a set takes one whole set per sellable unit.** A saved sub-assembly can't move between a recipe role and the Packaging role.
- Tests: `PackagingSetPersistenceTests` and a `SubAssembliesViewModelRefreshTests` round trip.

## The list

- **A tree one level deep: a folder for every role, in the Roles tab's order** (`SubAssemblyTree`, drawn by `FolderTree`), each holding its sub-assemblies by name and showing how many. The Packaging role's folder holds the packaging sets. A search shows only the folders holding a match, opened.
- **It's built as the Raw Materials and Products trees are** (`FolderTree`), and shares their parts:
  - `TreeSelection`, for `SelectedTreeNodes`/`SelectedTreeNode` against `SelectedRow` (`SyncTreeSelection`, `TreeSelectionChanged`).
  - `TreeLeaves` and `TreeRows`, `TreeMenuItem` and `TreeMenu`, and the move notice with Undo (`MoveNoticeBar`).
  - Drag and drop, the menus, F2, Del and Enter in `FolderTree`, through `IListTreeScreen`, which the screen implements explicitly.
  - A toolbar under the search box holds the empty space's commands.
- **The roles come in before the rows on a load**, so `Refresh` announces each folder shown and each that gets its first rows (`AnnounceExpanded`), top-level ones included. Unlike Raw Materials' type folders, none of its folders exists when the tree binds.
- **Every folder starts open, and the ones left closed are remembered** (`SubAssemblies.ClosedFolders`, by role id, through `FolderMemory`), so a role added since opens too. `SubAssemblyTree.ClosedFolderKeys` gives back the keys it was given until the first `Refresh` makes the folders, so disposing a screen never shown doesn't write the list away.
- **The ViewModel follows `Roles` through its `PropertyChanged`**, not `CollectionChanged`, for the re-entrancy reason `WatchForSave` gives.
- **A sub-assembly's menu**: Move to (the recipe roles), Rename (F2), Make In-House Ingredient… and Delete (Del). A packaging set has no Move to and no Make In-House Ingredient…, and several picked offer only Move N Sub-Assemblies to, which a set among them takes away.
  - **Dragging, or Move to, gives a role, saved at once** (`MoveSubAssembliesCommand`, `ISubAssemblyRepository.MoveAsync`). Only recipes move, among the recipe roles; a packaging set stays in the Packaging role (`SubAssemblyTree.MoveTo`/`MovesTo`, `IsPackagingSet`).
  - `MoveAsync` runs each move through `StageStandaloneAsync`, the checks Save makes on a new role (the kind kept, `EnsureDistinctWhereLinkedAsync`, and a composite that links it allowing only product-owned roles). **It saves each move before checking the next, in one transaction**, so two that a product links, moved into the same role, are checked against each other. The first refusal moves none.
  - The notice reads "Moved Ube Halaya to Filling", "Moved 3 sub-assemblies to Filling" or "Moved 3 sub-assemblies", and its Undo puts the old roles back. On the one the form shows, the Role picker follows and every undo step takes the new role (`History.RewriteAll`).
  - **Rename** (F2, or a slow second click on the name) types the name in place, as on Products (`SubAssembliesViewModel.TreeNameEdit`, `TreeNameBox`), and saves it alone (`RenameAsync`, through the same checks). On the one showing, the title and Name follow and the undo steps take it, so whatever else is typed stays to be saved.
  - **Make In-House Ingredient…** adds a made ingredient made from the recipe, saved at once (`ISubAssemblyRepository.AddMadeIngredientAsync`). The prompt (`PromptForMadeIngredient`, `MadeIngredientPrompt`) asks for the name, starting with the sub-assembly's, and one of the Made categories, read fresh each time. With no Made category it says to add one, and a name a raw material already has is refused.
  - The made ingredient has no add-ins and no weight loss, so it costs per gram what the recipe does. The recipe stays here as it is, the products linking it are left alone, and the new ingredient shows under Dependents. It's refused while the form shows that sub-assembly with unsaved changes, since the ingredient is made from it as saved.
- **A role's folder**: New Sub-Assembly Here (New Packaging Set Here on the Packaging role; `ClearForm(role)`), New Folder, Rename Role (F2), Delete Role (Del), Expand All and Collapse All.
  - **New Folder** adds a "New folder" row after the roles (`SubAssemblyTree.PendingFolder`, `IsNewFolder`, `RoleId` 0), and the name typed in it adds the role last and not product-owned (`SubAssemblyRoleRepository.AddAsync`). A name that's a role already picks that role's folder. The box's tooltip says what a role is, as the prompt did.
  - **Rename Role**, typed in place, calls `UpdateAsync` with the role's `IsProductOwned` as it is, so nothing changes owner and nothing is asked.
  - **New and New … Here add the new sub-assembly's row** (`NewSubAssemblyInList`, `SubAssemblyTree.PendingSubAssembly`, `SubAssemblyLeafNode.IsNew`): "New sub-assembly" or "New packaging set", last in its role's folder (from the header, the first role's). **The name typed goes into the form, not the database**, as an undo step that Save is offered for (`NameNewSubAssembly`): a sub-assembly needs a line before it can be saved. The row keeps the name until Save files it, or until the form leaves it (`ClearForm` ends it: New again, Copy, Don't Save, Delete). It has no menu, isn't dragged, isn't counted and loads nothing when picked. The prompts (`PromptForName`) are gone from this screen; Make In-House Ingredient keeps its own.
  - **Delete Role** reads "Delete Role (holds 2)" and is greyed while a sub-assembly in the list is in it, counting every one, not a search's matches. It isn't offered on the Packaging role, which can't be deleted. A role only products' own sub-assemblies are in is refused by the database, as on the Roles tab.
- **The empty space**: New Sub-Assembly, New Folder, Expand All and Collapse All. The roles' order and Product-owned are changed on the Roles tab.
- Tests: `SubAssemblyTreeTests`, `SubAssembliesViewModelTreeTests` (every menu, each item run as the menu runs it, a refused move, the closed folders, each name typed in place) and `SubAssemblyMovePersistenceTests`.

## Loading

- **Rows are `StandaloneSubAssemblyRow`s.** The products linking a sub-assembly and the made ingredients made from it are text properties (`DependentsText`, `MadeIngredientsText`), with the lists behind methods (`GetDependents`, `GetMadeIngredients`), because `CollectionMerge.HasSameValues` compares a row's scalar properties only: a changed list shows as a changed row through its text.
- **`LoadCommand` merges** roles, ingredients, packaging materials and rows (Application Architecture's fetch-then-merge), keeps unsaved lines (`IngredientLineRow.Resolve` reprices them), and re-points the selection by id.
  - A sub-assembly asked for before the list held it (`OpenSubAssembly`, from a linked badge on Products or a made ingredient's Recipe card on Raw Materials) is opened by the load that brings it in (`_subAssemblyToOpen`).
  - A form load counts itself (`_formLoadVersion`), so one overtaken by another pick or New drops its lines rather than adding them twice.
- Tests: `StandaloneSubAssemblyPersistenceTests`, `SubAssembliesViewModelRefreshTests` and a `ProductsViewModelRefreshTests` test that links one.

## Settings → Roles

- **The Roles tab** (`SubAssemblyRolesView`/`SubAssemblyRolesViewModel`) lists the roles in recipe order, the order the Sub-Assemblies list's folders and a product's recipe tabs follow, beside a form of Name and Product-owned. The form resets after Save and Delete, as the other lookup tabs' do (Application Architecture, the Settings dialog).
- **The order has no box: Move Up and Move Down** in the list card's header (`MoveUpCommand`/`MoveDownCommand`, off for the first and last row and with nothing picked) call `SubAssemblyRoleRepository.MoveAsync`, which swaps the role with its neighbour.
  - `AddAsync` numbers a new role one past the last, whatever it's given, and `UpdateAsync` leaves the order alone.
  - `MoveAsync` and `DeleteAsync` number every role 1…n again (ties by name, the order the tab lists them in), so the order never has a gap.
  - A move leaves the form's unsaved edits alone and keeps the role picked.
