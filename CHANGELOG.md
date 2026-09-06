# Changelog

Neueste Einträge oben. Format (siehe `.clinerules/01-workflow.md`):

```
## [YYYY-MM-DD] – Kurztitel
- Was geändert wurde (1–3 Sätze)
- Warum (falls nicht offensichtlich aus dem Titel)
- Betroffene Dateien/Ordner
```


## [2026-09-06] – Eine Komponente verbindet und ordnet Stimmen selbst
- `EarshotProximityVoice` tritt dem Sprachkanal automatisch bei, erkennt lokal/remote an Netcode/Mirror/Photon/FishNet (ohne diese Assemblies zu referenzieren) und haengt Vivox-Stimmen an die Avatare. `ConnectAsync`/`Bind` sind kein Pflichtschritt mehr.
- Kanal: Inspector-Feld, sonst Unity-Lobby-ID, sonst Settings, sonst `earshot`.
- Betroffene Dateien: `EarshotProximityVoice.cs`, `NetworkOwnershipProbe.cs`, `VoiceChannelResolver.cs`, `EarshotVoice.cs`, `EarshotVoiceSettings.cs`

## [2026-09-06] – Raum-Portal-Graph (Phase 3, Kern)
- Zonen und Portale bilden zur Laufzeit einen Graphen. Freie Sichtlinie bleibt der direkte Weg; sonst Dijkstra durch Tueren. Geschlossene Tueren machen den Weg teurer und dumpfer, mehrere Tueren addieren sich. Ohne Zonen in der Szene aendert sich nichts.
- Entfernungsdaempfung nutzt `HearingDistance` (Luftlinie oder Graph-Weg). Stimme kommt bei Graph-Weg aus der ersten Tuer.
- Betroffene Dateien: `Runtime/Voice/Graph/`, `GraphModifier.cs`, `VoicePipeline.cs`, `VoiceContext.cs`, `VoiceEmitter.cs`

## [2026-09-06] – Unity-.meta-Dateien fuer com.earshot.voice
- Git-Pakete sind fuer Unity unveraenderlich; ohne mitgelieferte `.meta` ignoriert der Editor alle Skripte. Metas fuer Dateien und Ordner nachgetragen.
- Betroffene Dateien: `DevProject/Packages/com.earshot.voice/**/*.meta`

## [2026-09-06] – Team-Install per Git-URL (Weg A)
- Spiel-Teams binden `com.earshot.voice` per Git-URL ein (`?path=/DevProject/Packages/com.earshot.voice`). Mitspieler brauchen dieses Repo nicht lokal. Nach Paket-Änderungen: hier pushen, im Spiel updaten, `packages-lock.json` mitcommitten.
- Betroffene Dateien: `docs/altes-earshot-entfernen.md`, `README.md`, `docs/DECISIONS.md`

## [2026-09-06] – Optionale Beispiel-Komponenten und EditMode-Tests
- `ProxVoiceMuteHotkey` und `ProxVoiceConnectInScene` aus dem Export gesichtet und als optionale Komponenten ins Voice-Paket übernommen (`EarshotVoiceMuteHotkey`, `EarshotVoiceConnectInScene`). Beide liegen nicht auf dem Player und sind kein Pflichtschritt.
- EditMode-Tests ohne Netzwerk: `VoiceRoster` (Register, Identity, Lookup, Unregister, Clear) und `VoiceSample` (Clamp, MoveTowards, logarithmische Frequenz). Test-Assembly unter `com.earshot.voice/Tests/EditMode/`, `com.unity.test-framework` im DevProject-Manifest.
- Betroffene Dateien: `DevProject/Packages/com.earshot.voice/Runtime/Player/`, `DevProject/Packages/com.earshot.voice/Tests/`, `DevProject/Packages/manifest.json`, `docs/PROGRESS.md`

