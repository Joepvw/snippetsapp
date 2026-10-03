---
title: Watcher opnieuw koppelen na staged Git-herstel
date: 2026-10-03
category: runtime-errors
tags: [filesystemwatcher, git, recovery, startup]
---

Een geslaagde staged clone-import kan de oorspronkelijke bibliotheek naar een backup verplaatsen en de clone op het actieve pad zetten. De bestaande FileSystemWatcher blijft dan mogelijk de verplaatste directory volgen. Alleen opnieuw scannen corrigeert de huidige snapshot, maar latere externe wijzigingen blijven onzichtbaar.

`SnippetRepository.LoadAllAsync` koppelt daarom de watcher vóór de scan opnieuw aan het actieve pad, onder dezelfde lifecycle-lock als disposal, en annuleert oude debouncewerkzaamheden. De single-writer channel blijft de enige mutatieroute. Externe reloads publiceren een bibliotheeksnapshot, maar geen lokale Git-savegebeurtenissen.

De regressie `Reload_AfterDirectoryReplacement_WatchesActiveDirectory` gebruikt echte Directory.Move naar `.preserved`, maakt een nieuwe map op het oorspronkelijke pad, herlaadt en verandert daarna extern een bestand. Vóór de correctie liep de waarneming na acht seconden vast; na de correctie slaagde deze proef samen met drie watcher/shutdownregressies.

Pas hetzelfde principe toe bij toekomstige atomic directory replacement: watchers volgen niet automatisch de bedoeling van een padnaam. Test een wijziging ná de directorywissel, niet alleen de eerste herlaad-snapshot.
