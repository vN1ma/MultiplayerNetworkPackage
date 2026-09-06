# Changelog

Alle nennenswerten Aenderungen an Earshot Voice werden hier festgehalten.
Das Format orientiert sich an [Keep a Changelog](https://keepachangelog.com/de/1.1.0/),
die Versionierung an [Semantic Versioning](https://semver.org/lang/de/).

## [Unreleased]

### Hinzugefuegt

- Phase 4 Walkie-Talkie: `EarshotWalkieTalkie`, Vivox-Funkkanal (`earshot.radio.*`),
  Half-Duplex, lokales Empfangs-Delay, Leak am Geraet, Mund-Daempfung beim Funken
- Walkie Sidetone (eigene Stimme versetzt an anderen Geraeten), Fan-out an alle
  Empfangs-Walkies, First-Speaker-Lock bei gleichzeitigem Funken
- Mitnahme-Doku `docs/walkie-talkie-game-integration.md` fuer Prefab/Input im Spiel
- EditMode-Tests `WalkieRulesTests`, `WalkieTalkArbitrationTests`
- Paket-Grundgeruest, netzwerkunabhaengig: `IProximityVoicePlayer` als Bruecke zu einem
  beliebigen Multiplayer-Framework, `VoiceRoster` als generisches Register
- `EarshotProximityVoice` als einzige Pflichtkomponente auf dem Player (Zero-Config- und
  Advanced-Modus)
- Voice-Pipeline aus `ProximityChatExport/` uebernommen: Vivox als flacher 2D-Kanal, Audio
  Taps, Distanz-/Occlusion-/Portal-/Zone-Module, Session-Log, Test-Werkzeuge
  (`VoiceTestSpeaker`, `VoiceSessionRecorder`)
- Sample `CustomVoiceModifier` (`RadioModifier`) als Vorlage fuer eigene Module
  und die Walkie-Talkie-Phase
- Optionale Komponenten `EarshotVoiceMuteHotkey` und `EarshotVoiceConnectInScene`
- EditMode-Tests fuer `VoiceRoster` und `VoiceSample`
- Raum-Portal-Graph: Dijkstra durch `VoiceZone`/`VoicePortal`, `GraphModifier`
- Treppen-Portale, `ApparentDirection`, Offset-Faecher, Authoring-Fenster, Graph-EditMode-Tests
- Hoertest-Szene: geschlossener Flur, Tuer, Treppe, Ton mit E, Tuer mit F
- Hoerregler direkt am Player (`EarshotProximityVoice`): Distanz, Luft, Waende, Tueren, Graph, Hall, getrennte Glaettung
- `Voice Source Color` pro Quelle: Dumpf, Hall, blechern, Presets (Testlautsprecher, Player, AudioSource)
- Hoertest: `Testaudio` statt synthetischem Loop; E pausiert/setzt fort
- Schallweg folgt Tueren/Treppen; kein Cutoff mehr, wenn man knapp aus der Zone tritt

### Geaendert

- `IVoiceBackend`: `VoicePathKind` / `VoiceSpeakerKey`; optionales `IVoiceRadioBackend`
- Beim Walkie-Senden nur Funkkanal (`TransmissionMode.Single`), nicht parallel zum Proximity-Mund
