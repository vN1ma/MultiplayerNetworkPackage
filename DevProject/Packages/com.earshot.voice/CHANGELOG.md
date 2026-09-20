# Changelog

Alle nennenswerten Aenderungen an Earshot Voice werden hier festgehalten.
Das Format orientiert sich an [Keep a Changelog](https://keepachangelog.com/de/1.1.0/),
die Versionierung an [Semantic Versioning](https://semver.org/lang/de/).

## [Unreleased]

### Hinzugefuegt (radio-tx-probe v17, 2026-09-20)

- **Diskriminator `funkSprecher=[…]`:** Die `WALKIE VIVOX RX`-Beweiszeile zeigt jetzt Vivox'
  EIGENE Sicht der Funkkanal-Fernseher (`playerId:E=…/S=…` aus `VivoxParticipant.AudioEnergy/
  SpeechDetected`, via `VivoxVoiceBackend.DescribeRadioChannelSpeech`). Beweislage des ersten
  2-Client-Lokaltests (20260920-071757/071918): Vivox quittiert den TX-Wechsel auf den Funkkanal,
  Mikro und Empfaenger-Taps stehen — aber kein Audio erreicht je den Kanal; die Arbitration
  (`NO_REMOTE_WINNER`) basiert nur auf unserem Tap und kann das nicht unterscheiden.
- **F6-Experiment `VivoxVoiceBackend.RadioTransmissionModeAll`** (leak-hunt-Checkbox noetig,
  Auto-Restore beim Deaktivieren): PTT sendet dann per `TransmissionMode.All` in Proximity UND
  Funkkanal statt Single→Funkkanal — Gegenprobe gegen den Verdacht, dass Vivox die Mikro-Speisung
  unter `TransmissionMode.Single` nicht in den ZWEITEN Audiokanal leitet, und zugleich Live-Test
  der offenen Design-Frage „Proximity parallel" (OFFENE-PUNKTE). Default weiterhin Single
  („Funk ersetzt Mund") — null Verhaltens-Change ohne F6.

### Hinzugefuegt (remote-hunt v16.9, 2026-09-20)

- **Sender-Seiten-Diagnose** (Beweis-Kette: Freund-Session 20260920-005940 — die Sendung des
  zweiten Clients kam nie im Funkkanal an, aber kein Log zeigte die Bruchstelle): `WALKIE PTT
  BLOCKIERT` (flanken-getriggert) statt stillem Guard-Return in `EarshotWalkieTalkie.SetTransmitting`;
  `FUNK sendet BLOCKIERT` bei fehlender Vivox-Verbindung, `FUNK sendet FEHLGESCHLAGEN` mit Exception
  im Session-Log (vorher nur Unity-Konsole), `FUNK sendet aus` beim Rueckwechsel und TX-Bestaetigung
  mit autoritativer Vivox-Sicht `vivoxTx=[…]` in `VivoxVoiceBackend.SetRadioTransmittingAsync`;
  `WALKIE SYNC FEHLGESCHLAGEN` zusaetzlich im Session-Log (`WalkieRadioSync`).
- **`docs/walkie-2client-testplan.md`:** Anleitung + 6-Szenarien-Checkliste fuer den lokalen
  2-Client-Test (Editor-Host + Windows-Build-Client), inkl. erwarteter Log-Zeilen pro Szenario.

### Geaendert (remote-hunt v16.9, 2026-09-20)

- **Log-Diaet** (null Verhaltens-Change): `WALKIE VIVOX RX` schreibt die Beweis-Zeile nur noch bei
  Zustandswechsel (PTT, funkRx>0, Sonden-Play-Status, Teilnehmerlisten) plus 30-s-Herzschlag statt
  alle 2 s; `WALKIE AUDIO-INVENTAR` listet nur noch voice-relevante Quellen (Earshot/Walkie/
  StreamClip/Tap — bisher auch 16 OceanSound-Ambience-Quellen pro Snapshot), LEAK-VERDACHT-Alert
  bleibt fuer alle Quellen; `WALKIE RADIO KANAL` nur noch bei Roster-Aenderung statt pro Sync-
  Durchlauf. Diagnostic-Revision: `remote-hunt-v16.9`.

### Behoben

- Sidetone-Leak (Selbsthoerung bei PTT ueberall gleich laut, distanz- und volumen-
  unabhaengig): `WalkieDeviceOutput.OnAudioFilterRead` ueberschrieb `data` mit den
  Delay-Ring-Samples in vollem Pegel. Unity wendet `AudioSource.volume`/`.mute` und
  `AudioListener.volume` VOR `OnAudioFilterRead` auf den Datenstrom an - das
  Ueberschreiben umging damit saemtliche Lautstaerkeregeln (Distanz-Falloff,
  OWN_DEVICE_TX, OUT_OF_RANGE, AudioListener-Master). Die effektive Lautstaerke wird
  jetzt autoritativ im Filter multipliziert (`smoothedVolume * GlobalListenerVolume`),
  `source.volume` bleibt konstant 1, und der AudioListener-Master wird zusaetzlich
  gespiegelt, damit globale Stummschaltung auch fuer Walkie-Lautsprecher greift.
  Behebt zugleich die bisher volumen-immune Distanzdaempfung von Remote-Funk-Stimmen
  an Geraeten (Beweis: Log 20260919-090425, Abschnitt 18 der Debug-Historie).

### Geaendert

- Leak-Hunt-Diagnose-Hotkeys F7-F12 (`WalkieSidetoneCapture`) sind per Default DEAKTIVIERT
  (v16.5): Root-Cause ist gefixt, F7 gehoert jetzt dem `VoiceGraphDebugHUD`. Zum Nachtesten
  am 'Earshot Voice Runtime'-Objekt die Checkbox `Diagnostic Hotkeys Enabled` setzen; beim
  Deaktivieren werden aktive Diagnose-Zustaende (F8-Geraet, F9-Mute, F10-Volume, F11-Feed,
  F12-Master) automatisch zurueckgesetzt. Belegungstabelle: `docs/debug-keys.md`

### Hinzugefuegt

- `VoiceGraphDebugHUD` (v16.5): Runtime-Debug-HUD fuer den Raum-Portal-Graphen, unten rechts,
  Toggle mit F7 (Inspector-konfigurierbar). Zeigt eigene Zone/Position, pro Remote-Spieler und
  Walkie die Ziel-Zone, Luftlinie vs. Graph-Laufweg (Rechenweise identisch zu
  `VoicePipeline.TryApplyGraph` inkl. Tuer-/Ecken-Aufschlag), die Tuer-Kette mit
  Offenheitsgrad, Portal-/Raum-Zaehler und Geschlossenheit; gesperrte Wege als
  Occlusion-Hinweis. Walkies erhalten eine Warnung, dass ihr Geraeteton nach Luftlinie
  (nicht nach Graph) daempft. Optionale Welt-Linien des Schallwegs (`Debug.DrawLine`,
  gruen = offen, rot = zu). Wird automatisch am VoiceRuntime-Objekt erzeugt, rein
  diagnostisch, kein Audio-Einfluss. Key-Map: `docs/debug-keys.md`
- `EarshotWalkieTalkie.SetLocalOwnership(bool)` + `IsLocallyOwned`: schuetzt `SetTransmitting`
  davor, auf fremden (Remote-)Client-Instanzen faelschlich Mikro/Sidetone auszuloesen —
  Standard bleibt `true` (unveraendertes Verhalten ohne Netzwerk)
- `Radio Crunch`-Regler an `EarshotWalkieTalkie`: bewusster Alter-Funk-Charakter
  (Sample-and-Hold + Bit-Reduktion + leichte Verzerrung), damit Walkie-Stimme sich
  hoerbar von Mund-Stimme unterscheidet
- `VivoxCaptureSourceTap` als lokale Sidetone-Quelle: verwendet Vivox' bereits geoeffnetes
  Eingabegeraet, ohne einen zweiten `Microphone.Start`-Pfad
- Nahfeld-Lautstaerke-Deckel (`MinPerceivedDistance`) in `WalkieDeviceOutput` gegen
  akustische Rueckkopplung, wenn ein Geraet sehr nah am Ohr sitzt
- Sidetone-Selbstschutz (`MinSidetoneSelfDistance`): ein Geraet direkt an der eigenen
  Hoerposition (z.B. unsynchronisiertes Sicht-/Handmodell-Duplikat) spielt nie die eigene
  Stimme ab, unabhaengig von seinem eigenen `IsTransmitting`-Flag
- Debug-Log beim Sendestart: listet alle anderen eingeschalteten Geraete auf demselben
  Kanal samt Entfernung, warnt bei < 0,5 m (Duplikat-/Sichtmodell-Verdacht)
- Persistente Walkie-Diagnose im `EarshotLogs`-Sitzungslog: Ausgabegeraet, Modus/Stream,
  Listener- und Lautsprecherposition, Entfernung/Falloff, Ziel-/Ist-Lautstaerke,
  Filterwerte, aktive Listener, PTT-Sync-Revision/-Dauer, Tap-Recovery-Dauer und
  Vivox-Capture-Tap-Status. Falls der Projektordner nicht beschreibbar ist, wird
  `Application.persistentDataPath/EarshotLogs` verwendet.

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

- Walkie-Sidetone war nur als ~100-ms-Blitz hörbar und verstummte danach: Auf dem
  Funkkanal (`earshot-radio-*`) gepinnte Capture-Taps liefern nie native Daten
  (Quelle pausiert nach `NoMoreData`); die einzigen Signal-Blöcke stammten aus dem
  Restpuffer der vorherigen Proximity-Registrierung. Der Tap wird jetzt permanent
  auf dem Proximity-Kanal gepinnt (Revision `proximity-pin-v9`) — der einzige
  belegte Datenpfad. Nebenbei entfällt die Neu-Registrierung bei jedem PTT, der
  Latenzpuffer bleibt erhalten. `inputPeak` ist jetzt ein Fenster-Maximum statt
  des Momentanwerts des letzten Buffers (der zeigte trotz `signalBlocks=5` immer
  0.0000), das FLOW-Log erscheint sekündlich statt alle 2 s.
- `SetTransmitting(false)` konnte den globalen PTT-Zustand nicht loeschen, weil das
  Geraete-Flag bereits vorher auf `false` stand. Die Registry merkt sich jetzt die
  exakte Senderinstanz; Loslassen beendet Half-Duplex und Sidetone sofort und dauerhaft.
- Der neue `VivoxCaptureSourceTap` konnte als ungefilterte globale 2D-Quelle in den
  finalen Mix gelangen. Ein einzelner Tap bleibt mit normalem Gain DSP-aktiv und
  ungemutet; der Feed nullt seinen Filterpuffer — das ist die einzige und
  ausreichende Sicherung gegen direkte Tap-Ausgabe. (`AudioSource.mute` als zweite
  Sicherung wurde in v7 entfernt: Es nullt die Samples in `OnAudioFilterRead`
  und blockiert damit den Sidetone-Datenpfad komplett, siehe naechster Punkt.)
  Capture-Logs enthalten jetzt Eingangs- und Ausgangspegel,
  Callback-Zahl, Source-Zustand, Diagnose-Revision sowie aktive/verfuegbare Vivox-Geraete.
  Bereits kanonische Kanal-IDs erzeugen keine String-Allokationen im Audiopfad.
- Walkie-Sidetone blieb stumm, obwohl der native Vivox-Capture-Tap Daten lieferte
  (Log `20260918-0735`: `callbacks>0`, `sourcePlaying=True`, aber `inputPeak=0`).
  Ursache war das seit `capture-hardmute-v2` gesetzte `AudioSource.mute` auf der
  Tap-Source: Unity feuert `OnAudioFilterRead` fuer gemutete Quellen weiter,
  uebergibt dem Filter aber nur Nullen. Der Mute ist entfernt; eine neue
  `EnforceDirectOutputUnmuted`-Sicherung entsperrt die Source, falls sie doch
  wieder gemutet wird (Revision `capture-unmute-v7`).
- Proximity- und Funk-Tap desselben Spielers wurden bei Single-Channel-Sendung gegenseitig
  als haengend fehlinterpretiert und alle sechs Sekunden synchron neu aufgebaut. Recovery
  prueft und erneuert jetzt nur noch den exakten `VoiceSpeakerKey`-Pfad.
- `WalkieRadioSync` verwirft keine PTT-Aenderungen mehr, die waehrend eines laufenden
  Vivox-Syncs eintreffen; ein Dirty-Durchlauf uebernimmt immer den neuesten Zustand.
- Sidetone-Distanz nutzt vorrangig den tatsaechlich aktiven AudioListener und hat an
  `MaxHearingDistance` einen expliziten harten Cutoff.
- Sidetone klang roboterhaft/zu schnell und konnte mit virtuellen Geraeten lokales
  Monitoring ausloesen. Der lokale Funkton kommt jetzt direkt aus Vivox'
  `VivoxCaptureSourceTap`; kein zweites Unity-Mikrofon und keine abweichende Capture-Rate.
- Periodisches Klacken am Empfangs-Walkie: `WalkieDeviceOutput` brach die Wiedergabe fruerher
  ab, wenn der Puffer kurz leer war (Gate zu) — Delay-Ring laeuft jetzt immer durch
  (Stille wird eingemischt statt die Wiedergabe abzubrechen)
- Lautstaerke stieg beim Weggehen vom Walkie faelschlich an (Anti-Feedback-Nahdaempfung
  wirkte gegenteilig) — jetzt einfache monotone Distanzdaempfung: naeher = lauter, weiter = leiser
- Bus/Ring vereinheitlicht auf Mono-Frames (`WalkieRadioTapFeed` und
  `WalkieVivoxCaptureFeed` mischen ihre Vivox-Taps runter) statt Rohdaten unabhaengig
  von Kanalzahl weiterzureichen
- Sidetone-Gate ist jetzt ein weich nachziehender Gain mit Sustain-Schwelle (~90 ms), damit
  kurze Transienten wie Schritt-Klicks seltener durchrutschen, statt hart an/aus zu schalten
- `WalkieDeviceOutput.DistanceFalloff()` gab bei unbekannter Zuhoerer-Position faelschlich
  volle Lautstaerke (`1f`) zurueck statt still zu bleiben — jetzt `0f`
- `WalkieDeviceOutput`s Listener-Suche nahm bei mehr als einem aktiven AudioListener in
  der Szene den erstbesten (nichtdeterministisch) — nutzt jetzt dieselbe robuste,
  Spieler-bevorzugende Suche wie `VoiceRuntime` (`VoiceRoster.FindPreferredAudioListener`)
