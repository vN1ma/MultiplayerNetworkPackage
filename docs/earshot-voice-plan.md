# Earshot — Proximity Voice Abkopplung & Realistic Audio Graph

Gesamtplan: Multiplayer abspalten → Altlasten sauber entfernen → Bugs fixen → Raum-Portal-Graph bauen.
Reihenfolge ist bewusst so gewählt, dass am Ende kein doppelter Multiplayer und keine doppelte Voice-Pipeline parallel laufen.

## Leitprinzip (Definition of Done für JEDE Phase)

> Wer Earshot nutzen will, zieht **eine Komponente** (`EarshotProximityVoice`) auf seinen Player. Nicht mehr, nicht weniger.

Das ist das Hauptziel des gesamten Umbaus — nicht nur ein Feature von Phase 1. Jede Phase wird daran gemessen:

- [x] Phase 1: `EarshotProximityVoice` ist die einzige Pflichtkomponente, alles andere (Backend, Registrierung) läuft automatisch dahinter
- [x] Phase 2: Im eigenen Spiel ist `com.earshot.coop` restlos weg; auf dem Player steht `EarshotProximityVoice` — keine `CoopPlayer`
- [x] Phase 3: Zonen (`VoiceZone`) und Portale (`VoicePortal`) bleiben **optionale** Komponenten auf Wänden/Türen — der Graph funktioniert mit Fallback (`OcclusionModifier`) auch ganz ohne sie, ein Nutzer *kann* sie für bessere Akustik hinzufügen, muss aber nicht
- [x] Kein Schritt in irgendeiner Phase darf verlangen, dass der Nutzer Netzwerk-Code, Player-IDs oder Audio-Filter manuell verdrahtet

Wenn ein Arbeitspaket dazu führt, dass der Nutzer mehr als eine Komponente anfassen oder ein Feld manuell setzen muss, gehört das in den Advanced-Modus (optional, versteckt) — nicht in den Standardweg.

---

## Phase 1 — Multiplayer abkoppeln (Kernumbau)

