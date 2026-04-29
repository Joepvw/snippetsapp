---
title: GitService runs all LibGit2Sharp calls on a dedicated worker thread
date: 2026-04-28
tags: [threading, git, sync, libgit2sharp]
problem: LibGit2Sharp handles are not thread-safe; calling git operations from arbitrary threads causes intermittent crashes and corrupted state.
---

## Problem

Initial sync implementation invoked LibGit2Sharp from whatever thread happened to call `GitService`. Under bursty edits (rapid save → save → save) we saw intermittent `AccessViolationException` and corrupted index state. LibGit2Sharp's underlying native handles are not safe to use across threads, even with locks.

## Root cause

Native `git_repository` handles in libgit2 are documented as not thread-safe. Even serializing access with a `lock` is insufficient because libgit2 caches per-thread state internally.

## Solution

`GitService` owns a single dedicated worker thread and a `Channel<GitOperation>`. All public methods enqueue an op and return a `Task` that completes when the worker processes it. The worker is the only thread that ever touches the `Repository` object.

Side benefit: the persistent push-queue (offline restart) reuses the same channel — push retries are just ops with a retry policy.

## Where it lives

- [src/SnippetLauncher.Core/Sync/GitService.cs](../../src/SnippetLauncher.Core/Sync/GitService.cs) — worker thread + channel
- [tests/SnippetLauncher.Core.Tests/Sync/GitServiceTests.cs](../../tests/SnippetLauncher.Core.Tests/Sync/GitServiceTests.cs) — integration tests against real temp git repos

## Don't

- Don't expose the underlying `Repository` outside the worker thread.
- Don't add a second consumer of the channel — single-writer/single-reader is the invariant.
