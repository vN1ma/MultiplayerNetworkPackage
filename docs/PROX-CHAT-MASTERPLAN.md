# Proximity-Chat Masterplan (v1, 2026-09-19)

> Von oben betrachtet: Was ist das Endziel, wie steht das Fundament wirklich da, und in
> welcher Reihenfolge bauen wir? Dieser Plan ist die Referenz — OFFENE-PUNKTE.md bleibt
> die Abhak-Liste für Details. Phase-für-Phase abarbeiten, nach jeder Phase verifizieren.

## 1. Endziel (präzise definiert)

- **E1 — Physikalisch plausible Sprachübertragung:** Distanz, Wände, Türen (offen/zu/teilweise),
  Räume (Hall), Treppen und Aufzüge verändern hörbar, wie Stimmen ankommen. Kein „Ton ignoriert
  die Welt".
- **E2 — Echtzeit & latenzarm:** alles läuft flüssig ohne spürbares Nachdenken; kein audible
  Poppen/Ruckeln bei Zustandswechseln (Tür schließt).
- **E3 — Walkie als Weltobjekt:** liegt ein Walkie irgendwo und gibt Ton ab (Sidetone/Musik),
  verhält sich dieser Geräteton physikalisch wie eine Stimme an diesem Ort — inkl. Türen,
  Wänden, Zonen. (Der Funkweg Mikrofon→Funkkanal bleibt bewusst unphysikalisch.)
- **E4 — Teleport-Türen sind akustische Türen:** Steht jemand vor der AQUARIUM-Tür und der
  Kollege ist durchgeportet, hört man ihn wie durch eine Tür — nicht wie aus dem Nichts.
- **E5 — Robust gegen Veränderung:** Szene/Layout ändert sich noch oft; später generieren
  Stockwerke sich teils random. Zonen/Portale dürfen nie pro Variante von Hand getunt werden
  müssen.
- **E6 — Multiplayer-konsistent:** Alle Clients hören dasselbe (Türzustände sind bereits
  netzgesynct — SimpleDoor.networkIsOpen ist für alle lesbar).

## 2. Ist-Stand: Fundament-Check (ehrlich, code-verifiziert 2026-09-19)

**Was da ist und dem Branchenstandard entspricht** (NICHT corner-cutting):

- **Portal-Graph + Dijkstra** (`VoiceZone`-Knoten, `VoicePortal`-Kanten, `VoiceGraphSearch`):
  exakt so machen es fertige Spiele für Schall-Ausbreitung — Räume als Knoten, Öffnungen als
  Kanten, Weglänge ersetzt die Luftlinie. „Der Ton läuft um die Ecke durch die offene Tür"
  ist genau das, was der Graph liefert, sobald Zonen/Portale in der Szene liegen.
- **Stimme scheint aus dem Türrahmen zu kommen** (`ApparentPosition` = erstes Portal): ein
  Detail, das Profi-Audio ausmacht — Richtung stimmt mit dem Schallweg überein.
- **Unity-3D-Spatialization** (spatialBlend=1) + Vivox-Transport; Modifikator-Kette sauber
  getrennt (Distanz, Occlusion, Graph, Zone, Reverb, LowPass).
- **Performance:** pro Sprecher-Hörer-Paar ~5 SphereCasts + 2 OverlapSphere + Dijkstra über
  ~20–100 Knoten — Mikrosekunden bis wenige Millisekunden, komplett ohne GC (NonAlloc-Buffer).
  Echtzeit ist realistisch, kein Problem.

**Echte Lücken (ehrlich):**

- **L1 — Hotelszene hat 0 Zonen/Portale** (Szene-Scan 2026-09-19, nur EarshotHearingTest hat
  welche). Dadurch läuft der Fallback: Sichtlinien-Occlusion. DAS ist der Sound, der sich
  gerade „falsch" anfühlt — es fehlt Inhalt, nicht Architektur.
- **L2 — „1 Wand = volle Dämpfung"** (Default `WallsUntilFullMuffle = 1`): abrupt und hart,
  aber profil-tunbar (weichere Kurve einstellbar).
- **L3 — Walkie-Geräteton umgeht den Graphen** (reine Luftlinie, `(1−d/max)²`).
- **L4 — Portale verbinden nur geometrisch benachbarte Zonen** (Probe ±Vorwärtsachse 0,4–4 m).
  Eine Portal-Kante zwischen zwei weit entfernten Zonen (Teleport-Brücke) geht noch nicht.
- **L5 — Jede Offenheits-Änderung baut den kompletten Graphen neu** (`Openness`-Setter →
  `MarkDirty`). Billig, aber unnötig — inkrementell machbar.