## [2026-09-06] – Multiplayer aus dem Repo entfernt, DevProject ist Voice-only
- `com.earshot.coop` vollständig gelöscht. Netcode, Multiplayer Play Mode, Multiplayer Tools und Multiplayer Center aus `DevProject/Packages/manifest.json` entfernt. `DevProject` bleibt das Unity-Testprojekt, jetzt ohne Multiplayer-Paket.
- NGO-Adapter entfällt: das eigene Spiel räumt der Nutzer selbst auf. Anleitung: `docs/altes-earshot-entfernen.md` (Paket, Code, Komponenten, Assets, optionale Netcode-Reste, Abschluss-Check, danach erst `com.earshot.voice`).
- `RadioModifier`-Sample nach `com.earshot.voice/Samples~/CustomVoiceModifier/` übernommen, damit die Phase-4-Vorlage nicht mit dem alten Paket verschwindet.
- Betroffene Dateien: `DevProject/Packages/com.earshot.coop/` (gelöscht), `DevProject/Packages/manifest.json`, `DevProject/Packages/com.earshot.voice/Samples~/`, `docs/altes-earshot-entfernen.md`, `docs/PROGRESS.md`, `docs/earshot-voice-plan.md`, `docs/DECISIONS.md`, `README.md`

## [2026-09-06] – com.earshot.voice als eigenständiges Paket angelegt (Phase 1, Kernumbau)
- Neues Unity-Paket `DevProject/Packages/com.earshot.voice/` angelegt: eigene `package.json`/`.asmdef` ohne Abhängigkeit zu Netcode/Multiplayer, nur Vivox + Unity Authentication. Ablageort war eine offene Frage des Nutzers — Option A (Sibling-Paket neben `com.earshot.coop` unter `DevProject/Packages/`) gewählt, damit `com.earshot.voice` ein klar eigenständiges, direkt kompilier- und playtestbares Paket bleibt statt mit dem Multiplayer-Code vermischt zu sein.
- Code aus `ProximityChatExport/` übernommen und auf den Namespace `Earshot.Voice` umgestellt: `IVoiceBackend`/`VivoxVoiceBackend`, `VoiceRuntime`/`VoiceEmitter`/`VoicePipeline`, `VoiceContext`/`VoiceProfile`/`VoiceZone`/`VoicePortal`/`VoiceTransparent`, `VoiceSessionLog`, alle vier `Modifiers/`. `ProxVoice` → `EarshotVoice`-Fassade, `ProxVoiceSettings` → `EarshotVoiceSettings`, `ProxLog` → `EarshotVoiceLog`.
- Neues `IProximityVoicePlayer`-Interface (`PlayerId`, `HasIdentity`, `IsLocalPlayer`, `VoiceAnchor`, `Position`, `DisplayName`) plus generisches `VoiceRoster`-Register ersetzen `ProxVoiceRoster` und das coop-eigene `PlayerRegistry`.
- Neue Hauptkomponente `EarshotProximityVoice`: Zero-Config (Standard `isLocalPlayer = true`, PlayerId füllt sich selbst über Unity Authentication) plus Advanced-Modus (`Bind()` für Netzwerk-Adapter).
- Test-Werkzeuge `VoiceTestSpeaker` und `VoiceSessionRecorder` aus dem alten `Voice/`-Ordner übernommen und von `Coop`-Zuständen auf `EarshotVoice.IsConnected`/`VoiceRoster` umgestellt.
- `com.earshot.voice` in `DevProject/Packages/manifest.json` als Dependency registriert.
- `VoicePipeline.MeasureLineOfSight` entkoppelt: prüft jetzt `IProximityVoicePlayer` statt des alten `ProxVoicePlayer`, um Spieler-Collider von der Verdeckungsberechnung auszuschließen.
- Beide bekannten Bugs (2-Sekunden-Delay, fehlende Muffle/Reverb/Distanz-Kurve) waren im Export-Stand bereits behoben und sind mit übernommen worden.
- Offen: NGO-Adapter (`NetcodeVoicePlayer`), EditMode-Tests, Hörtest, danach Löschen von `ProximityChatExport/`.
- Betroffene Dateien: `DevProject/Packages/com.earshot.voice/**` (neu), `DevProject/Packages/manifest.json`, `docs/PROGRESS.md`

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

