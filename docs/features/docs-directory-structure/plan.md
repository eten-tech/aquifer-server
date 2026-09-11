# Docs Directory Structure Migration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Migrate `aquifer-server`'s `docs/` to the feature-grouped structure defined in [design.md](design.md), with history preserved and no broken links, then make the structure self-sustaining via an `AGENTS.md` convention pointer and a CI check. The rest of the Aquifer ecosystem is scoped but deferred (see design.md's Migration section) — each of those repos needs its own migration pass from its own checkout.

**Architecture:** Mechanical `git mv` of `aquifer-server`'s two ad hoc doc locations (`docs/processes/`, `docs/superpowers/plans/`) and the mis-slugged `docs/features/{design,plan}.md` into the correct `docs/features/<slug>/` shape, plus an `AGENTS.md` pointer and a CI structure check added to the existing `pre-merge.yml` workflow.

**Tech Stack:** git (mv, log for history/dates), markdown link grep, GitHub Actions.

**Spec:** [design.md](design.md)

## Global Constraints

- Every move uses `git mv`, never delete+recreate — history must follow the file.
- No `superpowers/` or `processes/` directory survives migration.
- Feature folder file naming follows the spec's three cases: (1) one document per stage → fixed name (`proposal.md`/`design.md`/`plan.md`); (2) multiple documents for the same stage → dated files, either flat in the feature folder prefixed with the stage name or under a `plans/`/`tickets/` subfolder; (3) a document that doesn't map onto proposal/design/plan at all → keep its original descriptive filename. See the spec's Rules section for full detail.
- Before every migration commit, audit the **whole repository**, not just `docs/`, for references to every path moved, and fix any hit. Use `git grep -n -E` with the literal old-path substrings — a substring match catches Markdown inline links (`](path)`), Markdown reference-style link definitions (`[label]: path`), HTML `href=`/`src=` links, and absolute `/docs/...` references all in one pass, regardless of wrapper syntax, anywhere in the repo (README, AGENTS.md, source comments, etc.), since `git grep` only searches tracked files and already respects `.gitignore`.
- Stay on the current working branch — do not create a new branch.

---

### Task 1: Reconcile `aquifer-server`'s docs into the new structure

**Files:**
- Move: `docs/features/design.md` → `docs/features/docs-directory-structure/design.md`
- Move: `docs/features/plan.md` → `docs/features/docs-directory-structure/plan.md`
- Move: `docs/processes/ai-translation-workflow.md` → `docs/ai-translation-workflow.md`
- Move: `docs/processes/translation-pairs-reasoning-model-fix-plan.md` → `docs/features/translation-pairs-preservation/design.md`
- Move: `docs/superpowers/plans/2026-09-11-translation-pairs-placeholder-masking.md` → `docs/features/translation-pairs-preservation/plan.md`
- Modify: `docs/README.md` (Fluent → Aquifer wording)
- Leave in place: `docs/architecture.md`, `docs/database.md`, `docs/development.md`, `docs/ecosystem.md`, `docs/infrastructure.md` (repo-wide reference)

**Interfaces:** N/A (docs-only)

- [x] **Step 1: Perform the moves**

```bash
mkdir -p docs/features/docs-directory-structure docs/features/translation-pairs-preservation

git mv docs/features/design.md docs/features/docs-directory-structure/design.md
git mv docs/features/plan.md docs/features/docs-directory-structure/plan.md
git mv docs/processes/ai-translation-workflow.md docs/ai-translation-workflow.md
git mv docs/processes/translation-pairs-reasoning-model-fix-plan.md \
       docs/features/translation-pairs-preservation/design.md
git mv docs/superpowers/plans/2026-09-11-translation-pairs-placeholder-masking.md \
       docs/features/translation-pairs-preservation/plan.md

find docs/processes docs/superpowers -type d -empty -delete
```

- [x] **Step 2: Verify no `processes/` or `superpowers/` directory remains**

Run: `test ! -d docs/processes -a ! -d docs/superpowers && echo OK`
Expected: `OK`

- [x] **Step 3: Audit the whole repository for references to moved paths and fix relative links**

```bash
git grep -n -E 'docs/processes/|docs/superpowers/'
```
`docs/features/translation-pairs-preservation/design.md` linked to
`ai-translation-workflow.md` as a same-folder sibling — became
`../../ai-translation-workflow.md` after the move. Its `plan.md` linked to
the spec as `docs/processes/translation-pairs-reasoning-model-fix-plan.md`
— became `design.md` (same-folder sibling) after the move. Both were
already fixed as part of the move in this task.

- [x] **Step 4: Update `docs/README.md` for Aquifer**

Changed "This repo follows the Fluent-wide docs convention." to "This repo
follows the Aquifer-wide docs convention." — the rest of the file (the
`features/`/`runbooks/`/`guides/`/`tasks/` bullet list) was already
generic and needed no changes.

- [x] **Step 5: Review the diff and commit**