- **L6 — Keine echte Beugung/Diffraction an Kanten, Reverb ist Mischwert, nicht geometrisch.**
  Das ist die Steam-Audio-Klasse — optional, siehe Phase 5.

**Diagnose der Beobachtung „neben der offenen Tür wird es abrupt leiser":** Das ist der
Occlusion-Fallback. Sobald der Graph greift, wird Occlusion buchstäblich übersprungen
(`OcclusionModifier`: `if (context.UsedGraph) return;`) und die Lautstärke folgt dem Laufweg
durch die offene Tür — kontinuierlich, ohne Abbruch. Dass es im Hörraum trotzdem abrupt war,
heißt: dort war eine Bedingung nicht erfüllt (Flur/Außenwelt keine Zone, Portal-Seiten nicht
aufgelöst, oder beide in derselben Zone). **Das F7-HUD zeigt ab jetzt live, welche Bedingung
fehlte** (`UsedGraph?`, Zielzone, „KEIN WEG") — Phase 0 klärt das.

**Fazit:** Das Fundament ist solide, performant und dem Standard entsprechender Ansätze —
nicht wackelig. Was fehlt: Content-Authoring (L1) und vier gezielte Erweiterungen
(L2–L5). Kein Neubau nötig.

## 3. Architektur-Entscheidung

Wir bleiben auf dem **Portal-Graph-Ansatz** (statt z. B. Steam Audio):
multiplayer-freundlich (deterministisch, billig, pro Client identisch),
voll tunbar, klein. Steam Audio nur falls Phase 5 einen konkreten Bedarf zeigt.

## 4. Roadmap

### Phase 0 — Verifikation & Diagnose (jetzt, ~1 Spielsession)
- Meta-Fix einspielen (done, Commit bdddb86), Package aktualisieren, Unity neu starten.
- **F7-HUD im Hörraum und im Hotel testen.** Im Hörraum die „abrupt leiser"-Stelle
  aufsuchen und ablesen: Zielzone vorhanden? Weg gefunden? UsedGraph? → Ursache in
  OFFENE-PUNKTE dokumentieren und ggf. Szene korrigieren.
- Im Hotel erwartungsgemäß „keine Zone / 0 Räume" — bestätigt L1.

### Phase 1 — Graph-Infra erweitern (im Package)
- **1a:** `VoicePortal`: optionale **explizite Zonen-Zuweisung** (Zone A/B Felder; überschreiben
  die Achsen-Probe). Grundlage für Teleport-Brücken (E4) und für ungünstige Geometrie.
- **1b:** **Inkrementelle Offenheit:** Struktur-Rebuild nur, wenn Zonen/Portale entstehen/verschwinden;
  reine Offenheits-Änderung ohne Rebuild (Edge-Gewichte live).
- **1c:** **WalkieDeviceOutput an den Graphen** (E3): Zone des Geräts ermitteln, Pfad zum Hörer,
  gleiche Modifier-Kette wie die Stimme.
- **1d:** Occlusion-Tuning im Profil (WallsUntilFullMuffle > 1, weichere Kurve) — nach F7-Tests.

### Phase 2 — Authoring-Pipeline (Editor-Tool im HOTEL_GAME)
> Wichtig zum Verständnis: **Editor-Authoring = einmalig, gespeichert in der Szene.**
> Zur Spielzeit wird NICHT „ausgerechnet, wo Räume sind" — der Graph liest nur die platzierten
> Komponenten (Millisekunden, nur bei Struktur-Änderung). Genau das „einmal machen und
> beibehalten", wie gewünscht.
- **„Türen verkabeln" (automatisch):** findet alle `SimpleDoor`, setzt `VoicePortal` auf den
  Tür-Collider (Achse automatisch), hängt Openness-Adapter an (`door.IsOpen()` → `portal.Openness`,
  netzwerk-synchron). Idempotent.
- **„Zone aus Marker" (semi-auto):** Empty in die Raummitte ziehen → Tool raycastet zu
  Boden/Wänden/Decke → baut passende `VoiceZone`-Box. Nach Layout-Änderungen in Sekunden
  regenerierbar; Zimmer-Prefabs: einmal einrichten, gilt für alle Instanzen.
- **Validierungs-Report:** Portal ohne beide Seiten, überlappende Zonen, unverschlossene
  Bereiche ohne Zone — als Konsolen-/Fenster-Ausgabe.
- Teleport-Türen (`TeleportDoor`) bekommen KEIN Standard-Portal (siehe Phase 3).

**Sicherheitskonzept gegen „Tool zerstört die Szene":**
- Alles, was das Tool erzeugt, landet unter EINEM Wurzel-Container pro Szene
  (`_EarshotAudioGraph`). Zonen sind NEUE Kind-Objekte — die bestehende Geometrie
  (Wände, Böden, Meshes) wird nicht angefasst, nicht verschoben, nicht umbenannt.
- Einzige Ausnahme: an `SimpleDoor`-Objekten werden genau zwei Komponenten angehängt
  (`VoicePortal` + `SimpleDoorPortalLink`). Der Tool-Lauf listet vorab jede geplante
  Änderung als Preview/Dry-Run (nur Log, nichts wird geschrieben).
- **„Alles entfernen"-Button:** entfernt den Container UND exakt die angehängten
  Tool-Komponenten — nichts anderes. Nachweisbar über Markierung (z. B. ein leeres
  Marker-Skript oder Namenskonvention).
- **Idempotent:** erneut Ausführen überschreibt/vervielfacht nichts (vorhandene
  Zonen/Portale werden erkannt und aktualisiert statt dupliziert).
- Undo läuft über Unitys Undo-System; und die Szene liegt im Git → worst case
  `git checkout` der Szene. Doppeltes Netz.

**Verfahren „Zone aus Marker" — Schritt für Schritt (und warum genau so):**
1. Pro Raum einmal `Create Empty`, in die Raummitte ziehen, sinnvoll benennen
   (z. B. `Zone_Zimmer101`). Das ist die komplette Handarbeit — Sekunden pro Raum.
2. Marker selektieren → Tool-Button „Zone aus Marker": Das Tool raycastet vom Marker aus
   nach unten (Boden), oben (Decke) und 4× horizontal (Wände — Möbel-Collider werden per
   Layer übersprungen). Aus den Treffern berechnet es die Box, die den Innenraum füllt,
   und erzeugt ein Kind-Objekt mit `BoxCollider (Is Trigger)` + `VoiceZone` unter dem
   Container. Der Marker bleibt als Referenz stehen: Layout geändert → Marker (notfalls)
   verschieben → Button erneut → Zone wird aktualisiert, nicht dupliziert.
3. **Warum ein Marker statt Vollautomation:** Ein Punkt im Rauminneren definiert den Raum
   eindeutig, und die Wände dazu zu finden ist per Raycast robust. Ohne diesen Hinweis
   wäre „Was ist ein Raum?" mehrdeutig (offene Durchgänge, Nischen, angrenzende Flure,
   Möbel) — genau diese Mehrdeutigkeit macht Vollautomation fehleranfällig.
   **Warum nicht per Hand boxen:** ~15 Werte pro Raum abtippen statt 1 Punkt setzen,
   und nach jeder der vielen bevorstehenden Layout-Änderungen wieder von vorn.

