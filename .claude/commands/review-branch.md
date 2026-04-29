---
description: Spawn 4 specialized review agents in parallel against the current branch diff
---

You are coordinating a parallel multi-agent review of the current git branch before it ships. Do **not** review the code yourself — your job is to dispatch specialists, collect their findings, and present a prioritized punch list.

## Step 1 — Gather diff context

Run in parallel:
- `git status`
- `git log master..HEAD --oneline` (commits on this branch)
- `git diff master...HEAD --stat` (files changed)

If the branch *is* master with uncommitted changes, use `git diff HEAD` and `git status` instead.

If there are no changes vs master, stop and tell the user.

## Step 2 — Spawn 4 reviewers in parallel

Send a single message with **four** Agent tool calls. Each agent should:
- Read the diff itself (`git diff master...HEAD`) plus any files it needs.
- Return findings as a list with severity (P1 critical / P2 important / P3 nice-to-fix), file:line references, and a concrete suggested fix.
- Stay under 400 words.

The four agents:

1. **`compound-engineering:review:security-sentinel`** — focus: clipboard handling, file IO paths, git credentials, log redaction, P/Invoke surface, deserialization (YAML frontmatter, settings.json).

2. **`compound-engineering:review:architecture-strategist`** — focus: Core ↔ App boundary (no WPF refs leaking into Core), DI seams (`IClock`, `IClipboardService`, `IDialogService`, `ICommandBus`, `IGitService`, `IGlobalHotkeyService`), threading invariants (GitService single-worker, SnippetRepository single-writer Channel).

3. **`compound-engineering:review:performance-oracle`** — focus: search index allocation, debouncing (popup search 150ms), startup time, memory ceiling (<150MB target), unnecessary file reads, large LINQ chains, async/await correctness.

4. **`compound-engineering:review:code-simplicity-reviewer`** — focus: YAGNI in WPF ViewModels, premature abstractions, dead code, comments that restate the code, error handling for impossible scenarios.

In each agent prompt, paste:
- The branch name and commit list from Step 1.
- The list of changed files.
- A pointer to `CLAUDE.md` (architectural invariants) and `docs/solutions/` (prior learnings) so they ground their findings.
- An instruction to ignore findings already covered by `docs/solutions/` entries (don't re-flag known trade-offs).

## Step 3 — Consolidate

After all four agents return, present a single prioritized list:

```
## P1 — must fix before shipping
- [security] file:line — finding (suggested fix)

## P2 — should fix
- [arch] file:line — finding

## P3 — nice to fix
- [simplicity] file:line — finding
```

End with a one-line ship recommendation: **Ship / Fix P1s first / Needs discussion**.

Do not auto-fix anything. The user decides what to address.
