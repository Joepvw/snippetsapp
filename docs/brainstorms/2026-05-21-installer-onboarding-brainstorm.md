# Brainstorm — Eén-klik installer + in-app update-notificatie

**Datum:** 2026-05-21
**Status:** Brainstorm afgerond, klaar voor `/workflows:plan`
**Huidige versie:** v1.0.4

## Probleem

Een tweede gebruiker meekrijgen op SnippetLauncher kost nu veel te veel moeite. De
huidige flow:

1. GitHub Releases → zip downloaden
2. Zip uitpakken naar een verstopt pad (`%LOCALAPPDATA%\Programs\SnippetLauncher\`)
3. `.exe` in een map vol DLL's en runtime-bestanden zoeken
4. Rechtsklik → Eigenschappen → "Blokkering opheffen" voor SmartScreen
5. Optioneel shortcut maken
6. Pas dan: app starten en first-run wizard

Voor een **niet-technisch publiek** (zangers, familie, vrienden) is dit een
dealbreaker. Andere Windows-apps doen dit met één setup.exe. SnippetLauncher zou dat
ook moeten doen.

De in-app first-run wizard is overigens al goed — de pijn zit puur in de
**distributie + installatie + updaten** vóór de wizard.

## Wat we gaan bouwen

**Eén `SnippetLauncher-Setup-vX.Y.Z.exe`** op GitHub Releases, gebouwd met
**Inno Setup**. De installer:

- Installeert SnippetLauncher naar `%LOCALAPPDATA%\Programs\SnippetLauncher\`
  (geen admin-rechten nodig, user-scoped)
- Maakt Start Menu shortcut, optioneel desktop shortcut
- Optioneel: aanvinken "Start bij Windows" tijdens installatie
- Werkt ook als update: installeert simpelweg over de bestaande versie heen
- Toont na installatie "SnippetLauncher starten" checkbox

**Plus in-app update-notificatie:**

- App checkt periodiek (bv. elke 24 uur, bij startup en daarna) de GitHub Releases API
- Vergelijkt huidige assembly version met latest release tag
- Bij nieuwere versie: subtiele notificatie in de tray ("v1.1.0 beschikbaar")
- Klik opent browser naar de release-download
- Gebruiker download nieuwe setup.exe, draait die, klaar

## Waarom deze aanpak

**Inno Setup** is de Windows-standaard voor lichte installers: gratis, mature,
genereert één `.exe`, vereist niets op de doelmachine (.NET runtime zit al
self-contained ingebakken in onze publish-output). Geen MSI-complexiteit, geen
WiX-jargon. Eén `.iss`-bestand checken we in onder `installer/`.

**In-app update-check via GitHub Releases API** is een paar uur werk: één HTTP-call
naar `api.github.com/repos/Joepvw/snippetsapp/releases/latest`, semver-vergelijking,
tray-notificatie. Geen Velopack/Squirrel-dependency, geen delta-updates, geen
auto-install in stilte. Gebruiker houdt expliciete controle ("ja, installeer nu").

**Waarom geen volledige auto-update (Velopack/Squirrel)?** Joep koos bewust voor
"klein scope". Een installer + update-notificatie is 90% van het ervaringsverschil
voor ~20% van de complexiteit. Auto-update kan later als MINOR versie.

**Waarom geen code-signing certificate (nu)?** Kost €200-400/jaar of een Azure
Trusted Signing-abonnement, plus signing-stap in release-flow. SmartScreen blijft dus
bij eerste install nog een hobbel — installer toont één duidelijke "Toch uitvoeren"
instructie in de README. Code-signing kan later (MAJOR-investering, niet
implementatie-werk).

## Key decisions

- **Installer-tool:** Inno Setup (niet WiX, niet MSIX, niet Squirrel/Velopack)
- **Install-locatie:** `%LOCALAPPDATA%\Programs\SnippetLauncher\` (per-user, geen admin)
- **Distributie:** GitHub Releases blijft authoritative source, naast de bestaande
  `SnippetLauncher-vX.Y.Z-win-x64.zip` komt `SnippetLauncher-Setup-vX.Y.Z.exe` te liggen
- **Update-detectie:** in-app polling van GitHub Releases API, géén delta-update
- **Update-actie:** browser openen naar release page, géén in-app download (variant C
  afgewezen — voegt te weinig toe zonder code-signing)
- **Code-signing:** buiten scope voor deze iteratie
- **Auto-update (silent):** buiten scope voor deze iteratie
- **Bestaande zip-flow:** blijft beschikbaar voor power-users die geen installer willen

## Te beslissen vóór planning

**Git-authenticatie voor niet-tech gebruikers op een privé-repo.** Dit is de
verzwegen blocker. De installer zelf lost niets op als de eerste sync alsnog
strandt op "Git for Windows niet geïnstalleerd" en "wat is een PAT?". Voor het
gestelde doel (zangers/familie aansluiten) moet deze vraag beantwoord zijn voordat
de plan-fase begint, anders bouwen we een mooie voordeur naar een afgesloten huis.

Mogelijke richtingen (de plan-fase werkt er één uit, brainstorm besluit welke):

a) **Installer kettinkt Git for Windows mee.** Detecteert ontbrekende Git, biedt
   aan om Git for Windows-installer te downloaden en te draaien. Lost Git op,
   PAT-vraag blijft.
b) **LibGit2Sharp eigen credential-flow.** Eigen PAT-invoerveld in de first-run
   wizard, opslaan via DPAPI. Verwijdert Git for Windows als requirement helemaal.
   Grotere implementatie, raakt `GitService` en wizard.
c) **GitHub Device Flow / OAuth in de wizard.** Gebruiker krijgt een code te zien
   en plakt die op github.com/login/device. Geen PAT, geen Git for Windows. Meest
   gebruiksvriendelijk, vereist een (gratis) GitHub OAuth App-registratie.
d) **Out of scope verklaren.** Installer-feature gaat door, auth-flow voor niet-tech
   wordt een separate volgende ronde. Eerlijk over wat dit project nu oplost.

## Open questions (voor plan-fase)

1. **Release-pipeline aanpassing** — De huidige `release` skill bouwt alleen een zip.
   Moet ook Inno Setup compileren (`ISCC.exe`) en de setup.exe als release-asset
   toevoegen. Toolchain-vereiste op build-machine: Inno Setup geïnstalleerd.

2. **Update-check frequentie en UX** — Hoe vaak checken? Bij elke app-start? Eens per
   24u? Hoe presenteren — pop-up, tray-balloon, statusbar-icoon? Moet "skip deze
   versie" een optie zijn? Hoe interacteert dit met `IClock` voor testbaarheid?

3. **SmartScreen "Toch uitvoeren"-instructie** — Welke vorm: een korte landing page
   op GitHub Wiki met screenshot? Een inline notitie in de release notes? Of in de
   download-knop-tekst zelf?

4. **Eerste-keer-update vanaf zip-installatie** — Bestaande gebruikers (Joep zelf
   incl.) zitten op een zip-uitgepakte versie. Werkt de installer-update daar
   overheen, of moet er een eenmalige migratie-stap zijn?

## Wat we expliciet níet doen

- Geen code-signing (kost geld, separate beslissing later)
- Geen silent auto-update (Velopack/Squirrel) — kan als MINOR feature later
- Geen admin-install / Program Files (per-user blijft het uitgangspunt)
- De in-app first-run wizard blijft ongewijzigd

## Volgende stap

Eerst de **Te beslissen vóór planning**-vraag (Git-auth voor niet-tech) afkaarten —
een korte vervolg-brainstorm of expliciete beslissing van Joep. Daarna
`/workflows:plan` voor een implementatieplan dat Inno Setup-config, GitHub Releases
API-call, semver-vergelijking, tray-notificatie, release-pipeline-aanpassing én de
gekozen auth-richting dekt.
