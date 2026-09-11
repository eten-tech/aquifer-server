# Docs directory structure

This repo follows the Aquifer-wide docs convention. See
[the spec](features/docs-directory-structure/design.md) for the full
rationale.

- `features/<slug>/` — everything about one feature or initiative:
  `proposal.md`, `design.md`, `plan.md`, `tickets/`. Only the stages that
  exist are present.
- `runbooks/` — operational procedures (deploys, rollbacks, hotfixes).
- `guides/` — process/how-to docs not tied to one feature.
- `tasks/` — standalone dated work items with no parent feature.
- Loose files at the root of `docs/` — repo-wide reference docs.