```bash
git status
git add -A
git commit -m "docs: migrate to feature-grouped docs structure"
```

---

### Task 2: Enforcement — `AGENTS.md` pointer + CI structure check

Per the spec's Enforcement section: the convention layer (`AGENTS.md`) only
reaches agents that read it before writing docs; the enforcement layer (CI)
is what makes compliance hold regardless of who or what touched `docs/`.
`aquifer-server` has no `AGENTS.md` yet, so this task creates one.

**Files:**
- Create: `scripts/check-docs-structure.sh`
- Create: `AGENTS.md`
- Modify: `.github/workflows/pre-merge.yml` — add a `docs-structure` job

**Interfaces:**
- Produces: `scripts/check-docs-structure.sh`, exit 0 if `docs/` matches the
  allowed shape, exit 1 with one `::error::` line per violation otherwise.

- [ ] **Step 1: Write the check script**

```bash
#!/usr/bin/env bash
set -euo pipefail

allowed=(features runbooks guides tasks)
status=0
shopt -s nullglob

for entry in docs/*; do
  name="$(basename "$entry")"
  if [[ -d "$entry" ]]; then
    ok=0
    for a in "${allowed[@]}"; do
      [[ "$name" == "$a" ]] && ok=1
    done
    if [[ "$ok" -eq 0 ]]; then
      echo "::error::Unexpected top-level docs/ directory: docs/$name — allowed: ${allowed[*]} (plus loose *.md files at docs/ root). See docs/README.md."
      status=1
    fi
  elif [[ -f "$entry" ]]; then
    if [[ "$name" != *.md ]]; then
      echo "::error::Unexpected top-level docs/ file: docs/$name — only *.md files are allowed loose at docs/ root. See docs/README.md."
      status=1
    fi
  fi
done

exit "$status"
```

Save as `scripts/check-docs-structure.sh`, then:

```bash
chmod +x scripts/check-docs-structure.sh
```

- [ ] **Step 2: Verify the script locally**

```bash
./scripts/check-docs-structure.sh && echo PASS
```
Expected: `PASS` (Task 1 already brought `docs/` into the allowed shape).
Then confirm it actually catches a violation:

```bash
mkdir -p docs/scratch-test
./scripts/check-docs-structure.sh; status=$?
rmdir docs/scratch-test
echo "exit status: $status"
[[ "$status" -eq 1 ]] && echo "PASS (correctly failed)" || echo "FAIL (expected exit 1, got $status)"
```
Expected: an `::error::` line naming `docs/scratch-test`, then `PASS (correctly failed)`.

- [ ] **Step 3: Create `AGENTS.md`**

```markdown
# AGENTS.md — aquifer-server

## Docs

See `docs/README.md` for the docs directory convention. Brainstorming and
writing-plans skill output goes to `docs/features/<slug>/`
(`proposal.md`/`design.md`/`plan.md`/`tickets/`), not the skill's built-in
`docs/superpowers/...` default.
```

- [ ] **Step 4: Add the CI job to `.github/workflows/pre-merge.yml`**

Add a sibling job under the existing `jobs:` key, matching this repo's
existing indentation and any pinned `checkout` action version its other
jobs already use:

```yaml
  docs-structure:
    name: Docs Structure Check
    runs-on: ubuntu-latest
    timeout-minutes: 5
    steps:
      - uses: actions/checkout@<match-existing-pin>
        with:
          persist-credentials: false
      - run: ./scripts/check-docs-structure.sh
```

- [ ] **Step 5: Commit**

```bash
git add scripts/check-docs-structure.sh AGENTS.md .github/workflows/pre-merge.yml
git commit -m "ci: enforce docs/ directory structure convention"
```

---

## Deferred: the rest of the Aquifer ecosystem

`content-manager-web`, `well-web`, `marketing-aquifer`,
`aquifer-api-samples`, `content-loader`, `aquifer-utilities`, and
`aquifer-tiptap` each need their own migration pass, executed from a
checkout of that repo:

1. Survey that repo's current `docs/` (or equivalent) layout.
2. Move existing docs into `docs/features/<slug>/` using the three
   file-naming cases in the spec's Rules section, via `git mv`.
3. Add `docs/README.md` (same content as this repo's) and an `AGENTS.md`
   Docs-section pointer.
4. Add the `docs-structure` CI job to whatever PR-gating workflow that repo
   already has — or a new one, if none exists.

`bible-well` is discontinued (see [ecosystem.md](../../ecosystem.md)) and
is not a priority.

This is tracked scope, not included as executable tasks here, since it
requires checkouts of those repos.

## Self-Review Notes

- **Spec coverage:** Task 1 delivers the spec's "in scope and executed now"
  migration for `aquifer-server`; Task 2 delivers both enforcement layers
  from the spec's Enforcement section; the Deferred section mirrors the
  spec's Migration section's "in scope, deferred" list exactly.
- **Placeholder scan:** no TBD/TODO placeholders.
- **Type consistency:** N/A (docs/shell/YAML only, no application code).
