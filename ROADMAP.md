# Earshot — Plan fuer kuenftige Versionen

> Veraltet als Arbeitsdatei. Aktueller Plan: `docs/earshot-voice-plan.md`.

Stand: 16.08.2026 · Status: **nur Planung, nicht umsetzen**

Diese Datei sagt, was nach der jetzigen Fassung noch gebaut werden muss, damit
Multiplayer **stabil und fluessig** laeuft und Proximity Chat sich **richtig anhoert**:
durch Waende, durch offene Tueren, um Ecken, im Treppenhaus, ueber Etagen.

Sie ergaenzt `PLAN.md` (Architektur) und `TASKS.md` (abgeschlossene Erstfassung).
Nichts hierin ist Auftrag zum Sofortbauen.

---

## Zielbild

Zwei Spieler in einem echten Level (Hotel, Haus, Buero) sollen:

- in unter zwei Sekunden joinen, ohne dass Szenen oder Audio kaputtgehen
- sich ohne Ruckeln, Gummiband oder Teleport-Zucken sehen
- sich gegenseitig hoeren, sobald sie im selben akustischen Raum sind
- hinter einer **geschlossenen Tuer** dumpf und leise klingen, nicht stumm und nicht klar
- hinter einer **offenen Tuer** klingen, als stünde nichts im Weg — auch wenn die
  Luftlinie durch den Tuerrahmen geht
- **um eine Flurecke** klar klingen, weil der Schall den Flur nimmt, nicht die Wand
- im **Treppenhaus** ueber Etagen hoerbar sein, mit Distanz entlang der Treppe, nicht
  durch die Betondecke
- denselben Eindruck behalten, wenn jemand die Tuer waehrend des Sprechens oeffnet
  (weicher Uebergang, kein Knacken, keine halbe Sekunde Stille)

Was heute fehlt, ist vor allem: Schall kennt nur die **gerade Linie**. Deshalb klingt
jemand um die Ecke wie hinter Beton. Der Rest der Roadmap macht Sitzung und Stimme
betriebssicher, damit dieser akustische Graph in einem echten Spiel nicht zerbricht.

---

## Was heute gilt (nicht wieder aufmachen)

Diese Leitplanken bleiben in allen zukuenftigen Versionen:

1. Vivox bleibt ein **flacher 2D-Kanal**. Raeumlichkeit entsteht nur in `VoicePipeline`.
2. Nur `VoicePipeline` darf Raycasts / Graph-Suchen machen. Modifier lesen `VoiceContext`
   und schreiben nur `VoiceSample`.
3. Nur `VoiceEmitter` fasst `AudioSource` an. Vivox-Tap-GameObjects niemals zerstoeren.
4. `SceneCoordinator` laedt Szenen **additiv**. Client-Szenen nicht entladen.
5. Host-Client ueber Relay bleibt der Standardweg. Dedizierte Server sind optional spaeter.
6. Maximal **8 Spieler** ohne Culling. Darueber erst mit Voice-Culling / Interest-Management.

---

## Ist-Stand: warum es sich noch nicht „fertig“ anfuehlt

### Multiplayer

| Luecke | Folge |
|---|---|
| Host weg = Session tot | Runde stirbt, wenn der Gastgeber disconnectet |
| Kein Reconnect | WLAN-Hiccup wirft raus, Join-Code ist verbraucht |
| `NetworkTransform` ohne Feintuning | Andere Spieler zucken bei Ping-Spitzen |
| Stimme und Avatar werden ueber UGS-IDs gebunden | Eine abweichende ID → Tap bei Weltursprung, dann Stille |
| Keine Netz-HUD | Man sieht nicht, ob Relay, Vivox oder Interpolation das Problem ist |
| Late Join / Szenenwechsel | Zweiter Spieler kann in der falschen Szene oder ohne Voice-Tap stehen |
| Demo-Bewegung ist kein Spiel-Movement | Fremde Projekte muessen Interpolation selbst richtig setzen |

### Proximity

