# Consistent `docs/` directory structure across Aquifer repos

## Problem

`aquifer-server`'s `docs/` directory grew organically: five loose
repo-wide reference files (`architecture.md`, `database.md`,
`development.md`, `ecosystem.md`, `infrastructure.md`) at the root, plus,
as of this writing, two ad hoc additions with no shared shape — a
`docs/processes/` folder (a workflow reference doc and a fix proposal) and
a `docs/superpowers/plans/` folder (an implementation plan), the latter
existing only because it's the default output location the
writing-plans skill writes to.

The other repos in the Aquifer ecosystem (see
[ecosystem.md](../../ecosystem.md) for the full map — `content-manager-web`,
`well-web`, `bible-well`, `marketing-aquifer`, `aquifer-api-samples`,
`content-loader`, `aquifer-utilities`, `aquifer-tiptap`) each keep their own
`docs/` directory (or equivalent), and there is no reason to expect they've
converged on a shared shape either — nothing has ever asked them to. Without
one, a feature's proposal, design spec, implementation plan, and any
spun-off tickets end up scattered across whichever ad hoc folders existed in
that repo at the time, hard to find as a single unit, and there's no
"default output location" any AI planning skill can be pointed at
consistently across repos.

## Goals

- One directory shape, used consistently across every repo in the Aquifer
  ecosystem.
- A feature's proposal, design spec, implementation plan, and any spun-off
  tickets live together, findable by one slug.
- Non-feature-scoped docs (ops runbooks, process guides, standalone small
  tasks) get their own top-level homes, since grouping them by feature would
  scatter things people need fast during an outage.
- AI-planning skill output (brainstorming, writing-plans) lands directly in
  the right place with no separate namespace to reconcile later.

## Non-goals

- Redesigning the content or template of any individual doc type.
- Changing how runbooks/guides are authored — only where they live.
- Migrating every Aquifer repo in one pass. `aquifer-server` is migrated as
  part of this initiative (see [plan.md](plan.md)); the rest of the
  ecosystem is scoped but deferred — see Migration below.

## Structure

```text
docs/
  <loose files>.md            # repo-wide reference docs: architecture.md,
                               # database.md, development.md, ecosystem.md,
                               # infrastructure.md, ai-translation-workflow.md,
                               # etc. Kept loose at the root — few in number,
                               # not worth a dedicated folder.
  features/
    <feature-slug>/
      proposal.md              # initial idea / suggestion (brainstorming output)
      design.md                # approved spec
      plan.md                  # implementation plan
      tickets/                 # only if the feature spun off discrete,
        YYYY-MM-DD-<item>.md   # dated work items
    assessment-<name>/         # security/risk assessments — same shape as a
                                # feature folder (their own proposal/design/etc.
                                # as applicable)
  runbooks/
    deployment/
      prod-release.md
      prod-rollback.md
      prod-emergency-hotfix.md
      ...
  guides/
    <topic>.md                 # process/how-to docs, not tied to one feature
  tasks/
    YYYY-MM-DD-<task>.md       # standalone items with no parent feature
                                # (e.g. a one-off bug fix, a lone hardening task)
```

### Rules

- A feature folder holds only the stages that actually exist for it — no
  empty placeholder files.
- File names inside a feature folder follow three cases, in order:
  1. **One document per stage:** fixed name (`proposal.md`, `design.md`,
     `plan.md`), not dated. Git history covers revisions. This is the
     required shape for anything the brainstorming/writing-plans skills
     produce going forward.
  2. **Multiple documents for the same stage** (revisions, phases, or —
     during migration — pre-existing dated files that don't collapse into
     one): dated files, either directly in the feature folder prefixed
     with the stage name (e.g. `2026-07-06-plan-review-fixes.md`) or under
     a `plans/YYYY-MM-DD-<phase>.md` / `tickets/YYYY-MM-DD-<item>.md`
     subfolder — same pattern already used for `tickets/`. New work
     defaults to the subfolder form; migrated content may keep whichever
     of the two it already had.
  3. **Documents that don't map onto proposal/design/plan at all**
     (a supplementary assessment, a review doc, a technical reference):
     keep the original descriptive filename — do not force a rename that
     doesn't fit.
- No `superpowers/` wrapper directory and no other ungrouped top-level
  folder (e.g. `processes/`). Brainstorming/writing-plans skill output is
  directed to write directly into `features/<slug>/` — see Enforcement
  below for how this is actually made to hold.
