# Brainstorm: Snippet Launcher (Windows-first)

**Datum:** 2026-04-28
**Status:** Brainstorm afgerond

## Wat we bouwen

Een lokale Windows-applicatie waarmee je snel een opgeslagen stuk tekst (URL, alinea, FAQ-antwoord, WABA-template) kunt vinden en naar het klembord kopiëren, zodat je het in elke andere applicatie kunt plakken. Snippets zijn niet meer applicatie-gebonden zoals iOS-vervangers, WhatsApp Business snelle antwoorden of Wispr Flow snippets.

## Kernbeslissingen

| Onderwerp | Keuze |
|---|---|
| **Activering** | **V1: alleen globale hotkey + zoek-popup.** Trigger-woorden uitgesteld naar latere fase (te risicovol: low-level keyboard-hooks, antivirus-issues). |
| **Zoeken** | Eén fuzzy search-balk over titel, tags én inhoud (Alfred/Raycast-stijl) |
| **Plak-actie** | Alleen naar klembord — gebruiker plakt zelf met Ctrl+V (voorspelbaar, werkt overal) |
| **Database delen** | Git-repository als backend (snippets als bestanden, versiebeheer, conflict-resolutie via Git) |
| **Tech-stack** | Native Windows: C# / .NET WPF |
| **Platformscope v1** | Alleen Windows lokaal. Mac & iOS expliciet uit scope (latere fase). |

## Must-haves voor v1

- **Globale hotkey** opent een centraal popup-zoekvenster boven elke applicatie
- **Fuzzy search** op titel + tags + inhoud
- **Klembord-only** output (geen auto-paste in v1)
- **Variabelen / placeholders** in snippets (`{naam}`, `{datum}`) — invul-prompt vóór kopiëren
- **Gebruiksstatistieken**: recent/vaak gebruikt bovenaan
- **Quick-add vanuit clipboard**: hotkey die huidige clipboard-inhoud als nieuwe snippet opslaat
- **Git-sync** met een tweede Windows-gebruiker (gedeelde repo)

## Waarom deze aanpak

- **Hybride activering** dekt twee gebruikspatronen: snippets die je uit je hoofd kent (triggers) én ontdekken/zoeken bij grotere bibliotheken (hotkey + fuzzy search).
- **Klembord-only** voorkomt gedoe met focus-tracking en simulatie van toetsaanslagen, en werkt 100% betrouwbaar in elke Windows-app inclusief WhatsApp Web, browsers en native apps.
- **Git als sync-backend** geeft gratis versiebeheer, conflict-merging en geschiedenis zonder eigen server. Past goed bij snippets als bestanden (JSON of Markdown per snippet, of één SQLite-export — open).
- **Native WPF** geeft de beste Windows-integratie voor globale hotkeys, system tray, lage latency en kleine installer. Acceptabele trade-off: Mac-versie wordt later een herbouw, maar dat is bewust uitgesteld.

## Bewust uitgesteld (YAGNI)

- **Trigger-woorden / system-wide expansion** (low-level keyboard hook) — naar v2. In v1 alleen hotkey-popup.
- Mac- en iOS-applicaties
- Auto-paste in vorige app (focus-tracking)
- Rich text / opmaak in snippets — v1 is platte tekst
- Eigen sync-server, CRDT's, real-time collaboratie
- Categorieën / mappen — fuzzy search dekt v1; structuur kan later
- Korte aliases als apart concept — fuzzy search vangt dit op

## Open vragen

1. **Snippet-opslagformaat in Git**: één bestand per snippet (Markdown met frontmatter) vs. één JSON/SQLite-bestand? Trade-off: per-bestand = nettere diffs en handmatige edits; één bestand = simpeler beheer.
2. **Trigger-detectie**: globale keyboard hook (vereist verhoogde rechten / antivirus-vriendelijkheid) vs. een opt-in mechanisme. Hoe omgaan met false positives?
3. **Placeholder-UI**: inline invullen in popup vs. apart formulier vóór kopiëren?
4. **Welke hotkey** als default (Ctrl+Shift+Space? Win+;?). Conflictrisico met andere tools.
5. **Git-flow voor niet-technische tweede gebruiker**: hoe maken we `git pull` / `push` onzichtbaar (auto-sync bij start/save)?

## Marktonderzoek: bestaat dit al?

Kort antwoord: **er is niets dat alle vereisten in één tool combineert.** De markt splitst zich in drie kampen:
1. **Tekst-expanders** (trigger → vervangen): Espanso, Beeftext, PhraseExpress
2. **Snippet-managers met zoekvenster**: Lintalist, PhraseExpress
3. **Clipboard-managers** (geschiedenis + favorieten): Ditto, CopyQ, ClipClip, PastePilot

De combinatie *fuzzy-search popup + triggers + Git-syncbare bestanden + placeholders* zit het dichtst bij **Espanso** en **Lintalist**, maar geen van beide is een gepolijste WPF-app met een mooie zoek-popup én ingebouwde Git-flow voor niet-technische gebruikers.

