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

- [ ] **Walkie-Geräteton läuft NICHT durch den Raum-Portal-Graphen:** `WalkieDeviceOutput` dämpft nach reiner Luftlinie (MaxHearingDistance, `(1−d/max)²`). Der Leak/Sidetone am Gerät ist aber physischer Raum-Schall — durch geschlossene Türen/Wände sollte er dumpf/leiser sein, so wie die Mund-Stimme (`VoicePipeline` + `GraphModifier`). Entscheidung offen: `WalkieDeviceOutput` an `VoiceGraph.TryFindPath` anschließen (eigene Zone-Ermittlung + Pfad-Länge statt Luftlinie). Der geplante Graph-Debug-HUD macht genau das sichtbar.
- [x] **Debug-Key-Map dokumentiert** (2026-09-19, v16.5): `docs/debug-keys.md` — F7 = Voice Graph Debug HUD (neu, Default), Leak-Hunt-Keys F7–F12 default AUS über Inspector-Flag `Diagnostic Hotkeys Enabled` wieder aktivierbar.