- A doc belongs in `tasks/` only if it has no parent feature. Once a task is
  understood to be part of a larger feature's work, it moves into that
  feature's `tickets/` subfolder instead. This is why the top-level dir is
  named `tasks/` rather than `tickets/` — to avoid collision with the
  per-feature `tickets/` subfolder.
- A repo that already keeps visual mockups/screenshots in a dedicated
  top-level folder (e.g. a `design/` directory of PNGs) folds that content
  into the relevant feature folder as `features/<slug>/design/*.png` rather
  than keeping it as a separate top-level tree.

## Enforcement

The structure only holds if it's followed by whoever is writing docs next —
and that's not always a Claude Code session running the superpowers skill.
Three populations need to land in the right place, and only one of them is
reachable by configuring a skill:

1. **Agents running the brainstorming/writing-plans skills.** Their default
   output locations (`docs/superpowers/specs/...`,
   `docs/superpowers/plans/...`) are defined in the skill files under
   `~/.claude/plugins/...` — global, not per-repo — but each skill documents
   an override: "user preferences for spec/plan location override this
   default." That override is only real if the agent has actually read a
   repo-level instruction saying so before invoking the skill.
2. **Any other agent or tool** (a different coding assistant, a script, a
   human) that never invokes those skills in the first place. There is no
   skill default to override for this population — telling the skill where
   to write reaches nobody here.
3. **Anyone editing docs by hand**, who follows whatever's discoverable, or
   nothing at all if there's nothing to find.

This means a single lever — configuring the skill — only reaches population
1, and only in repos that have something for it to read. `aquifer-server`
has no `AGENTS.md`/`CLAUDE.md` at all as of this writing, so there is
currently no file from which any agent could pick up the override; the same
is true for most of the ecosystem until each repo is migrated.

Two layers are needed, addressing different populations:

- **Convention layer (populations 1 and 3):** every in-scope repo gets (or
  extends) a root `AGENTS.md` with a short "Docs" section pointing at
  `docs/README.md` and stating explicitly that brainstorming/writing-plans
  output goes to `docs/features/<slug>/`, not the skill's built-in default.
  `AGENTS.md` is used rather than `CLAUDE.md` because it's the more
  tool-agnostic convention (read by multiple coding agents, not just Claude
  Code) — where a repo already has both, both get the same pointer.
- **Enforcement layer (all three populations, including population 2, which
  the convention layer cannot reach at all):** a CI check, added to each
  repo's existing PR-gating workflow, that fails a PR introducing a docs
  layout violation — a top-level `docs/` directory outside the allowed set
  (`features/`, `runbooks/`, `guides/`, `tasks/`, loose root `*.md`), most
  importantly a reintroduced `docs/superpowers/` or similar ad hoc folder.
  This is the only layer that doesn't depend on whoever changed `docs/`
  having read anything first, which is what makes "any developer agent
  follows this" actually true rather than aspirational.

`aquifer-server` already has a `pre-merge.yml` workflow the check can be
added to (see [plan.md](plan.md)). Other repos' CI setups are unverified
from here — their own migration work needs to identify the equivalent
PR-gating workflow (or add one, if none exists) before the CI check can
land there.

## Migration

Applied per repo via `git mv`, preserving history, with a short
`docs/README.md` added to each repo explaining the convention so it's
discoverable and so skill output lands in the right place going forward.

**In scope and executed now:** `aquifer-server` — see [plan.md](plan.md)
for the concrete moves, including reconciling the two ad hoc folders
mentioned in Problem above into `docs/features/translation-pairs-preservation/`
and a loose `docs/ai-translation-workflow.md`.

**In scope, deferred:** the rest of the Aquifer ecosystem —
`content-manager-web`, `well-web`, `marketing-aquifer`,
`aquifer-api-samples`, `content-loader`, `aquifer-utilities`,
`aquifer-tiptap`. Each needs its own migration pass, from a checkout of
that repo, following the same shape: survey its current `docs/` (or
equivalent) layout, move existing docs into `features/<slug>/` using the
three file-naming cases above, add `docs/README.md` and an `AGENTS.md`
pointer, and add the CI structure check to whatever PR-gating workflow that
repo already has (or a new one, if none exists). `bible-well` is
discontinued (see [ecosystem.md](../../ecosystem.md)) and is not a
priority for this migration.