### Phase 3 — Hotel-Rollout
- Zonen für Lobby, Flure, 10 Gästezimmer, Treppenhaus, Nebenräume; Portale für die 15
  physischen Türen (`SimpleDoor`).
- **Treppenhaus (D1 — entschieden 2026-09-19):** Der Treppenschacht wird EINE eigene
  vertikale Zone über alle Etagen; jeder Flur ist seine eigene Zone; Flur ↔ Treppenhaus
  sind daueroffene Portale (openness fest 1). Effekt: Stehen beide IM Treppenhaus
  (z. B. Etage 30 vs. 25), sind sie in derselben Zone → freie Luftlinie + hoher
  Reverb-Wert der Treppenhaus-Zone = es hallt durchs Haus. Stehen sie in den Fluren,
  läuft der Ton Flur → Portal → Treppenhaus → Portal → Flur, und die Hörweite ist der
  lange Laufweg → deutlich leiser. Wichtig: **Zonen verschmelzen NICHT, nur weil sie
  verbunden sind** — Portale halten sie getrennt. Damit werden 30 Flure + Treppenhaus
  eben NICHT ein Raum, sondern 31 Zonen; der Zähler wächst linear pro Etage (1 Flur-Zone
  + 1 Portal) → Dijkstra bleibt trivial. Ehrliche Einschränkung: „Hallen" ist ein
  Reverb-Mischwert (diffuser Klang), kein echtes Echo mit Nachhallzeit — echte
  Late-Reverb wäre Steam-Audio-Klasse (Phase 5).
- **Aufzug (D2 — entschieden):** eigene Mini-Zone; Aufzugtüren als Portale. Geschlossene
  Tür = gedämpft hörbar (`closedMuffle`/`closedVolume` des Portals), öffnet sie sich,
  wird der Fahrstuhlinhalt normal hörbar.
