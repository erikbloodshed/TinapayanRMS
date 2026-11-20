# 0007. A load re-points a draft; it doesn't merge saves made elsewhere

**Status:** Accepted, 2026-11-20. Narrows ADR 0002's `Rebase`, which it supersedes in that respect only.

## Context

ADR 0002 gave each draft a `Rebase(fresh)`: "the one place a load meets the user's edits: a three-way merge of the record
as it was saved, as it's saved now, and as edited". Building Sub-Assemblies' draft (phase 1) showed what a load actually
does to a form today:

- it points what the form refers to (the role, each line's ingredient or material) at the load's fresh copies, which
  prices the lines as they now cost, and changes nothing typed;
- when the record was saved since the form took it (another PC, or a rename or move from the list), the form reads it
  again **only while nothing is typed** (`RecordScreenState.FollowSavedVersion`); with something typed, the form keeps its
  edits and the time it took, so its Save is refused rather than written over the newer save (`SavedSinceException`),
  and the refusal reads the lists again.

A three-way merge would change that: a save made elsewhere would be folded into a form with edits, field by field. That's
a behaviour the user would see, decided as one, not slipped in by a structural phase (ADR 0005: no feature work while a
screen moves).

## Decision

A draft's `Rebase` re-points it at a load's fresh copies of what it refers to, by id, and changes nothing typed:
`SubAssemblyDraft.Rebase(roleId, newWithoutRole)` takes the role again (a new draft with none takes the first) and
re-resolves each line. Following a save made elsewhere stays the form's, as it is: reread while nothing's typed, refused
at Save otherwise. Each draft's tests hold that a load changes nothing it saves, and nothing about whether it's dirty.

## Consequences

- A draft's load is small and the same on every screen: re-point, nothing else.
- Two people editing one record still meet at Save: the second is refused, reads the record's new state, and does their
  edit again. A merge, if it's ever wanted, is a feature with its own record.

## Rejected

- **The three-way merge ADR 0002 described.** A change to what users see, made without being asked for, in a phase that
  promises none; and a merge of recipe lines (which line is which, after both sides reordered them) is a design of its own.
