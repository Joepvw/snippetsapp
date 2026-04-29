---
title: Recover a snippet after a conflict-resolution backup
tags: [storage, conflicts, recovery]
last-verified: 2026-04-29
---

## Symptom

A snippet you edited recently now contains content from another machine (or vice versa). Or it disappeared entirely. Sync ran a last-writer-wins resolution and one version was overwritten.

## Diagnose

1. Look in `<snippets-dir>/.local/conflicts/`. Every conflict-resolution writes the **losing** version here with a timestamp suffix: `my-snippet.2026-04-29T14-30-00.md`.
2. If the file isn't there, fall back to git history:
   ```
   git -C <snippets-dir> log --all --oneline -- "<snippet-filename>.md"
   git -C <snippets-dir> show <commit-sha>:<snippet-filename>.md
   ```
   Every save is a commit, so any version that ever lived on this machine or was pulled is recoverable.

## Fix

**From the conflicts backup (preferred):**
1. Open `<snippets-dir>/.local/conflicts/<filename>.<timestamp>.md` in any editor.
2. Compare with the current `<snippets-dir>/<filename>.md`.
3. Either:
   - Replace the current file with the backup (if the backup was the version you wanted), or
   - Manually merge the two into a new combined version.
4. Save. The repository's normal save path commits and pushes.

**From git history:**
1. `git -C <snippets-dir> log --all --oneline -- "<snippet>.md"` to find the SHA.
2. `git -C <snippets-dir> show <sha>:<snippet>.md > /tmp/recovered.md`.
3. Inspect `/tmp/recovered.md`, then copy its content into the live file.

**If the snippet is fully gone:** create a new one with the same slug and paste recovered content. Don't try to revive it via `git checkout` — the app's index won't know about a file that wasn't created through the normal save path.

## Prevent

- Don't edit the same snippet on two machines at the same time. The sync window is seconds, not real-time.
- Periodically clean out `<snippets-dir>/.local/conflicts/` once you've confirmed nothing's needed — these accumulate forever otherwise. Safe to delete after a few weeks.
- For high-value snippets, add a deliberate `## changelog` section at the bottom and date your edits so the merge is human-readable.
