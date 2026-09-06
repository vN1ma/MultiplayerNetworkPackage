# PROGRESS — Earshot Voice Umbau

## Aktuell

**Phase 1 — Multiplayer abkoppeln (Kernumbau)**

Status: Dieses Repo ist jetzt ein Voice-only-DevProject. `com.earshot.voice` liegt unter `DevProject/Packages/com.earshot.voice/`. Das alte Multiplayer-Paket `com.earshot.coop` und die Netcode-/MPPM-Abhängigkeiten sind aus dem Repo entfernt. NGO-Adapter entfällt — das eigene Spielprojekt räumt der Nutzer selbst auf (Anleitung: `docs/altes-earshot-entfernen.md`), danach kommt nur noch das neue Paket hinein.

Offen hier: Hörtest in Unity (`VoiceTestSpeaker` + Distanz/Wand/Tür), danach Löschen von `ProximityChatExport/`. Phase 2 bleibt beim Nutzer im eigenen Spiel.

Nächster Schritt: diesen Stand **pushen**, dann im Spiel per Git-URL einbinden (`docs/altes-earshot-entfernen.md` Abschnitt 8). Danach `EarshotProximityVoice` + `ConnectAsync` + `Bind`, dann Hörtest.

---

## Leitprinzip (Definition of Done für JEDE Phase)

> Wer Earshot nutzen will, zieht **eine Komponente** (`EarshotProximityVoice`) auf seinen Player. Nicht mehr, nicht weniger.

- [ ] Phase 1: `EarshotProximityVoice` ist die einzige Pflichtkomponente, alles andere (Backend, Registrierung, Adapter-Erkennung) läuft automatisch dahinter
- [ ] Phase 2: Im eigenen Spielprojekt ist `com.earshot.coop` restlos weg (Anleitung `docs/altes-earshot-entfernen.md`); auf dem Player steht danach nur `EarshotProximityVoice` — keine `CoopPlayer`
- [ ] Phase 3: Zonen (`VoiceZone`) und Portale (`VoicePortal`) bleiben **optionale** Komponenten auf Wänden/Türen — der Graph funktioniert mit Fallback (`OcclusionModifier`) auch ganz ohne sie, ein Nutzer *kann* sie für bessere Akustik hinzufügen, muss aber nicht
- [ ] Kein Schritt in irgendeiner Phase darf verlangen, dass der Nutzer Netzwerk-Code, Player-IDs oder Audio-Filter manuell verdrahtet

Wenn ein Arbeitspaket dazu führt, dass der Nutzer mehr als eine Komponente anfassen oder ein Feld manuell setzen muss, gehört das in den Advanced-Modus (optional, versteckt) — nicht in den Standardweg.

---

## Phase 1 — Multiplayer abkoppeln (Kernumbau)