| Luecke | Folge |
|---|---|
| Ein SphereCast auf der Luftlinie | Um die Ecke = Wand, obwohl der Flur frei ist |
| Offene Tuer nur, wenn der Strahl die Tuer trifft | Daneben stehen → immer noch Wand |
| Keine Etagen / Treppen als akustischer Weg | Stimme durch die Decke oder gar nicht, statt die Treppe runter |
| `FullOcclusionHits = 1` | Ein Streiftreffer (Rahmen, Kapsel, Sturz) macht fast stumm |
| Keine Beugung (Diffraction) | Halb hinter einer Saeule kippt hart zwischen klar und dumpf |
| Richtung = Sprecherposition | Stimme kommt „durch die Wand“, nicht aus der Tueröffnung |
| Zonen sind optionale Trigger | Ohne Handarbeit kennt das Level keine Raeume |
| Hall nur grob pro Zone | Treppenhaus, Bad, Flur klingen gleich |

Die Fixes an Kapseln, Ball und „nicht auf 0 stumm schalten bis der Avatar da ist“ gehoeren
zur aktuellen Fassung. Sie sind Voraussetzung, nicht die Zielakustik.

---

## Versionen

Jede Version hat ein **hoerbares oder spielbares Abnahmekriterium**. Erst wenn das sitzt,
die naechste anfassen. Reihenfolge ist Absicht: erst Schallwege, dann Komfort, dann
Betrieb, dann groessere Runden.

### v1.1 — Schallwege (Ecken, Tueren, Treppenhaus)

**Abnahme:** In einem L-Flur mit offener Tuer klingt der Sprecher um die Ecke **nah und
klar**. Dieselbe Person hinter einer **geschlossenen** Zimmertuer klingt dumpf. Im
Treppenhaus ist jemand eine Etage tiefer ueber die Treppe hoerbar, nicht durch die Decke.

#### Raum-Portal-Graph

- Raeume (`VoiceZone`) und Oeffnungen (`VoicePortal`: Tuer, Durchgang, Treppenlauf,
  Galerie, Schacht) bilden einen Graphen.
- Kante = Portal. Gewicht = akustische Laenge + Daempfung aus `Openness`.
- Pro Sprecher/Hoerer: kuerzester Weg (Dijkstra / A*). `VoiceContext` bekommt zusaetzlich
  z. B. `AcousticDistance`, `PathPortals[]`, `ApparentDirection`, `DiffractionAmount`.
- Direkte Sichtlinie bleibt als schneller Pfad, wenn frei. Sonst gewinnt der Graph.
- `OcclusionModifier` bleibt fuer Draussen und als Fallback, wenn kein Graph gebaut ist.

#### Offene und geschlossene Tueren

- Offene Tuer (`Openness >= Schwellwert`): Kante fast kostenlos. Keine extra Wanddaempfung
  durch Rahmen/Sturz auf demselben Weg.
- Geschlossene Tuer: Kante teuer (ClosedVolume + Muffle), aber nicht tot, ausser das Spiel
  setzt ClosedVolume bewusst auf 0.
- Tuer oeffnet sich waehrend jemand spricht: Glaettung aus dem Voice-Profil, kein Knacken.
- Mehrere Tueren hintereinander (Zimmer → Flur → Zimmer): Daempfungen **addieren** sich
  entlang des Weges, nicht „erste Wand gewinnt“.

#### Treppenhaus und Etagen

- Ein Treppenlauf ist ein Portal zwischen zwei Zonen (Etage n / n+1), nicht eine Wand.
- Vertikale Luftlinie durch die Decke zaehlt als Occlusion, **wenn** kein Treppen-Portal
  auf dem Graphen verbindet.
- Offenes Treppenauge / Galerie: eigenes Portal mit wenig Daempfung, groesserer Hall.
- Laenge der Treppe geht in `AcousticDistance` ein (man hoert jemanden „weiter weg“,
  obwohl er 3 m senkrecht unter einem steht).

#### Beugung und Richtung

- Mehrere kurze Rays oder ein Offset-Faecher fuer „halb hinter der Kante“.
- `ApparentDirection`: AudioSource / Spatial Blend so drehen, dass die Stimme aus der
  **oeffnenden Tuer / Flurecke** kommt, nicht durch die Trennwand.
- Capsules, CharacterController, lokale Kamera bleiben vom Cast ausgeschlossen.

#### Autorentools (ohne die das Graph in fremden Projekten nicht fliegt)

- Editor: Zonen aus Collidern erzeugen oder aus markierten Volumes.
- Portale an Tuer-Prefabs automatisch erkennen (Collider + `VoicePortal`).
- Bake-Button: Graph im Editor vorberechnen, zur Laufzeit nur Offeneheit updaten.
- Gizmos: Raeume, Kanten, gewaehlter Schallweg zwischen zwei Debug-Punkten.
- Preflight: „Zone ohne Portal“, „Portal ohne zwei Zonen“, „Spieler ausserhalb jeder Zone“.

