# Changelog

Alle nennenswerten Aenderungen an Earshot Voice werden hier festgehalten.
Das Format orientiert sich an [Keep a Changelog](https://keepachangelog.com/de/1.1.0/),
die Versionierung an [Semantic Versioning](https://semver.org/lang/de/).

## [Unreleased]

### Hinzugefuegt

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
