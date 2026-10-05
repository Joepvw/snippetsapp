# Snippet Launcher — Eigen snippets-bibliotheek opzetten

Deze handleiding helpt je om Snippet Launcher op te zetten met **je eigen** snippets-bibliotheek
in een eigen Git-repository. Voor meedoen met een bestaande gedeelde repo zie
[setup-second-user.md](setup-second-user.md).

## Vereisten

- Windows 10 22H2 of nieuwer (of Windows 11)
- [Git for Windows](https://git-scm.com/download/win) — alleen nodig als je wilt
  synchroniseren tussen meerdere machines
- Een GitHub-account — alleen als je wilt synchroniseren

> **Sync is optioneel.** Je kunt Snippet Launcher prima lokaal gebruiken zonder remote.
> De app initialiseert in dat geval gewoon een lokale Git-repo voor versiebeheer.

## Stap 1 — Snippet Launcher installeren

1. Ga naar de Releases-pagina: **https://github.com/Joepvw/snippetsapp/releases/latest**
2. Download `SnippetLauncher-Setup-vX.Y.Z.exe` (één bestand, geen zip uitpakken).
3. Dubbelklik het bestand. Zie je "Windows heeft uw pc beschermd", klik dan op
   **Meer info** en dan op **Toch uitvoeren** (uitleg hieronder).
4. Klik door de installer. Standaard wordt Snippet Launcher per gebruiker geïnstalleerd in
   `%LOCALAPPDATA%\Programs\SnippetLauncher\` zonder admin-rechten. Vink eventueel
   "Snelkoppeling op bureaublad maken" aan. Aan het einde kun je "Snippet Launcher starten"
   aanlaten.

> **"Windows heeft uw pc beschermd"?** Klik op **Meer info** en dan op **Toch uitvoeren**.
> Windows toont deze melding omdat de installer niet digitaal ondertekend is (een
> handtekening kost een betaald certificaat). Er is niets mis met het bestand: je kunt
> het controleren met de SHA256-controlesom onder de release.

> **Updates** verlopen later vanzelf: zodra er een nieuwe versie is, verschijnt er
> een melding in het systeemvak met een link naar de nieuwe download. Draai die
> opnieuw en je hebt de update.

> **Liever zonder installer?** Ook de `.zip` met de losse bestanden staat als alternatief
> op de Release-pagina, voor power-users. Pak hem uit naar
> `%LOCALAPPDATA%\Programs\SnippetLauncher\` en klik met rechts op
> `SnippetLauncher.App.exe` → **Eigenschappen** → onderin bij "Beveiliging" →
> vink **Blokkering opheffen** aan.

## Stap 2 — Kies waar je snippets komen te staan

Bedenk een lokale map waar je snippets-repo komt, bijvoorbeeld:

```
C:\Users\JouwNaam\Documents\Snippets
```

Je hoeft de map **niet zelf aan te maken** — de wizard doet dat. Als je hem wel zelf
aanmaakt, zorg dat hij **leeg** is (de wizard weigert niet-lege niet-git mappen).

## Stap 3 — (Optioneel) Maak een GitHub-repo voor sync

Sla deze stap over als je geen sync nodig hebt.

1. Ga naar https://github.com/new
2. Geef de repo een naam (bv. `my-snippets`).
3. Kies **Private** (snippets bevatten vaak persoonlijke teksten).
4. **Laat alle vinkjes uit** — dus géén README, géén `.gitignore`, géén license.
   De repo moet leeg zijn zodat de eerste push van je lokale machine slaagt.
5. Klik **Create repository** en kopieer de HTTPS-URL,
   bv. `https://github.com/jouwnaam/my-snippets.git`.

## Stap 4 — First-run wizard doorlopen

Start Snippet Launcher (de installer doet dat zelf als je dat aanliet, anders via het Start Menu). De wizard opent automatisch:

| Stap | Actie |
|------|-------|
| Welkom | Klik **Aan de slag** |
| Snippets-map | Klik **Bladeren…** en kies (of typ) je gewenste pad. De map wordt aangemaakt als hij niet bestaat. |
| Remote | Plak je GitHub-URL uit Stap 3, of laat leeg voor lokaal-only gebruik |
| Hotkeys | Bevestig of pas de sneltoetsen aan |
| Klaar | Klik **Voltooien** |

Bij eerste start initialiseert de app automatisch een Git-repo in de gekozen map.
Als je een remote hebt opgegeven, koppelt hij die en pusht de eerste commit.

## Stap 5 — Snippets toevoegen

Er zijn drie manieren om snippets te maken:

### A. Vanuit de app (makkelijkst)

- Druk op **Ctrl+Shift+N** om een nieuwe snippet toe te voegen vanaf je klembord, óf
- Open de zoek-popup (**Ctrl+Shift+Space**) en kies **Nieuw**.

### B. Met de hand een `.md`-bestand toevoegen

Snippets zijn gewone Markdown-bestanden in je snippets-map met een YAML-frontmatter.
Zet een nieuw bestand neer als `<snippets-map>/welkom-mail.md`:

```markdown
---
title: Welkomstmail nieuwe klant
tags: [mail, sales]
placeholders:
  - name: voornaam
    label: Voornaam klant
    default: ""
created: 2026-04-29T10:00:00Z
updated: 2026-04-29T10:00:00Z
---
Hoi {voornaam},

Welkom! We bellen je op {date} rond {time} om je vragen door te nemen.
```

De **bestandsnaam zonder `.md`** wordt het ID. De `title` is wat je in de zoek-popup ziet.
Frontmatter-velden zijn allemaal optioneel — een puur leeg `.md`-bestand werkt ook.

### Placeholders

| Token | Betekenis |
|---|---|
| `{date}` | Huidige datum (`2026-04-29`) |
| `{time}` | Huidige tijd (`14:35`) |
| `{clipboard}` | Klembord-inhoud op het moment dat je de snippet kiest |
| `{eigen_naam}` | Eigen veld — app vraagt je een waarde in te vullen |
| `{{` of `}}` | Letterlijke `{` of `}` |

## Stap 6 — Werking controleren

- Druk op **Ctrl+Shift+Space** om de zoek-popup te openen.
- Typ een snippet-naam om te zoeken.
- **Enter** kopieert; plak met **Ctrl+V** in de actieve applicatie.
- Klik het tray-icoon (rechtsonder) met rechts voor alle opties.

## Synchronisatie

Als je een remote hebt gekoppeld:

- De app pullt elke 60 seconden wijzigingen op.
- Lokale wijzigingen worden automatisch gecommit en gepusht.
- Bij conflicten wint de remote-versie; een backup van je lokale versie staat in
  `<snippets-map>/.local/conflicts/`.

Wil je later een tweede machine toevoegen? Stuur die persoon (of jezelf op een andere PC)
naar [setup-second-user.md](setup-second-user.md).

## Problemen oplossen

| Probleem | Oplossing |
|----------|-----------|
| "Windows heeft uw pc beschermd" | Klik op **Meer info** en dan **Toch uitvoeren**; zie Stap 1 |
| Wizard weigert map | Map is niet leeg en geen Git-repo. Kies een lege map. |
| Push faalt na eerste commit | GitHub-repo was niet leeg. Verwijder de remote-repo en maak hem opnieuw aan zonder README/`.gitignore`. |
| Git auth-fout | Gebruik een [Personal Access Token](https://github.com/settings/tokens) (scope: `repo`) als wachtwoord; Git Credential Manager onthoudt hem. |
| Hotkey werkt niet | Tray → Instellingen → Sneltoetsen, kies een andere combinatie |

## Logbestanden

`%APPDATA%\SnippetLauncher\log\` — stuur deze mee bij een bugreport.

## Aanmelding en latere mapwijzigingen

Kies bij een private repository **Instellingen → Opslag → Aanmelden bij GitHub**. Achtergrondwerk toont geen herhaalde loginprompts. Laat de remote leeg om alleen lokaal te werken. Een andere snippets-map wordt na herstart actief; huidige saves blijven tot dan in de oorspronkelijke map. Zie [veilig syncherstel](runbooks/sync-stuck.md).
