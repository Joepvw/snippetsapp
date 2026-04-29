# Runbooks

Operational fix-it procedures for when something is broken at runtime. Different from [docs/solutions/](../solutions/): solutions are **why** we built something a certain way, runbooks are **what to do right now** when it stops working.

## When to write one

Add a runbook when:
- A failure mode has bitten the user (or you) at least once and the fix isn't obvious from the symptom.
- Recovery requires touching files in `%APPDATA%/SnippetLauncher/` or `<snippets-dir>/.local/`.
- Diagnosing requires reading `log/app.log` or running specific git commands.

## Structure

Keep it scannable — under 200 words. Lead with **Symptom**, then **Diagnose**, then **Fix**, then **Prevent**.

```markdown
---
title: <one-line problem statement>
tags: [sync, hotkey, storage, ...]
last-verified: YYYY-MM-DD
---

## Symptom
What the user sees. Specific enough to match against this runbook.

## Diagnose
1. Steps to confirm this is the right runbook.
2. Files to check (paths absolute).
3. Log lines to grep for.

## Fix
1. Concrete commands or actions.
2. Expected outcome after each step.

## Prevent
What to do differently next time, if applicable. Often "nothing — this is rare."
```

## Index

- [sync-stuck.md](sync-stuck.md) — git sync no longer pushes; push-queue grows
- [hotkey-not-working.md](hotkey-not-working.md) — global hotkey doesn't trigger or popup stays behind
- [snippet-recovery.md](snippet-recovery.md) — recover a snippet after a conflict-resolution backup
