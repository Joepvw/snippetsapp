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
- `BringToForeground` returns at once when the window already is the foreground window. Otherwise the retry at `ApplicationIdle` taps ALT a second time, right when the user starts typing, and a key pressed between that ALT-down and ALT-up arrives as Alt+key.

Measured through the IPC route (no modifier held, the hard case), typing "zq" the moment the popup has the foreground: before, it arrived as "q" on the first open (2 of 2); after, "zq" arrives on every open (8 of 8), and the first open is typeable 77 to 100 ms after the trigger instead of 507 to 874 ms.

## Where it lives

- [src/SnippetLauncher.App/Views/SearchPopupWindow.xaml.cs](../../src/SnippetLauncher.App/Views/SearchPopupWindow.xaml.cs): `ShowAndActivate`, `SuppressKeyMenu`
- [src/SnippetLauncher.App/Services/GlobalHotkeyService.cs](../../src/SnippetLauncher.App/Services/GlobalHotkeyService.cs): `BringToForeground`
- Related: [2026-04-28-win11-24h2-hotkey-alt-trick.md](2026-04-28-win11-24h2-hotkey-alt-trick.md)
