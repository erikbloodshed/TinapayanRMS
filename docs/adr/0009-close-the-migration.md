# 0009. The migration closes: what it dropped, and what stays open

**Status:** Accepted, 2026-11-22, the owner choosing "Drop a and b, keep c" of the three below. Narrows ADR 0002, ADR 0003
and ADR 0006 in what it drops, and changes nothing else in them.

## Context

ADR 0002's migration was called complete on 2026-11-21, by its phases' done-whens: the screens read and write through use
cases, each record screen edits drafts, and Presentation doesn't reference Infrastructure. That holds. A review on
2026-11-22 set the code beside the rest of what the records promised, and three promises weren't kept:

- **(a) The screens' tests without a database** (0002's consequences; 0006's "Screens" level: fakes of the editors'
  interfaces). 34 of the 66 test files in Presentation.Tests use the database. Two classes there stand in for a use
  case (`WithoutVersions`, `HeldCatalog`), and each wraps the real one. The screens' tests on real use cases pass, and
  they show what a fake can't: a Save refused by what's stored, which that day's lost-edit fixes were tested by.
- **(b) A draft's `Version`, which its figures and dirty check are cached on**, in place of every call to `RefreshDirty`
  and `InvalidateCosts` (0002; 0003 leans on it for figures that hear of every change). `SubAssemblyDraft` and
  `RawMaterialDraft` declare one and move it, and nothing reads it: their forms follow the draft's `Changed`. Products'
  drafts have none. Presentation still names `RefreshDirty` on 24 lines and `InvalidateCosts` on 23, and its figures are
  `Derived<T>`, marked stale by whoever writes.
- **(c) Random sequences with every kind of step** (0006: edits, loads with and without changes saved elsewhere, undos,
  redos, saves and tab switches). None of the seven draft test classes has a save or a tab switch as a step. Undo and
  redo come only once a sequence has ended. `ProductDraftTests` has no load. The others' loads move only costs or rates,
  except the recipe's, which also brings lines, a name, a role and ownership saved elsewhere. Its load property only
  ever moved costs until 2026-11-22, which is how a load that threw away typed edits got through (75108f8).

The records' own rule slipped too. ADR 0001 lets an accepted record change in its status alone, and three bodies were
edited afterwards: 0002's (phase 3's done-when and its amendment), 0004's (the file-size gate, local CI) and 0005's (the
prerelease sentence, local CI and its amendment).

## Decision

- **(a) is dropped.** A screen's state may be tested over the real use cases on PostgreSQL, and that's the default where
  a behaviour rests on what's stored. A fake of a use case's interface is allowed where a test needs one, to hold a read
  back or to leave one out. The drafts' tests keep to no database.
- **(b) is dropped.** A draft needn't keep a `Version`, and nothing is to be cached on one. A form follows what its drafts
  raise, and a figure is a `Derived<T>` marked stale broadly, as built. The two `Version`s can go when their drafts next
  change.
- **(c) is kept, and is open** until each draft's sequences take the steps 0006 names: undo and redo among the edits, a
  load with each kind of change that can be saved elsewhere to what the draft holds, a save, and a tab switch. A step
  only a form has, as a tab switch may be, is tested at the form. The build plan's open items hold the work.
- **ADR 0002's migration is closed.** (c) is 0006's work and reopens no phase.
- **ADR 0001 stands as written.** The edits already made stay, named above and kept in Git's history. A change to what a
  record decided is a new record, as this one is, and the old one's status says so.

## Consequences

- The records say what was built, so a later change doesn't take a dropped promise for a rule.
- The screens' tests stay slower than fakes would make them, some six minutes for Presentation.Tests in a whole run.
- What a write sets off still depends on every writer making its calls, the cost 0003 named and the `Version` was to
  remove. A missed call shows as a stale figure or dirty check, which the tests have to catch, so (c) matters the more.
- Until (c) is done, a bug in a sequence of saves, loads and tab switches is caught only by an example someone wrote.

## Rejected

- **Converting the 34 test files to fakes.** Much work to test less: the tests pass, and what they check is the meeting
  of a screen with what's stored.
- **Building the `Version` caching now.** A rewrite of how every figure and dirty check hears of a change, with no bug
  that asks for it.
- **Dropping (c) as well.** Sequences are where the bugs that lose edits have come from.
- **Keeping all three open.** Two open items nobody means to do would read as debt, and as rules for new code.
- **Allowing dated amendments to an accepted record.** It would loosen the rule that was broken, and a reader of a record
  couldn't tell what was accepted from what was added.
