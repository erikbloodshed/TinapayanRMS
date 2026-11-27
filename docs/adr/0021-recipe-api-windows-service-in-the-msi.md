# 0021. The Recipe API as a Windows service in the app's MSI

**Status:** Accepted, 2026-11-28, the owner, asked how the Recipe API should run once it comes with the MSI: "Windows
service". It closes the 2026-11-18 architecture review's item "The Recipe API in the MSI".

## Context

The Recipe API (`src/TinapayanRMS.RecipeApi`) serves the recipes, read-only, to TinapayanBMS (ADR 0019). Until now
`scripts/Install-RecipeApi.ps1` published it from source into the Windows user's profile
(`%LocalAppData%\Programs\TinapayanRMS.RecipeApi`) and started it hidden from a Startup shortcut, as that user, so it
found the reader role's password in the user's own `pgpass.conf`. The app itself has come as an MSI since 2026-11-15,
and updates its database as it opens (`DatabaseUpdater`). The two could drift: an MSI update migrated the database while
the API, updated only when someone ran the script from a source folder, stayed behind it, and answered 503 ("behind this
version") until it was.

Putting the API in the MSI changes who runs it. A per-machine MSI can't stop a process a user started hidden, nor start
it again for that user after an update, and the API's key, its role and its log, kept beside it, can't live in Program
Files, which a user can't write. The API also ran only while someone was signed in.

## Decision

- **The MSI carries the Recipe API**, built by `scripts/Build-Installer.ps1` from a publish of the project, in a feature
  of its own (`RecipeApi`) that's off unless asked for (`msiexec /i <msi> ADDLOCAL=ALL`), as only the PC that holds the
  database runs it. An update keeps the feature, as `MajorUpgrade` migrates the features' states.
- **It runs as a Windows service**, `TinapayanRMSRecipeApi`, from `Program Files\TinapayanRMS\RecipeApi`, started at
  boot and again a minute after a failure, as its own virtual account (`NT SERVICE\TinapayanRMSRecipeApi`). The MSI stops
  it before replacing its files and starts the new one, so the app and the API move to a version together. It hosts
  through `Microsoft.Extensions.Hosting.WindowsServices` (`UseWindowsService`, which does nothing when it isn't a
  service, so `dotnet run` and the tests are as they were).
- **What it knows of the PC is in `%ProgramData%\TinapayanRMS\RecipeApi`**: its `recipeapi.json` (the key, the role),
  over the one beside it, and a `pgpass.conf` of its own (`Database:PasswordFile`), as it has no user profile. Only the
  administrators, SYSTEM and the service can read the folder; its `logs` every user can. The MSI never writes there, so
  an update or an uninstall keeps the key.
- **`scripts/Setup-RecipeApi.ps1` fills it, once, in an administrator PowerShell**: it locks the folder, sets the key
  (kept from the per-user copy when there is one, so TinapayanBMS needs no change, or made and shown) and the role on the
  settings already there, and copies that role's line from the user's `pgpass.conf`; restarts the service, and takes the
  per-user copy away only once the service itself answers, starting that copy again if it doesn't.
- **The MSI opens the port** (WiX's Firewall extension), for the service's `.exe` alone, from the local subnet, on
  private networks, as the script did, and takes the rule away with the feature.

## Consequences

- An update is one MSI, on every PC: the API can't fall behind the database the app migrated.
- The API answers from boot, whether or not anyone is signed in, and comes back on its own after a failure.
- The reader role's password is in two files: the user's `pgpass.conf` and the service's. When `New-DatabaseReader.ps1`
  gives the role a new password, `Setup-RecipeApi.ps1` has to run again, or the service answers 503.
- Copying the key and reading the setup take an administrator. The log stays readable by anyone using the PC.
- Only the administrators, SYSTEM and the service can read the data folder, but whoever owns `%ProgramData%\TinapayanRMS`
  (often the app's own user, whose first backup made it) can rename the folder aside and put another in its place, with a
  key of their own. Left as it is (the 2026-11-28 review): that user's `pgpass.conf` already signs in as the app's role,
  which can write the database, and the key is there to keep the shop's other PCs out, not the people who use this one.
- The PC that runs the API needs the ASP.NET Core Runtime 11 as well as the .NET Runtime. The MSI checks for it only
  when its Recipe API feature is being installed (an error action after costing, as a launch condition can't see a
  feature's state).
- Two WiX extensions are added beside Netfx, Firewall and Util (the service's failure actions: WiX warns that Windows
  Installer's own `MsiServiceConfig` "doesn't work as expected"), at the WiX SDK's 5.0.2.
- `Uninstall-RecipeApi.ps1` takes the feature out of the installed product and deletes the data folder. `Undo-Update.ps1
  -Msi` asks for the feature again where the service was, since an uninstall and an install don't migrate it.

## Rejected

- **Keep it at sign-in, with the MSI carrying its files** (the other option put to the owner): the MSI would have to
  stop a process it didn't start, by name, and start it again as the user who set it up, and the API would still run only
  while someone was signed in.
- **The service as LocalService, with its service SID turned on** (Windows Installer's `MsiServiceConfig`): WiX warns
  that table doesn't work as expected; a virtual account carries its own SID with nothing to configure.
- **The API's own key and role in Program Files, beside it**: a user can't write there, and the MSI would replace them
  on every update.
- **The API in every PC's install**: only one PC holds the database, and a service on the others would sign in to
  nothing.
- **A self-contained publish, so no ASP.NET Core Runtime is needed**: the owner kept framework-dependent publishes
  (2026-11-06), for their size.
