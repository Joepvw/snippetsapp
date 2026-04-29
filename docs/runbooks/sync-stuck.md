---
title: Git sync no longer pushes; push-queue grows
tags: [sync, git, push-queue]
last-verified: 2026-04-29
---

## Symptom

Tray icon shows the "sync error" or "offline" state for more than a session restart. New edits save locally but don't appear on the remote. `%APPDATA%/SnippetLauncher/push-queue.json` keeps growing instead of draining.

## Diagnose

1. Open `%APPDATA%/SnippetLauncher/log/app.log` and grep for `GitService` and `push`. Look at the most recent error.
2. Common causes, in order of likelihood:
   - **Auth expired** — log shows `401 Unauthorized` or `authentication failed`. Git credentials in Windows Credential Manager (`git:https://github.com`) have expired or been revoked.
   - **Diverged remote** — log shows `non-fast-forward` or `rejected`. Someone (or another machine) pushed in between.
   - **Corrupt push-queue.json** — log shows JSON deserialization error on startup. Rare; usually only after an unclean shutdown.
   - **Network down** — log shows `Could not resolve host`. Not a bug; sync resumes when connectivity returns.
3. Check `<snippets-dir>/.git/` exists and `git -C <snippets-dir> status` works manually.

## Fix

**Auth expired:**
1. Quit the app fully (tray → Quit).
2. Open Windows Credential Manager → remove the `git:https://github.com` entry.
3. Run `git -C <snippets-dir> push` once from a terminal — it will prompt and re-store credentials.
4. Restart SnippetLauncher; the push-queue drains on next sync tick.

**Diverged remote:**
1. Quit the app.
2. From `<snippets-dir>`: `git pull --rebase`. Resolve any conflicts manually (rare for snippets — they're per-file).
3. `git push`.
4. Restart the app. Push-queue will replay; duplicates are no-ops because the commits are already there.

**Corrupt push-queue.json:**
1. Quit the app.
2. Move `%APPDATA%/SnippetLauncher/push-queue.json` to `push-queue.json.bak` (don't delete — you may need to inspect what was queued).
3. Restart. The app starts with an empty queue and will re-queue from the current diff vs. the remote on first sync.
4. If anything was pending that didn't make it, it's still in your local commits — `git push` from the snippets dir will catch it.

**Network down:** wait. Or test `ping github.com`.

## Prevent

- Don't kill the app process; use tray → Quit. The push-queue serializes properly on graceful shutdown.
- Don't manually edit files in `<snippets-dir>` while the app is running unless you're prepared to resolve a conflict — last-writer-wins will create a backup in `<snippets-dir>/.local/conflicts/`.