Ziel: `com.earshot.voice` entsteht als eigenständiges Paket, das nichts von Netcode/NGO/UGS-Multiplayer weiß. Basis ist die neueste Voice-Iteration im Ordner `ProximityChatExport/` (netzwerk-unabhängig, mit `ProxVoice`-Fassade, `ProxVoiceRoster`/`ProxVoicePlayer`; siehe `docs/DECISIONS.md`, Eintrag „ProximityChatExport ist die neueste Voice-Iteration"). Der `Voice/`-Ordner in `com.earshot.coop` ist der ältere Stand und liefert nur noch die Test-Werkzeuge. Die zwei bekannten Bugs werden hier gleich mitgefixt, weil genau die betroffenen Klassen ohnehin umgebaut werden.

Ordner-Rollen ab jetzt: Dieses Repo **ist** das Voice-DevProject. Neuer Voice-Code entsteht **ausschließlich** unter `DevProject/Packages/com.earshot.voice/`. `DevProject` ist das Unity-Testprojekt ohne Multiplayer. `com.earshot.coop` ist aus diesem Repo entfernt (Entscheidung 2026-09-06: Nutzer räumt das eigene Spiel selbst auf). Anleitung: `docs/altes-earshot-entfernen.md`. `ProximityChatExport/` ist nur Quelle der Übernahme und wird nach Hörtest gelöscht.

- [x] `IProximityVoicePlayer`-Interface definieren
- [x] Neues Package-Grundgerüst `com.earshot.voice` anlegen
- [x] Code aus `ProximityChatExport/` übernehmen (Namespace `Earshot.Proximity` → `Earshot.Voice`)
  - [x] `IVoiceBackend`, `VivoxVoiceBackend`
  - [x] `VoiceRuntime`, `VoiceEmitter`, `VoicePipeline`
  - [x] `VoiceContext`, `VoiceProfile`, `VoiceZone`, `VoicePortal`, `VoiceTransparent`
  - [x] `VoiceSessionLog`
  - [x] `Modifiers/` (Distance, Occlusion, Portal, Zone)
  - [x] `ProxVoice` → `EarshotVoice`
  - [x] `ProxVoiceSettings` → `EarshotVoiceSettings`, `ProxLog` → `EarshotVoiceLog`
  - [x] `ProxVoiceMuteHotkey` / `ProxVoiceConnectInScene` als optionale Komponenten übernommen
- [x] Test-Werkzeuge übernommen: `VoiceTestSpeaker`, `VoiceSessionRecorder`
- [x] Generisches Register: `VoiceRoster`
- [x] `EarshotProximityVoice` (Zero-Config + Advanced/`Bind`)
- [x] Hoerregler (Distanz, Dumpf, Hall, Glaettung) direkt am Player, kein Pflicht-Asset
- [x] Bug 1 (2-Sekunden-Delay) und Bug 2 (Muffle/Reverb/Distanz) aus dem Export-Stand übernommen
- [x] ~~NGO-Adapter `NetcodeVoicePlayer` im alten Paket~~ — **entfallen** (Nutzer entfernt `com.earshot.coop` selbst, siehe `docs/altes-earshot-entfernen.md`)
- [x] Tests: EditMode-Tests für das generische Register ohne Netzwerk
- [ ] Hörtest mit `VoiceTestSpeaker` + itsjessamess-Clip: Delay weg, alle drei Effekte (Muffle, Reverb, Distanz-Falloff) einzeln hörbar korrekt
- [ ] Nach Übernahme und bestandenem Hörtest: `ProximityChatExport/` aus dem Repo löschen — der Stand bleibt über die Git-Historie gesichert (Commit `1e78b0f`); `ANLEITUNG.md` vorher auf Phase-5-Relevanz gesichtet

---

## Phase 2 — Altes Earshot im eigenen Spiel entfernen

Ziel: im Spielprojekt kein `com.earshot.coop` mehr, keine doppelte Voice-Pipeline. Dieses Repo enthält das Multiplayer-Paket nicht mehr. Der Nutzer arbeitet die Checkliste in `docs/altes-earshot-entfernen.md` selbst ab und bindet danach nur `com.earshot.voice` ein.

- [x] Inventur / altes Paket im Spiel entfernt
- [x] `com.earshot.voice` per Git-URL eingebunden, `EarshotProximityVoice` auf dem Player
- [x] Verbindung und lokal/remote laufen über `EarshotProximityVoice`

---

## Phase 3 — Raum-Portal-Graph (realistischer Proximity Chat)

Läuft jetzt vollständig innerhalb von `com.earshot.voice`, komplett multiplayer-unabhängig. Entspricht v1.1 aus der bisherigen `ROADMAP.md`.

- [x] Datenmodell: Zonen (`VoiceZone`) als Knoten, Portale (`VoicePortal`) als Kanten (`VoiceGraph`)
- [x] Kantengewicht = akustische Länge + Dämpfung aus `Openness` (geschlossene Tür +12 m Strafe)
- [x] Pfadsuche: Dijkstra (`VoiceGraphSearch`)
- [x] Direkte Sichtlinie bleibt Schnellpfad, wenn frei; sonst gewinnt der Graph
- [x] Neuer `GraphModifier` in die bestehende Modifier-Kette einhängen
  - [x] `OcclusionModifier` bleibt als Fallback, wenn kein Graph-Weg existiert
- [x] Tür-Logik (Kern):
  - [x] Offen (`Openness >= 0.5`): Kante fast kostenlos
  - [x] Geschlossen: Kante teuer plus Dumpf, nicht automatisch stumm
  - [x] Mehrere Türen hintereinander: Dämpfung addiert sich
  - [x] Tür öffnet sich während des Sprechens: weicher Übergang über die Profil-Glaettung
- [x] Treppenhaus/Etagen:
  - [x] Treppenlauf als Portal zwischen zwei Zonen (`VoicePortalKind.Stair`)
  - [x] Vertikale Luftlinie durch Decke zählt nur als Occlusion, wenn kein Treppen-Portal verbindet
  - [x] Länge der Treppe geht in `HearingDistance` ein (`TravelLength`)
- [x] Richtung/Beugung:
  - [x] `ApparentDirection`: Stimme kommt hörbar aus der offenen Tür/Flurecke, nicht durch die Wand
  - [x] Mehrere kurze Rays / Offset-Fächer für "halb hinter der Kante"
- [x] Autorentools:
  - [x] Editor-Tool: Zonen aus Collidern erzeugen (`Earshot Voice/Authoring`)
  - [x] Portale an Tür-Prefabs automatisch erkennen
  - [x] Bake-Button: Graph im Editor vorberechnen
  - [x] Gizmos: Räume, Kanten, gewählter Schallweg
  - [x] Preflight-Warnungen: "Zone ohne Portal", "Portal ohne zwei Zonen", "Spieler außerhalb jeder Zone"
- [x] Tests:
  - [x] L-Flur, Tür offen: Graph-Weg leiser als freie Sicht, aber viel lauter als durch die Wand
  - [x] Dieselbe Tür zu: dumpf, Volumen im ClosedVolume-Bereich
  - [x] Zwei Etagen, nur Decke dazwischen: stark gedämpft
  - [x] Zwei Etagen plus Treppen-Portal: Distanz ≈ Treppenlänge, kein Decken-Cut
  - [x] Rahmenstreifschuss bei offener Tür: nicht voll occluded

---

## Phase 4 — Übertragungsgeräte (Walkie-Talkie)

Ziel: Funkgeräte als **optionale** Welt-Objekte — die Stimme läuft über einen separaten Funkkanal und wird am Gerät des Empfängers abgespielt. V1-Regel: Funk ersetzt die Mund-Stimme, solange gesendet wird (Begründung: `docs/DECISIONS.md`, „Walkie-Talkie V1"). Half-Duplex und lokales Walkie-Delay sind Teil von V1. Mitnahme-Doku: `docs/walkie-talkie-game-integration.md`.

- [x] Vivox-Multi-Kanal-Fundament: separater Funkkanal pro Kanal-ID; beim Senden nur Funk, sonst Proximity — `IVoiceRadioBackend` / `VivoxVoiceBackend`
- [x] `EarshotWalkieTalkie`-Komponente (optional, Welt-Objekt): Funkkanal-ID, Audio-Anchor, Power / CanTransmit / PTT — kein Spiel-Input im Package
- [x] Empfang am Gerät — blechernes EQ-Band, Half-Duplex, Walkie-Delay
- [x] Umgebungs-Leak über Distanz-Falloff am Geräte-Transform
- [x] Sender-Dämpfung der Mund-Stimme während Funk-Audio
- [x] EditMode-Tests (`WalkieRulesTests`)
- [ ] Hörtest: zwei Walkies, Verhaltensregeln verifiziert
- [ ] Bewusst nicht in V1: Squelch-Knacksen, Reichweitenlimit/Batterie, dominanter Pfad mit weicher Überblendung

---

## Phase 5 — Später (nicht jetzt anfassen)

- [ ] Hall-Presets pro Raumtyp (Flur, Treppenhaus, Bad, Halle, Außen)
- [ ] Luftabsorption entlang des Graph-Wegs (hohe Frequenzen sterben in langen Fluren)
- [ ] Material-Tags: leise Transmission durch dünne Wände vs. harte Occlusion durch Beton
- [ ] Mehrere gleich gute Wege: offenster gewinnt
- [ ] Occlusion-Fächer für halboffene Türen (Spalt)
- [ ] Adapter-Ökosystem: Mirror-/Photon-Adapter als optionale Zusatz-Packages — `IProximityVoicePlayer`-API von Anfang an darauf auslegen
- [ ] Vivox-Credentials-Setup erleichtern (Settings-Asset + minimaler Setup-Dialog) — letzter verbleibender Pflichtschritt nach dem Leitprinzip
- [ ] Sprech-Modi: Open Mic / Push-to-Talk / Sprachaktivierung als Inspector-Option
- [ ] Optionale UI-Bausteine: Teilnehmerliste, Mute-Buttons, Sprech-Indikator
- [ ] Mikrofon-Berechtigungen auf Mobilgeräten (Android/iOS) sauber abfragen
- [ ] Late-Join-/Reconnect-Robustheit der Voice-Schicht

---

## Randbedingungen

- Große/komplexe Änderungen sind nur etwa im Wochentakt möglich (Zeit-/Ressourcenlimit) — Phase 1 realistisch nicht an einem Tag, eher über mehrere Sessions verteilt.
- Reihenfolge ist absichtlich: **erst** abkoppeln, **dann** aufräumen, **dann** Graph bauen — damit nicht zwei Multiplayer-Systeme parallel laufen und die Graph-Arbeit auf einer sauberen Basis passiert statt zweimal gemacht werden zu müssen.