### Top 5 dichtstbijzijnde alternatieven

| Functie / app | **Espanso** | **Lintalist** | **PhraseExpress** | **Beeftext** | **Ditto** |
|---|---|---|---|---|---|
| Windows lokaal | ✅ | ✅ | ✅ | ✅ | ✅ |
| Globale hotkey + zoek-popup | ⚠️ via search-bar (basic) | ✅ (fuzzy + bundles) | ✅ | ❌ (alleen triggers) | ✅ (clipboard-history) |
| Trigger-woorden (systeembreed) | ✅ | ✅ | ✅ | ✅ | ❌ |
| Fuzzy search | ⚠️ beperkt | ✅ | ✅ | ❌ | ⚠️ basic |
| Placeholders / variabelen | ✅ (forms, vars) | ✅ | ✅ | ❌ | ❌ |
| Klembord-only output (geen auto-paste) | ⚠️ (paste-default) | ✅ optie | ⚠️ | ❌ | ✅ |
| **Bestandsgebaseerd → Git-syncbaar** | ✅ (YAML) | ✅ (txt-bundles) | ❌ (eigen DB) | ❌ | ⚠️ (SQLite, gedeeld lastig) |
| Ingebouwde sync voor 2e Windows-gebruiker | ❌ (handmatig/Dropbox) | ❌ (handmatig) | ⚠️ (paid team) | ❌ | ⚠️ (LAN-sync) |
| Quick-add vanuit clipboard | ❌ | ⚠️ via plugin | ✅ | ❌ | ✅ (kerntaak) |
| Gebruiksstatistieken / recent-bovenaan | ❌ | ⚠️ beperkt | ✅ | ❌ | ✅ |
| Mac-port mogelijk (latere fase) | ✅ native | ❌ (AHK = Windows-only) | ✅ | ❌ | ❌ |
| Gratis / open source | ✅ FOSS | ✅ FOSS | ❌ €€ | ✅ FOSS (stagnant) | ✅ FOSS |
| Modern UI | ❌ CLI/YAML-first | ❌ AHK-stijl | ⚠️ gedateerd | ⚠️ basic | ⚠️ basic |

### Conclusie

- **Espanso** dekt triggers + placeholders + YAML-bestanden (perfect voor Git), maar mist een mooie fuzzy-search popup als primaire UI en heeft geen ingebouwde Git-flow. Bovendien is het paste-gericht, niet klembord-only.
- **Lintalist** komt het dichtst bij het zoek-popup-idee mét placeholders en bestand-gebaseerde bundles. Nadelen: AutoHotkey-look, Windows-only voor altijd, en geen Git-integratie.
- **PhraseExpress** is functioneel het rijkst, maar gesloten DB, betaald, geen Git-sync.
- **Beeftext** is te beperkt (geen popup-zoek, geen placeholders, niet meer onderhouden).
- **Ditto** is een clipboard-history tool, niet primair een snippet-bibliotheek.

**Geen enkele tool combineert: moderne fuzzy-search popup + triggers + Git-backed sync zonder gedoe + klembord-only + placeholders.** Daar zit de ruimte voor deze app.

### Implicatie voor de plannen

Overweeg vóór planning:
1. **Bouwen of forken?** Espanso (Rust) als engine + eigen WPF-zoek-popup eromheen kan een snellere route zijn dan vanaf nul. Maakt latere Mac-port ook makkelijker.
2. **USP scherp houden**: de Git-sync-zonder-gedoe + nette zoek-UI is de echte differentiator t.o.v. bestaande tools.

Bronnen:
- [Espanso - Privacy-first text expander](https://espanso.org/)
- [Lintalist - Snippet manager met bundles](https://lintalist.github.io/)
- [Best Beeftext Alternatives 2026 - TextExpander blog](https://textexpander.com/blog/best-beeftext-alternatives)
- [Best Espanso Alternatives 2026 - TextExpander blog](https://textexpander.com/blog/best-espanso-alternatives)
- [Best Clipboard Managers Windows 2026 - techpp](https://techpp.com/2026/04/02/best-clipboard-managers-for-windows/)
- [Lintalist op GitHub](https://github.com/lintalist/lintalist)

## Volgende stap

**Vastgesteld pad voor V1:** from-scratch **C# / .NET WPF**-applicatie. UI-controle is leidend; triggers zijn uit scope. Geschat: 2-4 weken parttime voor MVP.

`/workflows:plan` om implementatieplan op te stellen, met focus op:
- Hotkey-popup architectuur (`RegisterHotKey` + WPF-window lifecycle)
- Snippet-opslagformaat (één Markdown/JSON per snippet vs. één bestand) → bepaalt Git-diff kwaliteit
- Git-sync flow voor niet-technische gebruiker (auto-pull bij start, auto-commit+push bij save, last-write-wins)
- Placeholder-engine (template-syntax + invul-dialoog)
- Fuzzy search library-keuze (FuzzySharp vs. eigen)
