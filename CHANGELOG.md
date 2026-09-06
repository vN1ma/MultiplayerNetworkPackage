# Changelog

Neueste Einträge oben. Format (siehe `.clinerules/01-workflow.md`):

```
## [YYYY-MM-DD] – Kurztitel
- Was geändert wurde (1–3 Sätze)
- Warum (falls nicht offensichtlich aus dem Titel)
- Betroffene Dateien/Ordner
```


## [2026-09-06] – Snapshot vor Start von Phase 1 (Voice-Abkopplung)
- Arbeitsstand des coop-Pakets gesichert, bevor der Umbau in `com.earshot.voice` beginnt: Session-Diagnostik (`VoiceSessionLog`, `VoiceSessionRecorder`), `VoiceTestSpeaker`, `VoiceTransparent`, MPPM-Duo-Tester, Playtest-Builder- und Voice-Korrekturen aus den Test-Sessionen sowie Multiplayer-Playmode/Tools-Pakete im DevProject. Neu im Repo: `.clinerules/`, Planungs-Dokumente (`docs/`) und dieses Changelog.
- Lokale Voice-Session-Logs (`logss/`, `DevProject/EarshotLogs/`) und der frühere Export-Entwurf (`ProximityChatExport/`) über `.gitignore` ausgeschlossen.
- Betroffene Dateien: `DevProject/Packages/com.earshot.coop/`, `DevProject/Packages/manifest.json`, `docs/`, `.clinerules/`, `.gitignore`

## [2026-09-06] – Plan an Code-Realität angeglichen + Walkie-Talkie-Phase ergänzt
- Phase 1 korrigiert: `CoopVoice.cs` statt `Voice.cs`, `VoiceTestSpeaker` statt `VoiceDebugInjector`, Radio als Sample statt Runtime-Modifier gekennzeichnet (der Plan war ohne Repo-Zugriff entstanden). Neue Phase 4 „Übertragungsgeräte (Walkie-Talkie)" eingefügt mit V1-Regel „Funk ersetzt die Mund-Stimme"; bisherige Phase 4 wurde nur in Phase 5 umbenannt, Punkte um sechs Paket-Reifungs-Themen ergänzt. Vier destillierte Entscheidungen/Fakten aus externem Chat-Kontext in `docs/DECISIONS.md` festgehalten.
- Betroffene Dateien: `docs/earshot-voice-plan.md`, `docs/PROGRESS.md`, `docs/DECISIONS.md`, `CHANGELOG.md`

