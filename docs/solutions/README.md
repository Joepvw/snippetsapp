# Solutions

Institutional memory: each non-obvious problem we've solved gets one markdown file here, with YAML frontmatter so future sessions (human or agent) can find it by tag.

## Frontmatter schema

```yaml
---
title: <one-line summary>
date: YYYY-MM-DD
tags: [threading, wpf, git, hotkey, ...]
problem: <one-sentence problem statement>
---
```

## When to write one

Add an entry when:
- A bug fix involved a non-obvious root cause (the diff alone doesn't explain *why*).
- An architectural decision has a "we tried X first, it didn't work" story behind it.
- A platform quirk bit us (Win11 24H2 hotkey behavior, LibGit2Sharp threading, WPF dispatcher gotchas).

Don't add entries for routine work — the commit message and the code itself already explain those.

## Body structure

Keep it scannable: **Problem → Root cause → Solution → Where it lives**. Reference code with [path](path) links. Aim for under 200 words per entry.

## Discovery

Search by tag with grep:

```
grep -l "tags:.*threading" docs/solutions/*.md
```