Ziel: `com.earshot.voice` entsteht als eigenständiges Paket, das nichts von Netcode/NGO/UGS-Multiplayer weiß. Basis ist die neueste Voice-Iteration im Ordner `ProximityChatExport/` (netzwerk-unabhängig, mit `ProxVoice`-Fassade, `ProxVoiceRoster`/`ProxVoicePlayer`; siehe `docs/DECISIONS.md`, Eintrag „ProximityChatExport ist die neueste Voice-Iteration"). Der `Voice/`-Ordner in `com.earshot.coop` ist der ältere Stand und liefert nur noch die Test-Werkzeuge. Die zwei bekannten Bugs werden hier gleich mitgefixt, weil genau die betroffenen Klassen ohnehin umgebaut werden.

Ordner-Rollen ab jetzt: Dieses Repo ist das Voice-DevProject. Neuer Voice-Code entsteht **ausschließlich** unter `DevProject/Packages/com.earshot.voice/`. `DevProject` ist das Unity-Testprojekt (Szenen, Playtests) ohne Multiplayer-Paket. `com.earshot.coop` ist aus diesem Repo entfernt — das eigene Spiel räumt der Nutzer selbst auf (`docs/altes-earshot-entfernen.md`). `ProximityChatExport/` ist nur Quelle der Übernahme und wird nach Hörtest gelöscht.

- [x] **1.1 — `IProximityVoicePlayer`-Interface definieren** (`PlayerId`, `VoiceAnchor`, `Position`) — `ProxVoicePlayer`/`ProxVoiceRoster` aus dem Export sind die funktionierende Vorstufe und werden dadurch abgelöst
- [x] Neues Package-Grundgerüst `com.earshot.voice` anlegen (eigene `.asmdef`, **keine** Abhängigkeit zu Netcode/Multiplayer-Services, nur Vivox + Unity Authentication) — entstanden unter `DevProject/Packages/com.earshot.voice/`, damit das Unity-Projekt es direkt kompiliert und playtestet
- [x] Code aus `ProximityChatExport/` übernehmen (Namespace `Earshot.Proximity` → `Earshot.Voice` umbenannt):
  - [x] `IVoiceBackend`, `VivoxVoiceBackend`
  - [x] `VoiceRuntime`, `VoiceEmitter`, `VoicePipeline`
  - [x] `VoiceContext`, `VoiceProfile`, `VoiceZone`, `VoicePortal`, `VoiceTransparent`
  - [x] `VoiceSessionLog`
  - [x] `Modifiers/` (Distance, Occlusion, Portal, Zone) — das Radio-Modul liegt im alten Paket als Sample (`Samples~/CustomVoiceModifier`) und ist Vorlage für die Übertragungswege in Phase 4
  - [x] `ProxVoice` (statische Fassade) zu `EarshotVoice` umgebaut — Grundlage der `EarshotProximityVoice`-Komponente
  - [x] `ProxVoiceSettings` → `EarshotVoiceSettings` als Settings-Asset übernommen, `ProxLog` → `EarshotVoiceLog` überführt
  - [x] `ProxVoiceMuteHotkey` / `ProxVoiceConnectInScene` gesichtet und als optionale Komponenten übernommen: `EarshotVoiceMuteHotkey`, `EarshotVoiceConnectInScene` (nicht auf den Player, nicht Teil des Standardwegs)
- [x] Test-Werkzeuge aus dem älteren `Voice/`-Ordner des alten Pakets übernehmen: `VoiceTestSpeaker` (AudioClip-Testlautsprecher durch die echte Pipeline), `VoiceSessionRecorder` — beide auf `EarshotVoice`/`VoiceRoster` umgestellt (kein `Coop`-Zustand mehr, Auslöser ist jetzt `EarshotVoice.IsConnected`)
- [x] Generisches Register gebaut: `VoiceRoster` löst `ProxVoiceRoster` und `PlayerRegistry` ab (`IProximityVoicePlayer` ↔ VivoxParticipant, `Register()`/`Unregister()`/`NotifyIdentityReady()`)
- [x] Öffentliche Komponente `EarshotProximityVoice` gebaut (einzige Pflichtkomponente auf dem Player)
  - [x] Zero-Config-Modus (Standard `isLocalPlayer = true`, PlayerId füllt sich selbst über `EarshotVoice.LocalPlayerId`, sobald Unity Authentication durch ist)
  - [x] Advanced-Modus (`Bind()` für Netzwerk-Adapter, Inspector-Felder für Player-ID/Voice-Anchor manuell überschreibbar)
- [x] **Bug 1 — 2-Sekunden-Delay**: Fix aus `ProximityChatExport/` übernommen (`tap.loop = true` in `VoiceEmitter.ConfigureTap()` + Selbstheilung in `KeepVivoxStreamAlive()`); keine gesonderten Timing-Messpunkte ergänzt, da der Export-Stand den Fehler bereits behoben hatte
- [x] **Bug 2 — Muffle/Reverb/Distanz-Kurve**: Fix aus `ProximityChatExport/` übernommen (`VoiceEmitter.Apply()` überträgt Volume, SpatialBlend, LowPass/HighPass, Reverb; Filter-Komponenten entstehen in `AttachFilters()` per `AddComponent`)
- [x] ~~NGO-Adapter `NetcodeVoicePlayer` im alten Paket~~ — **entfallen**. Nutzer entfernt `com.earshot.coop` selbst aus dem Spiel (`docs/altes-earshot-entfernen.md`) und bindet danach nur `com.earshot.voice` ein. `com.earshot.coop` ist aus diesem Repo gelöscht.
- [x] Tests: EditMode-Tests für das generische Register ohne Netzwerk (`VoiceRosterTests`) plus `VoiceSample`-Mathematik (`Clamp`, `MoveTowards`)
- [ ] Hörtest mit `VoiceTestSpeaker` + itsjessamess-Clip: Delay weg, alle drei Effekte (Muffle, Reverb, Distanz-Falloff) einzeln hörbar korrekt
- [ ] Nach Übernahme und bestandenem Hörtest: `ProximityChatExport/` aus dem Repo löschen — der Stand bleibt über die Git-Historie gesichert (Commit `1e78b0f`); `ANLEITUNG.md` vorher auf Phase-5-Relevanz gesichtet

---

## Phase 2 — Altes Earshot im eigenen Spiel entfernen (Nutzer, nicht dieses Repo)

Ziel: im **Spielprojekt** kein `com.earshot.coop` mehr, keine doppelte Voice-Pipeline, keine Missing Scripts — danach erst `com.earshot.voice` einfügen. Schritt-für-Schritt: `docs/altes-earshot-entfernen.md`.

In diesem Repo ist das Multiplayer-Paket bereits weg. Die Checkliste unten gilt für das eigene Unity-Spiel:

- [x] Inventur / altes Paket im Spielprojekt entfernt (Nutzer, 2026-09-06)
- [ ] Diesen Repo-Stand pushen, dann im Spiel Git-URL einbinden (Weg A, siehe `docs/DECISIONS.md`)
- [ ] Player-Prefab: `EarshotProximityVoice`
- [ ] `EarshotVoice.ConnectAsync` am Match-Start, `Bind` auf lokal/remote
- [ ] Settings-Asset unter `Assets/Resources/EarshotVoiceSettings.asset`

---

## Phase 3 — Raum-Portal-Graph (realistischer Proximity Chat)

Läuft jetzt vollständig innerhalb von `com.earshot.voice`, komplett multiplayer-unabhängig. Entspricht v1.1 aus der bisherigen `ROADMAP.md`.

- [ ] Datenmodell: Zonen (`VoiceZone`) als Knoten, Portale (`VoicePortal`: Tür, Durchgang, Treppenlauf, Galerie, Schacht) als Kanten
- [ ] Kantengewicht = akustische Länge + Dämpfung aus `Openness`
- [ ] Pfadsuche: Dijkstra/A* pro Sprecher/Hörer-Paar
- [ ] Direkte Sichtlinie bleibt Schnellpfad, wenn frei; sonst gewinnt der Graph
- [ ] Neuer `GraphModifier` in die bestehende Modifier-Kette einhängen
  - [ ] `OcclusionModifier` bleibt als Fallback für draußen / ungebakte Level erhalten
- [ ] Tür-Logik:
  - [ ] Offen (`Openness >= Schwellwert`): Kante fast kostenlos
  - [ ] Geschlossen: Kante teuer (ClosedVolume + Muffle), nicht automatisch stumm
  - [ ] Mehrere Türen hintereinander: Dämpfung addiert sich
  - [ ] Tür öffnet sich während des Sprechens: weicher Übergang, kein Knacken
- [ ] Treppenhaus/Etagen:
  - [ ] Treppenlauf als Portal zwischen zwei Zonen
  - [ ] Vertikale vertikale Luftlinie durch Decke zählt nur als Occlusion, wenn kein Treppen-Portal verbindet
  - [ ] Länge der Treppe geht in `AcousticDistance` ein
- [ ] Richtung/Beugung:
  - [ ] `ApparentDirection`: Stimme kommt hörbar aus der offenen Tür/Flurecke, nicht durch die Wand
  - [ ] Mehrere kurze Rays / Offset-Fächer für "halb hinter der Kante"
- [ ] Autorentools:
  - [ ] Editor-Tool: Zonen aus Collidern erzeugen
  - [ ] Portale an Tür-Prefabs automatisch erkennen
  - [ ] Bake-Button: Graph im Editor vorberechnen
  - [ ] Gizmos: Räume, Kanten, gewählter Schallweg
  - [ ] Preflight-Warnungen: "Zone ohne Portal", "Portal ohne zwei Zonen", "Spieler außerhalb jeder Zone"
- [ ] Tests:
  - [ ] L-Flur, Tür offen: Graph-Weg leiser als freie Sicht, aber viel lauter als durch die Wand
  - [ ] Dieselbe Tür zu: dumpf, Volumen im ClosedVolume-Bereich
  - [ ] Zwei Etagen, nur Decke dazwischen: stark gedämpft
  - [ ] Zwei Etagen plus Treppen-Portal: Distanz ≈ Treppenlänge, kein Decken-Cut
  - [ ] Rahmenstreifschuss bei offener Tür: nicht voll occluded

---

## Phase 4 — Übertragungsgeräte (Walkie-Talkie)

Ziel: Funkgeräte als **optionale** Welt-Objekte — die Stimme läuft über einen separaten Funkkanal und wird am Gerät des Empfängers abgespielt. V1-Regel: Funk ersetzt die Mund-Stimme, solange gesendet wird (Begründung und Alternativen: `docs/DECISIONS.md`, Eintrag „Walkie-Talkie V1"). Baut auf der abgekoppelten Architektur aus Phase 1 und der stabilen Pipeline auf.

- [ ] Vivox-Multi-Kanal-Fundament: separater Funkkanal pro Kanal-ID, paralleles Senden (Mikrofon in Proximity-Kanal **und** Funkkanal) — `IVoiceBackend`/`VivoxVoiceBackend` erweitern
- [ ] `EarshotWalkieTalkie`-Komponente (optional, Welt-Objekt): Funkkanal-ID, eigener Transform als Voice-Anchor, Push-to-Talk-Input
- [ ] Empfang: Funk-Stimme am eigenen Gerät abspielen — blechernes EQ-Band (HighPass+LowPass nach Vorbild `RadioModifier`-Sample), Position folgt dem Gerät (Hand/Hüfte/Boden automatisch über Unity-3D-Audio)
- [ ] Umgebungs-Leak: jeder in Reichweite (~2–5 m) eines empfangenden Geräts hört die Funk-Stimme leise vom Gerät
- [ ] Sender-Dämpfung: Mund-Stimme des Senders leiser, solange er funkt (ins Gerät sprechen)
- [ ] Tests: EditMode-Tests für Kanal-Zuordnung und die „Funk ersetzt Mund"-Regel ohne Netzwerk
- [ ] Hörtest: zwei Walkies in der Testszene, alle Verhaltensregeln einzeln verifiziert
- [ ] Bewusst nicht in V1: Half-Duplex, Squelch-Knacksen, Reichweitenlimit/Batterie, dominanter Pfad mit weicher Überblendung

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