#### Tests (EditMode + hoerbare Szene)

- L-Flur, Tuer offen: Graph-Weg leiser als freie Sicht, aber viel lauter als durch die Wand.
- Dieselbe Tuer zu: dumpf, Volumen im Bereich ClosedVolume.
- Zwei Etagen, nur Decke dazwischen, keine Treppe: stark gedämpft.
- Zwei Etagen plus Treppen-Portal: Distanz ≈ Treppenlaenge, kein Decken-Cut.
- Rahmenstreifschuss bei offener Tuer: nicht voll occluded.

---

### v1.2 — Fluessige Avatare und ehrliche Stimme

**Abnahme:** Bei 80–120 ms Relay-Ping laufen andere Spieler ruhig. Stimme sitzt am Kopf,
nicht 0,5 s am Ursprung und dann weg. Join in einer fremden Szene zerstoert nichts.

- NetworkTransform: Owner-Auth belassen, Interpolation/Buffer fuer Remote-Spieler
  dokumentieren und im Wizard setzen (kein Roh-Teleport bei jedem Tick).
- Optional: leichtes Client-Side-Prediction nur fuer den lokalen CharacterController —
  Remote bleibt interpoliert. Kein Rollback-Shooter.
- Voice-Bind haerten: UGS-ID, Fallback „der andere Spieler“, niemals `SilenceImmediately`
  solange ein Remote existiert; Tap erst an den Anchor, dann Kanalmix stummschalten.
- Late-Join: vorhandene Vivox-Teilnehmer nachziehen (`ActiveChannels`), `IsInAudio` abwarten.
- Szenen: additive Loads pruefen, AudioListener genau einer (lokaler Kopf), Lobbykamera aus.
- Fester Tick / Send-Rate fuer Tuer-Zustand (`NetworkDoor` / Portal-Openness), damit
  akustischer Graph und sichtbare Tuer nicht auseinanderlaufen.
- Preflight-Checkliste fuer fremde Projekte: Prefab hat NetworkObject, CoopPlayer,
  NetworkTransform, VoiceAnchor; Szene hat NetworkManager, CoopBootstrap, Spawn-Punkte.

---

### v1.3 — Voice-Komfort und Diagnose

**Abnahme:** Man sieht, wer spricht, kann einzelne Leute leiser drehen, und bei „ich hoere
nichts“ sagt das Overlay in einem Satz warum.

- Push-to-Talk (konfigurierbare Taste) und optional Voice-Activation.
- Sprechindikator am Avatar und in der Spielerliste (Vivox SpeechDetected).
- Lautstaerke und Mute **pro Mitspieler** (lokal).
- Geraetewahl bleibt im Pause-Menue; Pegelanzeige fuer Mikro (kein Clipping-Raten).
- Overlay (F3) ausbauen: AcousticDistance, gewaehlter Portal-Weg, Occlusion, Bind-Status,
  Vivox-ID vs. UGS-ID, Ping.
- Kein Testball mehr in Spiel-Szenen. Fuer Allein-Tests: Debug-Clip-Injector an einem
  unsichtbaren Emitter in einer Zone, nur im Editor.

---

### v1.4 — Sitzung haelt

**Abnahme:** Host-WLAN 10 Sekunden weg → Client sieht „Verbindung unsicher“, danach wieder
drin oder saubere Meldung. Host beendet bewusst → Session endet klar, kein Haenger.

- Reconnect mit kurzem Timeout, gleicher Join-Code / Session-Token soweit UGS das hergibt.
- Host-Migration nur soweit ohne dedizierten Server ehrlich machbar; sonst klare UI
  „Gastgeber weg“. Keine halbfertige Migration, die Avatare verdoppelt.
- Disconnect-Gruende loggen: Relay, Auth, Vivox, NGO getrennt.
- Optional: Ping / Paketverlust klein in der Pause-UI.
- Join-Flow: Cursor, AudioListener, Voice-Connect in fester Reihenfolge, damit nicht
  wieder 2D-Mix → Stille passiert.

Noch nicht: Anti-Cheat, Dedicated Server als Pflicht.

---

