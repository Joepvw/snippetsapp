# Plans

One markdown file per non-trivial feature or refactor. Plans are the primary design artifact — write them before code, keep them concise, and reference them from PRs.

## Naming

`YYYY-MM-DD-<type>-<slug>.md` where `<type>` is `feat`, `fix`, `refactor`, `chore`, or `docs`.

Example: `2026-04-29-feat-update-checker.md`

## Template

```markdown
# <Title>

## Context
Why this change exists. The problem, the user need, what prompted it. One paragraph.

## Approach
The chosen design. Not all alternatives — just what we're doing and the key decisions.
Reference existing code with [path](path) links and `file:line` anchors.

## Critical files
- [src/SnippetLauncher.Core/...](../../src/SnippetLauncher.Core/...) — what changes here
- [src/SnippetLauncher.App/...](../../src/SnippetLauncher.App/...) — what changes here

## Verification
How to know it works end-to-end:
- Tests to add/run
- Manual steps in the running app
- CI signals to watch
```

## Notes

- Plans are not contracts — update them as understanding evolves, but keep the **Context** section stable so the "why" survives.
- When a plan ships, leave it in place as historical record. Don't delete.
- Solved problems and tricky learnings discovered during implementation belong in `docs/solutions/`, not in the plan.
