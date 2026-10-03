---
title: Synchronisatiefouten moeten de worker-task laten mislukken
date: 2026-10-03
tags: [git, sync, errors, onboarding]
problem: Een Error-status in de Git-worker werd gevolgd door een succesvol voltooide Task en een succesmelding in Instellingen.
---

## Oorzaak
De operationele catch-blokken slikten exceptions. De channel-dispatcher riep daarna `TrySetResult()` aan. Een foutstatus alleen bewijst dus geen fout voor een aanroeper die de task afwacht. Bootstrap verwarde bovendien een mislukte fetch met een geldige lege remote; pushretry behield de wachtrij maar voltooide zijn task succesvol.

## Oplossing
Clone-, fetch-, bootstrap- en pushfouten worden opnieuw geworpen naar de bestaande worker-catch. Die zet Error en voltooit de task met de exception. Achtergrondoperaties hebben geen taskcompletion en blijven door dezelfde worker afgevangen. Pushes behouden hun wachtrij en bestaande backoff; uitgeputte pogingen leveren ook een fout op. Een geldige lege remote blijft bruikbaar voor een nieuwe bibliotheek.

De first-run wizard gebruikt dezelfde Git-executablezoeker als de credentialprovider: eerst PATH, daarna Program Files/Git/bin en cmd. Een ontbrekende Git-installatie bij een ingevulde remote geeft een downloadlink; lokaal verdergaan blijft mogelijk door de remote te wissen. Instellingen geeft een algemene fout met setup-uitleg, zonder ruwe remote- of credentialinformatie in de UI.

## Bewijs
Echte tijdelijke Git-regressies detecteren de oorspronkelijke bugs: zes gerichte tests falen op de originele GitService en slagen met de fix. De volledige Core-suite en architectuurtest blijven de poorten. Voor de WPF-bronproef werden echte viewmodels, gegenereerde commands, de App-syncketen en tijdelijke Git-repositories gebruikt zonder App.OnStartup of de al draaiende gebruikersapp aan te roepen. Dit vervangt geen gebruikersproef met de toekomstige installer.
