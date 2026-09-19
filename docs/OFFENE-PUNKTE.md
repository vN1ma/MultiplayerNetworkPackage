# Offene Punkte — Earshot Voice / Walkie-Talkie

> Definitive Liste aller offenen TODOs/Beobachtungen. Abgearbeitetes hier abhaken mit Datum,
> nicht löschen (Nachvollziehbarkeit). Zugehörige Root-Cause-Analysen: `DECISIONS.md`,
> Detail-Verlauf: `walkie-talkie-debug-history.md`.

## Audio / v16.4-Nachtests

- [ ] **F12-Vollverifikation (v16.4):** F12 während PTT + durchgehendem Sprechen → jetzt KOMPLETT stumm (auch die eigene Stimme über die Walkies). Falls doch Ton bleibt: verbleibender Pfad ist OS-/Parsec-/VB-Cable-Seite (Protokoll: Debug-Historie Abschnitt 18).
- [ ] **Remote-2-Client-Distanztest (v16.4):** Distanz-Dämpfung der Funk-Stimme prüfen — nah laut, fern leise, >8 m stumm. Ist durch den v16.4-Fix erstmals tatsächlich wirksam (vorher volumen-immun).
- [ ] **Editor.log-Beobachtung:** „Invalid parameter"-Treffer sollten bei 37 bleiben (GetData-OOB-Fix wirkt). Bei Anstieg erneut graben.
- [ ] **txChannels/tapInTx-Anomalie:** Funkkanal-Pins lieferten in allen Logs nie native Vivox-Daten (nur ~100-ms-Restpuffer). Weiter beobachten, wenn wieder Funk-Frames fehlen.

## Repo-Hygiene

- [ ] **`Testaudio.mp3`** liegt untracked im Package-Repo-Root (`MultiplayerNetworkPackage/`) — löschen oder committen (z. B. für Tests als Sample-Asset).

## Akustik-Design (aus Brainstorming 2026-09-19, Graph-Debug-HUD)

- [ ] **Hotelszene hat noch KEINE VoiceZonen/VoicePortale:** Szene-Scan 2026-09-19 — nur `Assets/EarshotHearingTest.unity` enthält `VoiceZone`/`VoicePortal`-Komponenten; `Scenes/TRIALITY_HOTEL.unity` (und `Nima.unity`) haben keine. Damit ist der Raum-Portal-Graph im Hotel leer (HUD zeigt „keine Zone", „0 Raeume"), und die Mund-Stimme läuft dort über den Occlusion-Fallback (Sichtlinie). **Autoring-Tool ist seit 2026-09-19 (Phase 2/v16.8) vorhanden** — Ausführung steht aus: pro Raum `ZoneMarker_<Name>`-Empty in die Raummitte (Fenster „Earshot Voice/Authoring" → „Marker fuer Auswahl erstellen"), dann „Zonen aus Markern generieren". Tür-Verkabelung: „Earshot Voice/Hotelszenen-Authoring" → „Tueren verkabeln" (nur `SimpleDoor`, 2 Stück in TRIALITY_HOTEL; die 34 `TeleportDoor`s sind Phase 3). Das F7-HUD zeigt den Aufbau-Erfolg direkt an (Raum-Zähler steigt).
- [x] **Autoring-Pipeline Phase 2 gebaut** (2026-09-19, v16.8): `VoiceGraphFactory` (Runtime-Factory: `CreateZone(bounds, name, parent)`, `CreatePortal`, `LinkZones`, `ProbeRoomBounds`-Raum-Sondierung per Raycast — wiederverwendbar für den Stockwerk-Generator Phase 4), komplett neu geschriebenes `VoiceAuthoringWindow` (Marker→Zonen idempotent, `_EarshotAudioGraph`-Container, Dry-Run, Undo, erweiterte Prüfung inkl. Zonen-Overlap), im HOTEL_GAME `SimpleDoorPortalLink` (netzsynchroner `door.IsOpen()`→`portal.Openness`-Adapter, geglättet, Ruhezustand ohne Graph-Aktivität dank Phase 1b) + `EarshotHotelAuthoring` (Türen verkabeln/prüfen/entfernen). 5 neue EditMode-Tests (`VoiceAuthoringTests`: Raum-Sondierung geschlossen/offen/ohne Boden, Zone-Factory, Brücken-Verkabelung).
- [ ] **Tür-187-Beobachtung (2026-09-18):** Walkie hinter Tür 187 klang nicht gedämpft — erwiesen: Tür 187 ist ein `TeleportDoor` (Phase 3, kein Standard-Portal), Szene hat 0 Zonen (siehe Punkt oben). Welt-Okklusion des Gerätetons wirkt nachweislich (Logs zeigen `worldVol=0,22`, `LP=1420Hz`), aber ohne Zonen/Portale gibt es keinen Tür-/Umweg-Effekt.
- [x] **Walkie-Geräteton läuft NICHT durch den Raum-Portal-Graphen** (behoben 2026-09-19, Phase 1c/v16.7): `WalkieDeviceOutput` läuft jetzt pro LateUpdate durch `VoicePipeline.EvaluateWorldAttenuation` — derselbe Messweg und dieselbe Modifier-Kette wie die Stimme (Zonen, Graph-Pfad, Türen, Wände), ausgenommen das Distanz-Modul: die Reichweite bleibt Geräteeigenschaft (`walkie.MaxHearingDistance`), gerechnet wird aber auf der Pfadlänge statt der Luftlinie. Zusätzlich mischt sich der Welt-Tiefpass (dumpf hinter Tür/Wand, log-geglättet) unters Geräte-EQ, und der Welt-Dämpfungsfaktor in die Lautstärke. Diagnose-Log zeigt jetzt `path=…m, graph=…, worldVol=…`.
- [x] **Debug-Key-Map dokumentiert** (2026-09-19, v16.5): `docs/debug-keys.md` — F7 = Voice Graph Debug HUD (neu, Default), Leak-Hunt-Keys F7–F12 default AUS über Inspector-Flag `Diagnostic Hotkeys Enabled` wieder aktivierbar.
