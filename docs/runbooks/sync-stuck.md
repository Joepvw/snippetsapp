---
title: Synchronisatie of GitHub-aanmelding herstellen
tags: [sync, git, push-queue, authentication]
last-verified: 2026-10-03
---

## Aanmelding nodig

Open systeemvak → Instellingen → Opslag en kies **Aanmelden bij GitHub**. Alleen deze actie mag een interactieve aanmelding starten. Gebruik het account dat toegang heeft tot de ingestelde repository. Achtergrondsynchronisatie opent geen aanmeldvensters en wacht bij ontbrekende credentials op deze actie.

Controleer bij **remote niet beschikbaar** zowel de URL als het account en de uitnodiging/rechten op de private repository. Dit bericht bewijst niet dat het wachtwoord fout is: GitHub kan een onbereikbare private repository ook zo presenteren. Een geslaagde leesactie bewijst nog geen schrijfrecht; controleer dat een synthetische wijziging op de remote verschijnt.

Verwijder niet routinematig alle Windows-credentials. Bewaar lokale snippets en Git-history voordat je handmatig herstel probeert. Zet geen token, gebruikersnaam of wachtwoord in de repository-URL.

## Offline of uitgeputte retries

Lokale wijzigingen blijven als commits en queue bewaard. Kies **Opnieuw proberen** voor een nieuwe poging nadat netwerk of rechten hersteld zijn; deze actie reset ook uitgeputte retries. Gebruik **Nu synchroniseren** om ophalen en pushen handmatig te starten. De app pauzeert automatisch ophalen zolang een editorconcept dirty is.

## Mislukte eerste clone met later lokaal werk

Na expliciete aanmelding kan de app beide bibliotheken samenbrengen via een tijdelijke clone. Bij overlappende bestanden kies je lokaal, remote of annuleren. De oorspronkelijke bibliotheek blijft volledig bewaard in een naastliggende `.preserved-*` map; geïmporteerde conflictbestanden staan in `.local/conflicts/`. Annuleren houdt de actieve lokale bibliotheek intact. Er wordt geen force-push gebruikt.

## Beschadigde opslag

Pushqueues staan per bibliotheek/remote in `%APPDATA%/SnippetLauncher/` (hash in bestandsnaam). Een beschadigde queue wordt apart bewaard; bij openen reconstrueert GitService pending werk uit de lokale geschiedenis. Bewaar ook het oude `push-queue.json` als dat nog bestaat: verwijder het niet als standaard herstelstap.

Instellingen worden atomair geschreven met `.bak`. Bij beschadiging probeert de app deze reservekopie, bewaart het beschadigde bestand en keert niet stil terug naar de wizard. Zonder bruikbare kopie stopt starten met een herstelmelding. Usagefouten worden vastgelegd zonder snippetinhoud te verwijderen.

## Afsluiten en map wisselen

Gebruik systeemvak → Afsluiten en bewaar het concept als daarom gevraagd wordt. Lokale saves en Git-opdrachten worden afgerond; netwerkwerk wordt geannuleerd. Een mapwijziging wordt pas actief na herstart. Tot dan blijven zoeken, opslaan en sync in dezelfde actieve map werken.

Native Git-netwerkwerk kan vóór een transfercallback nog op een OS-timeout wachten. Als de worker niet binnen 15 seconden stopt, wordt geen tweede Git-worker voor dezelfde map gestart.

## Veldverificatie vóór gebruikersrelease

Deze procedure is getest met synthetische bestanden, lokale Git-repositories en geïsoleerde helperprocessen. Controleer op twee echte Windowsgebruikers met een private testrepository: achtergrond zonder prompts, bewust aanmelden, restart zonder nieuwe login, lees- én schrijfrechten, annuleren, offline herstel en behoud van beide bibliotheken. Een fix voor een specifieke gebruiker is pas bewezen na die proef op diens pc.
