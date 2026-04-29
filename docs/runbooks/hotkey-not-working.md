---
title: Global hotkey doesn't trigger or popup appears behind another app
tags: [hotkey, win32, windows-11]
last-verified: 2026-04-29
---

## Symptom

Pressing the global hotkey (default `Ctrl+Shift+Space` for search, `Ctrl+Shift+N` for quick-add) does one of:

1. Nothing at all — no popup, no tray feedback.
2. Popup appears but stays *behind* whatever app you were focused on.
3. Popup appears but the search box doesn't get keyboard focus — typing goes to the previous app.

## Diagnose

1. Open `%APPDATA%/SnippetLauncher/log/app.log`, grep for `GlobalHotkeyService` and `RegisterHotKey`.
2. Match the symptom:
   - **Nothing at all + log shows `RegisterHotKey failed` (HRESULT `-2147418113` / `0x8000FFFF`)** — another app already owns this hotkey combo. Common culprits: PowerToys, AutoHotkey, Logitech Options.
   - **Nothing at all + log is silent on the keypress** — the app isn't running, or the foreground app is an elevated (admin) process and we're not. Win32 hotkeys don't propagate from non-elevated to elevated.
   - **Popup behind / no focus** — Windows 11 24H2+ foreground-stealing hardening. The ALT-trick fallback should kick in but may have failed silently.
3. Check Settings → Hotkeys: is the binding what you expect? Live-rebinding sometimes leaves stale state if the app errored mid-rebind.

## Fix

**Conflict with another app:**
1. Open Settings → Hotkeys → rebind to a different combo.
2. Or: identify and disable the conflicting app's binding. PowerToys Keyboard Manager and Run both eat common combos.

**Elevation mismatch:**
1. If you regularly work in an elevated terminal/IDE, run SnippetLauncher elevated too: right-click the shortcut → Run as administrator.
2. Or accept the limitation and use the tray icon to open the popup when in elevated apps.

**Win11 24H2 foreground issue:**
1. Restart SnippetLauncher fully (tray → Quit, then relaunch). The ALT-trick state can desync after sleep/wake.
2. If it persists, file a note in [docs/solutions/](../solutions/) — Microsoft may have tightened foreground rules further and the workaround needs an update. Reference [docs/solutions/2026-04-28-win11-24h2-hotkey-alt-trick.md](../solutions/2026-04-28-win11-24h2-hotkey-alt-trick.md).

**Stale rebind state:**
1. Quit the app.
2. Delete the `Hotkeys` block from `%APPDATA%/SnippetLauncher/settings.json` (or restore defaults via Settings → Reset hotkeys).
3. Restart.

## Prevent

- After a Windows feature update, test both hotkeys deliberately — Microsoft's foreground rules change between releases.
- Avoid binding to combos that PowerToys / Logitech / NVIDIA overlay use by default.