### v1.5 — Groessere Orte, mehr Spieler

**Abnahme:** 8 Spieler in einem mehrstoeckigen Haus, CPU und Bandbreite bleiben ruhig.
Spieler drei Raeume weiter belasten das Ohr nicht.

- Voice-Culling: ausserhalb von AcousticDistance + Graph-Grenze kein Tap / lokal mute
  auf Netzwerkebene, soweit Vivox das zulaesst.
- Interest-Management fuer Transforms (NGO): weit entfernte Koerper seltener updaten.
- Graph-Bake in Chunks / Etagen, nicht jedes Frame neu suchen.
- Optional grobe Vivox-Positional-Channels nur als Vorfilter — nie als Ersatz fuer die
  lokale Pipeline.
- Erst hier Richtung 16 Spieler denken. Davor nicht.

---

### v1.6 — Feinschliff Akustik (nach dem Graph)

Nur wenn v1.1 im echten Level ueberzeugt.

- Hall-Presets: Flur, Treppenhaus, Bad, grosse Halle, Aussen.
- Luftabsorption entlang des Graph-Weges (Hohe Frequenzen sterben in langen Fluren).
- Leise Transmission durch duenne Waende vs. harte Occlusion durch Beton
  (Material-Tags oder Layer).
- Mehrere gleich gute Wege: der offenste gewinnt, nicht ein zufaelliger.
- Occlusion-Faecher fuer halboffene Tuer (Spalt).

---

### Spaeter, bewusst nicht jetzt

| Thema | Warum spaeter |
|---|---|
| Steam Sockets / Freundesliste | Braucht Steam-App, eigener Transport hinter `ITransportProvider` |
| Dedizierte Server (Multiplay) | Fuer 2–8 Freunde unnoetig, Host-Client reicht |
| WebGL | Keine Vivox-Audio-Taps, kein Muffling |
| Eigener Voice-Codec | Vivox bleibt |
| Volles Prediction/Rollback | Koop, kein kompetitiver Shooter |
| Automatisches NavMesh-zu-Akustik ohne Autor | Zu unzuverlaessig; Bake + Zonen bleiben fuehrend |

---

## Reihenfolge der Arbeit (wenn es so weit ist)

1. **v1.1 Graph** — ohne den klingt jedes Hotel falsch, egal wie stabil der Join ist.
2. **v1.2 Bindung + Interpolation** — sonst wirkt Voice „kaputt“, obwohl der Graph stimmt.
3. **v1.3 Diagnose** — sonst debuggen wir wieder im Blindflug mit Freunden.
4. **v1.4 Reconnect / Host** — erst wenn 2–4 Spieler eine Stunde durchhalten sollen.
5. **v1.5 Culling** — erst wenn Level und Spielerzahl es brauchen.
6. **v1.6 Material und Hall** — Politur, kein Fundament.

---

## Fremdes Projekt (Hotel, fertige Szene)

Wenn Earshot in ein bestehendes Level kommt, ist die Reihenfolge:

1. Paket einbinden, Wizard, Player-Prefab, ein Cloud-Projekt, **kein Testraum**.
2. Zonen auf Zimmer / Flur / Treppe / Lobby legen.
3. `VoicePortal` an jede Tuer und jeden Treppenlauf, `Openness` an die bestehende
   Tuer-Logik koppeln.
4. Graph backen, mit F3 zwei Spieler durch Flur, Tuer, Treppe schicken.
5. Erst dann Feinschliff an ClosedVolume / Hall.

Ohne Zonen und Portale kann v1.1 den Graph nicht fuellen — dann bleibt die Luftlinie,
und Treppenhaus plus Ecken klingen weiter falsch.

---

## Erfolg messen

Nicht an Feature-Listen, sondern an Saetzen im Spiel:

- „Ich hoere dich um die Ecke, als kämst du den Flur entlang.“
- „Tuer zu: dumpf. Tuer auf: sofort klar, ohne Knackser.“
- „Du bist eine Etage tiefer an der Treppe, nicht in meinem Boden.“
- „Du zuckst nicht, wenn das Netz hakt — hoechstens etwas spaeter.“
- „Wenn jemand rausfliegt, steht das da, und Join klappt nochmal.“

Wenn einer dieser Saetze nicht stimmt, gehoert der Fix in die Version oben, nicht in
einen Sonderpfad im aktuellen Code.
