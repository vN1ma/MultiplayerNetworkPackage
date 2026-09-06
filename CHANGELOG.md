# Changelog

Neueste Einträge oben. Format (siehe `.clinerules/01-workflow.md`):

```
## [YYYY-MM-DD] – Kurztitel
- Was geändert wurde (1–3 Sätze)
- Warum (falls nicht offensichtlich aus dem Titel)
- Betroffene Dateien/Ordner
```


## [2026-09-06] – Ordner-Rollen in Phase 1 dokumentiert
- In `docs/earshot-voice-plan.md` und `docs/PROGRESS.md` festgehalten: Neuer Voice-Code nur unter `DevProject/Packages/com.earshot.voice/`; `DevProject` bleibt Unity-Testprojekt; `com.earshot.coop` bleibt Multiplayer-Heimat; `ProximityChatExport/` wird nach Übernahme und Hörtest gelöscht (Historie sichert den Stand, Commit `1e78b0f`).
- Warum: Die Ablageorte und der Verbleib der Ordner waren bislang nur teilweise dokumentiert.
- Betroffene Dateien: `docs/earshot-voice-plan.md`, `docs/PROGRESS.md`

## [2026-09-06] – Phase 1 im Plan auf Export-Stand als Basis umgestellt
- Phase 1 in `docs/earshot-voice-plan.md` und `docs/PROGRESS.md` umgeschrieben: Basis ist die neueste Voice-Iteration aus `ProximityChatExport/` (Namespace `Earshot.Proximity` → `Earshot.Voice`, `ProxVoice` → eigenständige Fassade, `ProxVoiceRoster` + `PlayerRegistry` → generisches `IProximityVoicePlayer`-Register) statt des älteren `Voice/`-Ordners in `com.earshot.coop`. WP-Reihenfolge unverändert, nur Basis und Übernahme-Schritte angepasst.
- Warum: Nutzer hat klargestellt, dass der Export-Ordner die zuletzt bearbeitete Version enthält.
- Betroffene Dateien: `docs/earshot-voice-plan.md`, `docs/PROGRESS.md`

## [2026-09-06] – Neuesten Stand des Proximity-Chat-Exports gesichert
- `ProximityChatExport/` enthält die zuletzt bearbeitete, netzwerk-unabhängige Voice-Iteration (`ProxVoice`-Fassade mit `ConnectAsync`/Anonymous-SignIn, `ProxVoiceRoster`/`ProxVoicePlayer`, überarbeitete `VoiceRuntime`/`VoiceSessionLog`/`VivoxVoiceBackend`). Der Ordner war zuvor fälschlich als veralteter Entwurf eingestuft und gitignored — jetzt stattdessen committet. DevProject hält die ältere Voice-Version plus Test-Werkzeuge (`VoiceTestSpeaker`, `VoiceSessionRecorder`, `MppmDuoTester`).
- Absolute lokale Pfade in `ANLEITUNG.md` durch Platzhalter ersetzt (Regel 04).
- Betroffene Dateien: `ProximityChatExport/`, `.gitignore`, `docs/DECISIONS.md`

## [2026-09-06] – Snapshot vor Start von Phase 1 (Voice-Abkopplung)
- Arbeitsstand des coop-Pakets gesichert, bevor der Umbau in `com.earshot.voice` beginnt: Session-Diagnostik (`VoiceSessionLog`, `VoiceSessionRecorder`), `VoiceTestSpeaker`, `VoiceTransparent`, MPPM-Duo-Tester, Playtest-Builder- und Voice-Korrekturen aus den Test-Sessionen sowie Multiplayer-Playmode/Tools-Pakete im DevProject. Neu im Repo: `.clinerules/`, Planungs-Dokumente (`docs/`) und dieses Changelog.
- Lokale Voice-Session-Logs (`logss/`, `DevProject/EarshotLogs/`) und der frühere Export-Entwurf (`ProximityChatExport/`) über `.gitignore` ausgeschlossen.
- Betroffene Dateien: `DevProject/Packages/com.earshot.coop/`, `DevProject/Packages/manifest.json`, `docs/`, `.clinerules/`, `.gitignore`

## [2026-09-06] – Plan an Code-Realität angeglichen + Walkie-Talkie-Phase ergänzt
- Phase 1 korrigiert: `CoopVoice.cs` statt `Voice.cs`, `VoiceTestSpeaker` statt `VoiceDebugInjector`, Radio als Sample statt Runtime-Modifier gekennzeichnet (der Plan war ohne Repo-Zugriff entstanden). Neue Phase 4 „Übertragungsgeräte (Walkie-Talkie)" eingefügt mit V1-Regel „Funk ersetzt die Mund-Stimme"; bisherige Phase 4 wurde nur in Phase 5 umbenannt, Punkte um sechs Paket-Reifungs-Themen ergänzt. Vier destillierte Entscheidungen/Fakten aus externem Chat-Kontext in `docs/DECISIONS.md` festgehalten.
- Betroffene Dateien: `docs/earshot-voice-plan.md`, `docs/PROGRESS.md`, `docs/DECISIONS.md`, `CHANGELOG.md`

