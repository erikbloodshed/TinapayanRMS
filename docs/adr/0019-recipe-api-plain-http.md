# 0019. The Recipe API on plain HTTP over the shop's own network

**Status:** Accepted, 2026-11-22, the owner, asked whether the network between the two PCs is the shop's own, with no
customers or guests on it: "Trusted: record it". It closed the 2026-11-13 review's item to serve HTTPS. Recorded in
PLAN.md ("Tech Stack") and the developer manual that day; as a record after the fact on 2026-11-25, as the open item
from the ADR review of 2026-11-23 asked.

## Context

The Recipe API (`src/TinapayanRMS.RecipeApi`, since 2026-11-03) serves the recipes, ingredients, costs and prices,
read-only, to TinapayanBMS on another PC of the shop, on port 5290 across the LAN. Every request carries a key
(`X-Api-Key`, since 2026-11-03; with none set, only this PC is answered); it reads through a read-only transaction, and
installed, as `tinapayanrms_reader`, a role that can't write (2026-11-05). The installer opens the port to the local
subnet on private networks only.

Over plain HTTP the key and the costs travel unencrypted, so a key stops whoever doesn't have it, not someone who can
watch the network's traffic. The review of 2026-11-13 proposed HTTPS with a self-signed certificate the installer makes,
its thumbprint pinned in TinapayanBMS. The owner said the network is the shop's own, wired or private and
password-protected, with no customers or guests on it.

## Decision

- **The Recipe API stays on plain HTTP**, with the key, the read-only role and the firewall rule as they are.
- **If guests ever get on that network, it serves HTTPS**: a self-signed certificate the installer makes, its thumbprint
  pinned in TinapayanBMS. A change to who can reach the network revisits this record.

## Consequences

- No certificate to make, renew or pin, on either PC, and nothing more for TinapayanBMS to configure.
- Anyone on the shop's network who can watch its traffic can read the key and every cost and price sent, and with the
  key, read the rest. What they can't do is write: the API has no write, and its role is refused one by PostgreSQL.
- The decision rests on a fact about the shop, not the code, so nothing in the build or the tests checks it.

## Rejected

- **HTTPS now, with a self-signed certificate pinned in TinapayanBMS** (the 2026-11-13 review): it guards against
  someone on the network the owner says has nobody on it but the shop.
