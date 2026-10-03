# Meetprotocol

Run vanaf repository-root: `dotnet run --project scripts/MeasureCorePerformance -c Release`.
Gebruik voor baseline hetzelfde programma met `-p:CoreProject=<absoluut pad naar baseline Core.csproj>` en aparte `--artifacts-path` buiten de source-map. Vergelijk op dezelfde machine. Sluit andere zware taken voor een definitieve gebruikersmeting.

De harness maakt uitsluitend een eigen tijdelijke bibliotheek en verwijdert die na afloop. Per 0/100/1000/5000 bestanden zijn er drie loads, één querywarmup en 30 querysamples. Het simuleert een gesorteerde lijstrefresh bij de oorspronkelijke `SnippetChanged`-events of de nieuwe `LibraryChanged`-snapshot. JSON bevat alle runs; p95 is sample 29 van 30 na sorteren (nearest rank).

Deze proef meet lokale Core-load en warme Query, geen processtart, tray, fysieke hotkey, inputfocus, echte GCM of cold filesystemcache. De editor is in de nieuwe app bovendien lazy; een gesloten editor voert deze lijstrefresh niet uit.

Bronbaseline: e795fa130e8f643ef15cac9949de67fa8a19ab8f. Machine 2026-10-03: Windows, Intel Core 7 240H, 32 GiB RAM, .NET SDK 10.0.203, Release. Tijdens de baseline liepen ook tests/toolprocessen. Daarom zijn absolute ratios indicatief; een gecontroleerde cold/warm veldproef blijft nodig.
