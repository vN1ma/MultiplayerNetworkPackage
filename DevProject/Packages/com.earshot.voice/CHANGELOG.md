# Changelog

Alle nennenswerten Aenderungen an Earshot Voice werden hier festgehalten.
Das Format orientiert sich an [Keep a Changelog](https://keepachangelog.com/de/1.1.0/),
die Versionierung an [Semantic Versioning](https://semver.org/lang/de/).

## [Unreleased]

### Hinzugefuegt

- `EarshotWalkieTalkie.SetLocalOwnership(bool)` + `IsLocallyOwned`: schuetzt `SetTransmitting`
  davor, auf fremden (Remote-)Client-Instanzen faelschlich Mikro/Sidetone auszuloesen —
  Standard bleibt `true` (unveraendertes Verhalten ohne Netzwerk)
- `Radio Crunch`-Regler an `EarshotWalkieTalkie`: bewusster Alter-Funk-Charakter
  (Sample-and-Hold + Bit-Reduktion + leichte Verzerrung), damit Walkie-Stimme sich
  hoerbar von Mund-Stimme unterscheidet
- Mikrofon-Vorwaermen (`WalkieSidetoneCapture.Prewarm`) beim Verbindungsaufbau, um den
  bekannten `Microphone.Start`-Ruckler nicht erst beim ersten echten PTT-Druck zu zahlen
- Nahfeld-Lautstaerke-Deckel (`MinPerceivedDistance`) in `WalkieDeviceOutput` gegen
  akustische Rueckkopplung, wenn ein Geraet sehr nah am Ohr sitzt
- Sidetone-Selbstschutz (`MinSidetoneSelfDistance`): ein Geraet direkt an der eigenen
  Hoerposition (z.B. unsynchronisiertes Sicht-/Handmodell-Duplikat) spielt nie die eigene
  Stimme ab, unabhaengig von seinem eigenen `IsTransmitting`-Flag
- Debug-Log beim Sendestart: listet alle anderen eingeschalteten Geraete auf demselben
  Kanal samt Entfernung, warnt bei < 0,5 m (Duplikat-/Sichtmodell-Verdacht)

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

### Behoben

- Sidetone klang roboterhaft/zu schnell: Mikrofon wurde fest mit 16 kHz aufgenommen,
  aber mit der Ausgabe-Rate (meist 48 kHz) abgespielt — jetzt nimmt `WalkieSidetoneCapture`
  mit `AudioSettings.outputSampleRate` auf, kein Pitch-Fehler mehr
- Periodisches Klacken am Empfangs-Walkie: `WalkieDeviceOutput` brach die Wiedergabe fruerher
  ab, wenn der Puffer kurz leer war (Gate zu) — Delay-Ring laeuft jetzt immer durch
  (Stille wird eingemischt statt die Wiedergabe abzubrechen)
- Lautstaerke stieg beim Weggehen vom Walkie faelschlich an (Anti-Feedback-Nahdaempfung
  wirkte gegenteilig) — jetzt einfache monotone Distanzdaempfung: naeher = lauter, weiter = leiser
- Bus/Ring vereinheitlicht auf Mono-Frames (`WalkieRadioTapFeed` mischt Vivox-Tap runter,
  `WalkieSidetoneCapture` mischt Mehrkanal-Mikrofon runter) statt Rohdaten unabhaengig
  von Kanalzahl weiterzureichen
- Sidetone-Gate ist jetzt ein weich nachziehender Gain mit Sustain-Schwelle (~90 ms), damit
  kurze Transienten wie Schritt-Klicks seltener durchrutschen, statt hart an/aus zu schalten
- `WalkieDeviceOutput.DistanceFalloff()` gab bei unbekannter Zuhoerer-Position faelschlich
  volle Lautstaerke (`1f`) zurueck statt still zu bleiben — jetzt `0f`
- `WalkieDeviceOutput`s Listener-Suche nahm bei mehr als einem aktiven AudioListener in
  der Szene den erstbesten (nichtdeterministisch) — nutzt jetzt dieselbe robuste,
  Spieler-bevorzugende Suche wie `VoiceRuntime` (`VoiceRoster.FindPreferredAudioListener`)
