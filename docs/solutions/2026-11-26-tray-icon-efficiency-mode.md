---
title: Tray icon put the whole process in Windows Efficiency Mode, so the popup opened slowly
date: 2026-11-26
tags: [performance, wpf, tray, startup, windows-11]
problem: The search popup took long to open, especially the first time after login and whenever the machine was busy.
---

## Problem

Opening the popup felt slow, worst on the first hotkey press. Logs showed startup taking 25 s to 9 minutes and loading 30 snippets taking up to 82 s on a busy machine.

## Root cause

`TaskbarIcon.ForceCreate()` from H.NotifyIcon has a default parameter `enablesEfficiencyMode = true`. It sets the process to idle priority plus EcoQoS, permanently, because we never show windows through the library's own `Show` helper that would switch it off. Measured on the dev laptop: the same CPU work takes 2.2x longer in that mode on an idle machine, and an idle-priority process is starved outright when builds or other sessions keep the cores busy.

On top of that the first popup paid all one-time WPF costs (render pipeline, fonts, templates, JIT) at the moment the user pressed the hotkey.

## Solution

- `ForceCreate(enablesEfficiencyMode: false)`.
- Prewarm right after startup: one warm-up query plus a throwaway copy of the popup rendered off-screen without activation.
- `PublishReadyToRun` for the published build.
- Timing lines in the log (startup phases, "Search popup usable in N ms") so the next complaint starts with numbers.

Measured (idle machine): first popup usable in about 600 ms before, 65 to 250 ms after.

## Where it lives

- [src/SnippetLauncher.App/App.xaml.cs](../../src/SnippetLauncher.App/App.xaml.cs): `BuildTrayIcon`, `PrewarmSearchAsync`
- [src/SnippetLauncher.App/Views/SearchPopupWindow.xaml.cs](../../src/SnippetLauncher.App/Views/SearchPopupWindow.xaml.cs): `PrewarmAsync`

## Check

Task Manager shows a green leaf next to a process in Efficiency Mode. `(Get-Process SnippetLauncher.App).PriorityClass` must say `Normal`, not `Idle`.
