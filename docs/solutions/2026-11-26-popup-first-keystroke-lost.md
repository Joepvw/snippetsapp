---
title: First keystrokes after opening the popup were lost
date: 2026-11-26
tags: [hotkey, focus, wpf, win32]
problem: After the popup opened, typing did not always work immediately; the first character (or everything until Esc) was swallowed.
---

## Problem

The popup was visible and in the foreground, but the first typed character did not arrive in the search box. On the first open of a session typing only worked after roughly half a second.

## Root cause

Two things, both tied to the foreground workaround in `GlobalHotkeyService.BringToForeground`:

1. The workaround taps ALT. When that tap reaches the popup without another modifier held (tray click, second instance, or a slow open where Ctrl+Shift was already released), Windows sends `WM_SYSCOMMAND / SC_KEYMENU` and the window enters menu mode. The next keystrokes go to a menu that does not exist.
2. `Visibility = Visible` only queues the show. On the first show the HWND did not exist yet, so `BringToForeground` ran on a zero handle and focus arrived only with the retry at `ApplicationIdle`.

## Solution

- The popup ignores `SC_KEYMENU` (window hook in `OnSourceInitialized`).
- The first show calls `Show()` synchronously, so the handle exists before foregrounding. This also keeps the centred position independent of how fast the first query returns.

Measured through the IPC route (no modifier held, the hard case): before, "zq" typed right after foreground arrived as "q" on the first open; after, "zq" arrives on every open, and the popup is typeable about 75 to 175 ms after the trigger instead of 500 to 870 ms.

## Where it lives

- [src/SnippetLauncher.App/Views/SearchPopupWindow.xaml.cs](../../src/SnippetLauncher.App/Views/SearchPopupWindow.xaml.cs): `ShowAndActivate`, `SuppressKeyMenu`
- Related: [2026-04-28-win11-24h2-hotkey-alt-trick.md](2026-04-28-win11-24h2-hotkey-alt-trick.md)
