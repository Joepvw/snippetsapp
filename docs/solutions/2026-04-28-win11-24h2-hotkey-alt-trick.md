---
title: Win11 24H2 broke RegisterHotKey — workaround uses ALT-key + AttachThreadInput
date: 2026-04-28
tags: [hotkey, win32, wpf, windows-11]
problem: After Windows 11 24H2, the global hotkey would register but the popup window would not reliably take foreground focus when triggered.
---

## Problem

`RegisterHotKey` (Win32) still fires the WM_HOTKEY message on Win11 24H2+, but `SearchPopupWindow.Activate()` no longer brings the window to the foreground reliably. The popup would appear behind the active app, or the search box wouldn't get keyboard focus.

## Root cause

24H2 tightened foreground-activation rules. A process can only steal foreground when it "owns" the foreground state — which the user's currently-focused app does, not our background tray app. Standard `Activate()` / `SetForegroundWindow` silently fails in this case.

## Solution

Two-step trick before showing the popup:

1. **Synthesize an ALT keydown** via `keybd_event` — Windows treats ALT presses as a foreground-grant signal.
2. **`AttachThreadInput`** to the foreground thread, call `SetForegroundWindow`, then detach.

Fallback chain: try plain `Activate()` first; if `GetForegroundWindow()` doesn't match after a frame, apply the ALT trick.

## Where it lives

- [src/SnippetLauncher.App/Services/GlobalHotkeyService.cs](../../src/SnippetLauncher.App/Services/GlobalHotkeyService.cs) — RegisterHotKey + activation logic
- Comment in that file references this solution doc

## Caveats

- The ALT keydown is synthetic but real — if a screen-reader or accessibility tool is listening, they may see it. Acceptable trade-off for now.
- If Microsoft tightens further, we may need to move to a UI Automation-based focus path.