- **Teleport-Brücken (E4, D3 — entschieden):** `RandomRoomAssigner`/`FixedDoorManager`
  verkabeln bei Zuweisung automatisch akustische Portale (Hoteltür-Zone ↔ Raum-Zone).
  Verhalten EINHEITLICH über einen zentralen Stellregler in `VoiceHearingTuning`
  (Startwert: `travelLength`-Aufschlag +4 m — im Flur 4 m vor der Theme-Tür hört man
  den Kollegen auf der anderen Seite noch; Dämpfungsverhalten analog zu normalen Türen).
  Ein Wert geändert → alle Theme-Türen verhalten sich beim nächsten Lauf gleich.
  Technisch möglich über Phase 1a; alle Ziele liegen nachweislich in derselben Szene.

### Phase 4 — Prozedurale/random Stockwerke (E5)
- Der **Generator emittiert Zonen/Portale beim Bauen** — er kennt die Geometrie, die er gerade
  erzeugt (kleine Factory-API: `CreateZone(bounds, name)`, `LinkZones(a, b, collider, openness)`).
  Kein Szene-Scan, kein Hand-Tuning pro Variante. Das ist der langfristig richtige Weg und
  der Grund, warum Phase 2 eine Tool-Logik liefert, die wiederverwendbar sein muss.

### Phase 5 — Optionaler Realismus-Ausbau (nur bei Bedarf)
- Diffraction an Kanten, geometrischer Reverb, ggf. Steam-Audio-Evaluation.
- Bewusst letzter Punkt: Fein-Realismus lohnt erst, wenn Fundament (Phase 1) und Content
  (Phase 2–3) stehen.

**Kurzanalyse Steam Audio (D4):** Kostenlos (Valve), Unity-Plugin vorhanden. Liefert
geometrische Occlusion, Diffraction an Kanten, echte Reflexionen/Late-Reverb und HRTF.
Integration: Vivox-Transport bliebe erhalten (der Participant-Tap ist eine normale
AudioSource); ersetzt würde unsere Messstrecke — Steam Audios Ergebnisse (Occlusion %,
Distanz, Reverb) müssten auf `VoiceSample` gemappt werden statt Raycasts/Graph selbst zu
messen. Aufwand: 2–4 Wochen Evaluierung + Anbindung, PLUS neues Autoring (Akustik-Materialien
pro Fläche, Baking für statische Geometrie, Portale müssen dort ebenfalls definiert werden —
das Zonen-Authoring entfällt also nicht, es wandelt sich). Kein großflächiges Neu-Schreiben,
aber wir hätten ÜBERGANGSZEIT zwei Akustik-Systeme zu pflegen, weil Walkie-Weltton und
Teleport-Brücken (nicht-geometrisch!) weiter den Graphen brauchen. → Empfehlung unverändert:
erst wenn konkreter Bedarf.

## 5. Performance-Budget (Ziel)

| Vorgang | Frequenz | Kosten |
|---|---|---|
| Pipeline pro Sprecher-Hörer-Paar | jede Auswertung | < 1 ms (5 Raycasts + Dijkstra) |
| Graph-Rebuild | nur Struktur-Änderung | wenige ms (≈50 Komponenten + ≈100 Probes) |
| Offenheits-Update | pro Türoperation | ~0 nach Phase 1b |
| GC | nie | NonAlloc-Buffer überall (verifiziert) |

## 6. Entscheidungen & Offenes (Stand 2026-09-19)

- **D1 — entschieden:** Treppenhaus = eine vertikale Zone, Flure pro Etage eigene Zonen,
  daueroffene Portale dazwischen (Details Phase 3). „Durchs Treppenhaus hallen" über den
  Reverb-Wert der Treppenhaus-Zone.
- **D2 — entschieden:** Aufzug NICHT schalldicht — gedämpft durch die geschlossene Tür
  hörbar, Portale an den Aufzugtüren.
- **D3 — entschieden:** Theme-Türen über zentralen Stellregler in `VoiceHearingTuning`,
  Startwert +4 m `travelLength`; Verhalten analog zu normalen Türen, damit eine
  Stellschraube alle Türen einheitlich ändert.
- **D4 — analysiert, aufgeschoben:** Steam Audio siehe Kurzanalyse in Phase 5. Kein
  Neu-Schreiben nötig, aber Doppel-Pflege der Systeme → erst bei konkretem Bedarf.
- **D5 — geparkt:** Stockwerk-Generator existiert noch nicht; Konzept-Gespräch, sobald
  die Basis (Phase 1–3) steht.
- **Spieleranzahl:** ausgelegt auf 4, erweiterbar auf 8 → Performance irrelevant
  (max. 56 Paarungen × < 1 ms).


