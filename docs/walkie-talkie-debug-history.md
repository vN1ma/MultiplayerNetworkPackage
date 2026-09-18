# Walkie-Talkie — Debug-Historie (Stand 2026-09-18)

Dieses Dokument hält fest, **welche Symptome** auftraten, **welche Ursachen** vermutet und bestätigt wurden, **welche Fixes** versucht wurden und **warum der Eigenklang bei großer Entfernung trotzdem wiederkehrte**. Es ist Absicht, dass die gescheiterten Ansätze hier bleiben — sonst wiederholt sich dieselbe Schleife.

Repos: `HOTEL_GAME` (Spiel) + `MultiplayerNetworkPackage` / `com.earshot.voice` (Package).  
Aktueller Package-Stand der Diagnose-Revision: `capture-tx-follow-v4` (Capture-Tap folgt dem aktiven Sende-Kanal; Commit siehe Git-Historie).

---

## 1. Soll-Verhalten (was wir wollen)

1. Walkie in der Hand, PTT gedrückt → **kein** Abspielen der eigenen Stimme am Hand-Gerät.
2. Andere eingeschaltete Walkies auf demselben Kanal (z. B. Boden) → eigene Stimme **räumlich** von dort (Sidetone), mit Funk-EQ und Delay.
3. Außerhalb `MaxHearingDistance` → **vollständig still**, kein leises 2D-Hintergrund-Monitoring.
4. Mehrere Empfangs-Walkies in Reichweite → jedes Gerät spielt aus seiner Position (Fan-out).
5. Half-Duplex: während lokalem PTT kein Fremdempfang.
6. First-Speaker: bei mehreren Sendern nur der erste hörbar.
7. Kein zweites Unity-Mikrofon, keine unnötigen Allokationen/Hitches, niedrige Latenz.
8. Remote-Client: kein kurzer Freeze beim PTT des Partners; Radio-Effekt bleibt hörbar.

---

## 2. Ausgangssituation (gemeldete Symptome)

| Symptom | Wer | Kurzbeschreibung |
|--------|-----|------------------|
| Self-Echo / „Discord-Feeling“ | vor allem der Remote-Laptop-Nutzer (Parsec) | Stimme hörbar auch weit weg vom Boden-Walkie, oft 2D statt 3D |
| Kurzer Freeze | Kollege beim Empfang | Spiel ruckelt kurz, wenn der Sender PTT drückt |
| Radio-Effekt fehlt | beide connected | blecherner Walkie-Ton bricht weg oder kommt nicht |
| PTT nur einmal | nach ersten Fixes | nach Loslassen kein erneutes Senden möglich |
| „Nach Fix gar kein Sidetone mehr“ | Solo-Test | Boden-Walkie bleibt stumm |
| „Wieder meilenweit Self-Echo“ | Solo-Test nach `e46d900`/`cc68dba`-Vorbereitung | trotz harter Distanz-Cutoff in den Logs |

Asymmetrie (früh erkannt): Sender hört sich selbst; Empfänger friert. Das deutet auf **zwei verschiedene Pfade** (lokal Capture/Sidetone vs. Remote Tap-Pipeline), nicht auf einen einzigen gemeinsamen Bug.

---

## 3. Frühere Fehlannahmen (Chats / Game-Repo)

Diese Theorien wurden geprüft und **verworfen oder stark relativiert**:

| Annahme | Warum falsch / unvollständig |
|---------|------------------------------|
| Game und Package seien out-of-sync | Spiel nutzte den gepushten Package-Hash; Ownership-Fixes waren schon drin |
| `SetTransmitting` laufe auf allen Clients | Game-Code rief PTT nur beim Owner auf; `SetLocalOwnership` war verdrahtet |
| Doppelter `AudioListener` sei die Hauptursache | Kann Distanz verfälschen, erklärt weder programmierten Sidetone noch Tap-Rebuild-Schleifen |
| Sidetone sei „Bug“ und müsse weg | Sidetone war **gewollt** (Stimme vom anderen Gerät). Bug = **direkter 2D-Pfad** + falsche Hörweite |
| Parsec allein sei die Ursache | Proximity ohne Walkie war ok mit demselben Mic. Parsec ist legitimes Remote-Mic; erst der Walkie-Capture-Tap öffnet einen lokalen Unity-Wiedergabepfad |

---

## 4. Bestätigte Ursachen (mit Log-/Code-Belegen)

### 4.1 Tap-Recovery pro Player-ID (Freeze beim Kollegen)

Bei `TransmissionMode.Single` ist entweder Proximity- oder Funk-Tap absichtlich still. Die Selbstheilung bewertete „kein Signal“ als tot und baute **beide** Taps im ~6‑Sekunden-Takt neu auf.

- Folge: Hitches auf dem Empfänger, Radio-Pfad bricht weg, Proximity kann übrig bleiben.
- Fix: Recovery und Speaking-Checks über `VoiceSpeakerKey` (Player + Path + Channel).
- Commit-Linie: u. a. `94430b2`, Entscheidung „Tap-Recovery ist pfadbezogen“.
- Status: in Solo-Logs keine Rebuild-Schleife mehr; **Zwei-Client-Hörtest noch offen**.

### 4.2 PTT-Sync verliert Zwischenzustände

Schnelle PTT-Wechsel während laufendem Vivox-Sync gingen verloren → Funkkanal/Transmission blieb falsch.

- Fix: Dirty-Sync (`syncRequested` / Revision) in `WalkieRadioSync`.
- Status: Logs zeigen Start/Fertig + Nachlauf; ein Sync ~996 ms beim Loslassen ist noch beobachtenswert (nicht zwingend Frame-Freeze).

### 4.3 Sidetone über `Microphone.Start` (zweites Mikro)

Zusätzlich zu Vivox öffnete Sidetone ein Unity-Mikro. Auf dem Parsec-Setup: `Mikrofon (Parsec Virtual Audio)`.

- Folge: Treiber-/Virtual-Device-Monitoring, mögliche Hitches, Echo **unabhängig** vom räumlichen Walkie-Ausgang.
- Fix: Umstieg auf `VivoxCaptureSourceTap` (ein Capture-Stream).
- Commit: `c43479a`.
- Status: zweites Mic ist weg; **Eigenklang blieb**, weil der Tap selbst eine Unity-`AudioSource` speist.

### 4.4 Registry löscht lokalen TX falsch (PTT nur einmal)

Nach `SetTransmitting(false)` war `device.IsTransmitting` schon false; Registry prüfte erneut und ließ `LocalIsTransmitting` stehen.

- Folge: Half-Duplex/Sidetone-Zustand hängt; Walkie „kaputt“ nach erstem PTT.
- Fix: exakte `localTransmitDevice`-Referenz.
- Commit: `57dfe2e`.
- Status: wiederholbares PTT laut Registry/Tests ok.

### 4.5 Distanz / Listener für räumlichen Ausgang

Falsche oder verspätete Hörposition → Sidetone hörbar „überall“.

- Fixes: harter Cutoff bei `maxHearingDistance`, Preferenz für aktiven `AudioListener`, Self-Distance-Guard am eigenen Ohr.
- Status: **räumlicher Pfad funktioniert laut Log korrekt** (siehe 5.).

### 4.6 Direkter Capture-Tap-Ausgang (aktueller Hauptverdacht für „meilenweit“)

`VivoxCaptureSourceTap` schreibt in eine Unity-`AudioSource`. Vivox speist den Clip aktiv (`VivoxAudioProcessor` → `Play`). Auch wenn Samples im `OnAudioFilterRead` genullt werden, kann auf manchen Setups noch **direkter Mix** hörbar sein — oder Mute/Volume-0 virtualisiert den DSP und killt Sidetone ganz.

**Log-Beweis Solo-Test `voice-20260918-055128-998`:**

- Bei `8,20 m`: `WALKIE OUTPUT AUS … reason=OUT_OF_RANGE, target=0,000, actual=0,000`
- Parallel: Capture-Tap aktiv, `callbacks≈94`, Eingangssignal vorhanden
- Package-Hash im Spiel: `e46d900` (Filter-Nullung ohne Source-Mute)
- Nutzer hörte sich trotzdem

Schluss: Der Ton kam **nicht** vom räumlichen `WalkieDeviceOutput`. Distanz-Fixes konnten diesen Pfad nie lösen. Deshalb wirkte es wie „derselbe Scheiß“ — wir haben lange am falschen Ausgang gedreht.

---

## 5. Chronologie der Lösungsversuche

### Phase A — Feature-Bau (vor der Debug-Welle)

| Commit / Schritt | Idee | Ergebnis |
|------------------|------|----------|
| `046c982` | Funkkanal, Half-Duplex, Delay | Basis ok |
| `117abbd` | Sidetone + Fan-out + First-Speaker | Feature da, aber Sidetone über zweites Mic problematisch |
| `29066f9` / `407ea6f` / `d075613` | Clicks, Pitch, Feedback, EQ | Symptome am räumlichen Pfad gemildert, Echo-Root nicht |
| `a026740` / `8755d20` | Ownership-Guard, Self-Distance, AudioListener | Game-Integration robuster; „meilenweit“ blieb |

### Phase B — Zwei-Client-Stabilisierung (`94430b2`)

**Ansatz:** Pfadbezogene Tap-Recovery, Dirty PTT-Sync, harter Distanz-Cutoff, persistente Logs.

**Was besser wurde:** Grundlage gegen Freeze/Tap-Storm; bessere Sichtbarkeit.

**Warum Echo blieb:** Räumlicher Cutoff greift nur `WalkieDeviceOutput`. Direkter Capture-/Mic-Pfad war unberührt. Logs zeigten damals noch nicht klar genug „Walkie AUS, aber User hört trotzdem“.

### Phase C — Kein zweites Mikro (`c43479a`)

**Ansatz:** `VivoxCaptureSourceTap` statt `Microphone.Start`.

**Was besser wurde:** Kein zweites Device-Open; Release-tauglicher (kein Parsec-spezifisches `Microphone.Start`-Monitoring als Architektur).

**Warum Echo blieb / neue Probleme:** Tap braucht spielende `AudioSource`. `volume = 0` → auf manchen Backends kein `OnAudioFilterRead` → kein Sidetone. Unmuted Source → Risiko direkter 2D-Wiedergabe.

### Phase D — PTT-Instanz + `volume = 0` (`57dfe2e`)

**Ansatz:** Registry-Fix; Capture-Source hart auf Volume 0.

**Was besser wurde:** Wiederholbares PTT.

**Warum Nutzer „gar nichts“ hörte:** Volume 0 virtualisierte DSP → Feed bekam keine Samples → Bus leer → Boden-Walkie still. User-Feedback: „Pflaster statt Wurzel“ — berechtigt, weil man leise 2D vs. „gar nicht hörbar außer 3D“ verwechselt hatte.

### Phase E — Minimalpegel + Feed-Nullung (Workaround, verworfen)

**Ansatz:** `volume = 0.000001f`, danach Samples im Feed auf 0.

**Warum abgelehnt:** Klingt wie leises 2D-Monitoring; User will **null** 2D, nur 3D am Gerät. Industriell unsauber.

### Phase F — Feed-Nullung + `volume = 1` (`e46d900`)

**Ansatz:** Offizieller Vivox-Filterpfad: kopieren, dann `Array.Clear` auf dem Ausgangspuffer; Source Gain 1.

**Was besser wurde:** Sidetone-Datenfluss wieder da (Callbacks, Peaks in Logs).

**Warum Echo wieder da:** Log bewies räumliches Walkie bei >8 m stumm; Eigenklang blieb → Leak **vor** oder **neben** dem Filter (Vivox schreibt Clip + spielt Source). Filter-Clear allein reichte auf diesem Setup nicht.

### Phase G — Dual-Sperre + Mess-Logs (`cc68dba`, aktuell)

**Ansatz:**

1. Feed nullt weiterhin den Filterpuffer.
2. Zusätzlich `AudioSource.mute = true` (Unity: Mute = Ausgangspegel 0, Source bleibt spielend → DSP/Callback soll weiterlaufen).
3. Mute-Invariante jede Frame erzwingen.
4. Logs alle ~2 s: `inputPeak`, `directOutputPeak`, `callbacks`, `sourceMute`, `revision=capture-hardmute-v2`, plus `AUDIO DEVICES`.

**Status:** gepusht, **Hörtest steht aus**. Erfolgs-Kriterium im nächsten Log:

- `revision='capture-hardmute-v2'`
- `sourceMute=True`
- `directOutputPeak≈0` (oder -1 wenn GetOutputData bei Mute leer — dann trotzdem: kein hörbares 2D)
- bei großer Distanz: `WALKIE OUTPUT … OUT_OF_RANGE` und **subjektiv** stumm
- nah am Boden-Walkie: räumlicher Sidetone hörbar

Falls Echo trotz `directOutputPeak=0` und `sourceMute=True` bleibt → Ursache liegt **außerhalb** dieses Unity-Source-Pfads (OS/Parsec-Loopback, zweiter nicht geloggter Pfad, Vivox-nativer Output). Dann nächster Schritt: Capture-Tap während Diagnose ganz deaktivieren und vergleichen; Vivox Output Device / „No Device“-Strategien prüfen.

---

### Phase H — Capture-Tap fest auf Proximity pinnen (`capture-pin-proximity-v3`, **widerlegt**)

**Symptom:** Nach `cc68dba` hörte der Spieler während der kompletten PTT-Phase nichts von sich selbst: `signalBlocks=0` über die gesamte Sendezeit, native Tap liefert `NoMoreData` (~2 s Pause nach 20 verpassten Reads in `VivoxAudioProcessor`).

**Ursache (per Vivox-Runtime-Quellcode belegt):** `VivoxAudioTap` läuft mit `AutoAcquireChannel = true` (Standard) und registriert sich bei **jedem** `ChannelJoined`-Event auf `LastChannelJoinedUri` neu. Sobald `WalkieRadioSync` den Funkkanal joint, springt der Capture-Tap also stillschweigend auf den **Funkkanal** um. In genau diesem Zustand (Tap auf Funkkanal + Transmission auf Funkkanal) liefert der native Capture-Tap dauerhaft keine Daten. Im guten Log `20260918-0551` blieb der Tap zufällig an Proximity gebunden (kein Funk-Join vor dem PTT), deshalb floss dort Audio. Das Mute/AudioSource-Hardening aus Phase G war nicht schuld — der Tap wurde nie ausgehungert, sondern umgebunden.

**Fix:**

1. `VivoxVoiceBackend.ProximityChannelName` (neu, public) — exponiert den stabilen Sitzungskanal nach außen.
2. `WalkieSidetoneCapture.TryPinTapToProximityChannel()` — setzt am Tap `ChannelName = <Proximity>`. Der Setter deaktiviert `AutoAcquireChannel` und registriert den Tap fest auf genau diesen Kanal. Spätere Funk-Joins/Leaves lösen keine Umregistrierung mehr aus (ohne Auto-Acquire registriert Vivox nur beim Treffer des gepinnten Kanals neu — z. B. nach Reconnect auf Proximity).
3. Pin wird direkt nach der Tap-Erzeugung gesetzt und pro Frame (still) erneut geprüft/angestoßen — Selbstheilung, falls er je verloren geht.
4. `WALKIE CAPTURE FLOW` zeigt jetzt `tapChannel` + `autoAcquire`, das Erstellungs-Log ebenso. Neue Revision: `capture-pin-proximity-v3`.

**Erfolgskriterium im nächsten Solo-Log:**

- `revision='capture-pin-proximity-v3'`
- einmalig: `WALKIE Sidetone-Tap auf Proximity-Kanal '…' gepinnt: TapId=…, autoAcquire=False`
- während PTT auf dem Funkkanal: `CAPTURE FLOW: tapChannel='<proximity>', autoAcquire=False` mit `callbacks>0` und `signalBlocks>0`
- subjektiv: Sidetone während PTT hörbar

**Restrisiko:** Falls Vivox den Capture-Tap pro Kanal nur bei aktiver Transmission **dieses** Kanals speist, müsste Plan B greifen (Sidetone ohne Kanalbezug / alternatives Routing). Die neuen Log-Felder zeigen das sofort (dann `signalBlocks=0` trotz korrektem `tapChannel`).

**Ergebnis (Log `20260918-0652`): Restrisiko eingetreten — Phase H widerlegt.** Nach dem Pinning auf Proximity (`tapChannel='<proximity>', autoAcquire=False`) liefen während PTT zwar die Callbacks (`callbacks≈93/2 s`), aber `inputPeak=0.0000` und `signalBlocks=0`. Die Ursachenzuschreibung oben („Tap auf Funkkanal = tot") war falsch — siehe Phase I.

### Phase I — Capture-Tap folgt dem aktiven Sende-Kanal (`capture-tx-follow-v4`, aktuell)

**Symptom:** Solo-PTT-Test auf `capture-pin-proximity-v3`: Sidetone weiterhin stumm. Log `20260918-0652` zeigt zwei Dinge:

1. **Pin-Bug:** 529 Zeilen Pin-Spam mit `autoAcquire=True`. Der `ChannelName`-Setter in `VivoxAudioTap` bricht per Early-Return ab, wenn `m_LastChannelName` bereits den gewünschten Namen trägt — und genau das hatte Vivox' Auto-Acquire selbst schon gesetzt (Tap war automatisch auf Proximity registriert). Das Pinning war also ein No-Op, `AutoAcquireChannel` blieb `true`. Erst der Funkkanal-Join (der `m_LastChannelName` löschte) ermöglichte das echte Pinning (TapId 16→19) — danach fest auf Proximity.
2. **Kern-Erkenntnis:** Trotz korrekt gepinntem Tap auf Proximity: `callbacks=93, signalBlocks=0, inputPeak=0` während PTT auf dem Funkkanal. Vergleich mit dem guten Log `20260918-0551` (Tap war dort dem Auto-Acquire auf den **Funkkanal** gefolgt): `signalBlocks=13→71, peak=0,0361→0,0662`. **Vivox-Capture-Taps liefern nur Audio für den Kanal, auf den der lokale Teilnehmer gerade sendet.** Die Phase-H-Annahme war damit exakt verkehrt herum.

**Fix (`capture-tx-follow-v4`):**

1. `WalkieSidetoneCapture.TryPinTapToActiveChannel()` ersetzt `TryPinTapToProximityChannel()`: Während PTT wird der Tap auf den Funkkanal `earshot.radio.<id>` (`WalkieRules.ToVivoxRadioChannel`) gepinnt, im Ruhezustand auf Proximity. Der Pin wird pro Frame neu geprüft — der Tap wechselt also automatisch mit dem Sende-Kanal.
2. Reihenfolge im Pinning gefixt: **zuerst** `AutoAcquireChannel = false` setzen (löst seinerseits die Neuregistrierung aus), **danach** `ChannelName` setzen. Nur so ist das Pinning wirksam, wenn Vivox den Zielnamen bereits automatisch gesetzt hat (Phase-H-Spam-Bug).
3. Pin-Log nur noch bei Kanalwechsel (`lastPinnedChannel`), Fehler-Alerts auf max. alle 2 s gedrosselt.
4. Self-Heal: Wenn der Zielkanal bereits gepinnt ist, aber `TapId < 0` (Registration fehlgeschlagen/verloren), wird die Tap-Component kurz deaktiviert/reactiviert (`OnEnable` → `RegisterTapCore`).

**Erfolgskriterium im nächsten Solo-Log:**

- `revision='capture-tx-follow-v4'`
- bei PTT-Beginn: `WALKIE Sidetone-Tap auf Kanal 'earshot.radio.default' gepinnt … autoAcquire=False`, danach bei PTT-Ende zurück auf `'<proximity>'`
- während PTT: `CAPTURE FLOW: tapChannel='earshot.radio.default', autoAcquire=False` mit `callbacks>0`, `signalBlocks>0` und `inputPeak>0` beim Sprechen
- subjektiv: Sidetone während PTT hörbar (Boden-Walkie in Reichweite)

**Restrisiko:** Wenn die Neuregistrierung beim PTT-Start (Kanalwechsel Proximity→Funk) zu langsam ist, können die ersten ~100 ms Sidetone fehlen (Sustain-Gate im Feed fängt das auf). Falls selbst mit `tapChannel='earshot.radio.default'` `inputPeak=0` bleibt, wäre das ein Vivox-Seiteneffekt von `TransmissionMode.Single` — dann Plan B (Sidetone ohne Kanalbezug).

---


## 6. Warum es sich anfühlt, als kämen wir nicht weiter

1. **Zwei unabhängige Bugs** (Freeze/Tap-Recovery vs. lokaler Capture-Leak) wurden oft als ein Symptom behandelt.
2. **Sidetone ist Feature und Bug zugleich** — räumlich gewollt, global verboten. Fixes am Falloff wirken nur auf den Feature-Pfad.
3. **Unity/Vivox-Tap-Pipeline** speist zuerst einen Clip und spielt eine Source; Filter-Clear ist nötig, aber nicht immer hinreichend.
4. **Volume-0 vs. Mute vs. Minimalpegel** erzeugen jeweils andere Symptome (tot vs. leise vs. leckend) — ohne getrennte Messwerte wirkt jeder Fix zufällig.
5. **Frühere Logs** waren gut für PTT/Distanz, schlecht für „hört der User trotz Walkie-AUS noch etwas?“. Deshalb wirkten Logs „nutzlos“, obwohl sie den räumlichen Pfad längst freisprachen.

---

## 7. Was die Logs jetzt beweisen sollen (Checkliste)

Nach Package-Update im Spiel (`cc68dba`) und einem Solo-PTT-Test:

1. Datei unter `HOTEL_GAME/EarshotLogs/voice-*.txt` öffnen.
2. Einmalig: `AUDIO DEVICES: …`
3. Bei PTT: `WALKIE Vivox-Capture-Tap erstellt … revision='capture-tx-follow-v4'` und `WALKIE Sidetone-Tap auf Kanal 'earshot.radio.default' gepinnt … autoAcquire=False`
4. Periodisch: `WALKIE CAPTURE FLOW: … inputPeak=… directOutputPeak=… sourceMute=True …`
5. Weit weg: `WALKIE OUTPUT AUS … OUT_OF_RANGE … actual=0`
6. Nah am Boden-Walkie: `WALKIE OUTPUT AN … mode=SIDETONE …`
7. Subjektiv mit Log abgleichen und dokumentieren.

Interpretation:

| Beobachtung | Bedeutung |
|-------------|-----------|
| Walkie OUT_OF_RANGE + trotzdem Echo + `directOutputPeak>0` | Mute greift nicht / Source spielt noch in den Mix |
| Walkie OUT_OF_RANGE + Echo + `directOutputPeak=0` + `sourceMute=True` | Leak außerhalb dieses Source-Pfads |
| Nah Sidetone hörbar, weit still | Zielzustand für Solo-Sidetone |
| Keine Callbacks / kein Sidetone nah | Mute/Virtualisierung hat DSP wieder abgewürgt (Regression wie Phase D) |

---

## 8. Freeze beim Kollegen — Stand

- **Vermutete Hauptursache** (Tap-Rebuild-Storm) ist im Code behoben.
- In aktuellen Solo-Logs keine 6‑s-SELBSTHEILUNG-Schleife.
- **Noch nicht final bestätigt**, weil der entscheidende Test zwei Clients braucht.
- Nebenbeobachtung: Sync beim PTT-Loslassen kann ~1 s dauern — beobachten, ob das mit Freeze korreliert.

---

## 9. Architektur-Skizze (aktuell)

```
Vivox Capture (ein Mic, bereits offen)
        │
        ▼
VivoxCaptureSourceTap + AudioSource (mute=true, volume=1)
        │
        ├─ OnAudioFilterRead (Feed): Downmix → Gate → WalkieRadioBus (__local__)
        │                              └─ data[] = 0
        │
        └─ Unity-Mix: durch Mute (soll) kein hörbarer Direktausgang

WalkieRadioBus
        │
        ▼
jedes WalkieDeviceOutput (3D, EQ, Delay, Distanz-Cutoff)
        │
        ├─ sendendes Gerät: stumm (OWN_DEVICE_TX)
        ├─ Gerät am eigenen Ohr: Sidetone-Guard
        └─ außerhalb MaxHearingDistance: Volume 0
```

---

## 10. Relevante Dateien

| Datei | Rolle |
|-------|--------|
| `Runtime/Walkie/WalkieSidetoneCapture.cs` | Capture-Tap, Mute, Flow-Logs |
| `Runtime/Walkie/WalkieDeviceOutput.cs` | räumlicher Ausgang + Distanz-Logs |
| `Runtime/Walkie/WalkieRadioBus.cs` | Fan-out |
| `Runtime/Walkie/WalkieTalkieRegistry.cs` | lokaler TX-Zustand |
| `Runtime/Walkie/WalkieRadioSync.cs` | Vivox-Kanal-Sync |
| `Runtime/Voice/VoiceRuntime.cs` + `VivoxVoiceBackend.cs` | Tap-Recovery |
| `Runtime/Voice/VoiceSessionLog.cs` | Datei-Logs unter `EarshotLogs/` |
| `docs/DECISIONS.md` | Kurzentscheidungen |
| `docs/PROGRESS.md` | Phase-4-Checkboxen |
| `docs/walkie-talkie-game-integration.md` | Spiel-Integration |

---

## 11. Nächste Schritte (kurz)

1. Spiel auf Package-Revision `capture-tx-follow-v4` aktualisieren, Solo-Hörtest + Log prüfen (Phase-I-Kriterien, Abschnitt 7).
2. Wenn Solo ok: Zwei-Client-Test (Freeze + Radio-Effekt).
3. Wenn `signalBlocks=0` / `inputPeak=0` bleibt trotz `tapChannel='earshot.radio.default'`: Vivox-Capture-Tap-Speisung unter `TransmissionMode.Single` untersuchen (Plan B, Phase I).
4. Erst wenn Hörtest grün: Phase-4-Checkbox „Hörtest“ in `PROGRESS.md` abhaken.

---

*Dokument angelegt 2026-09-18. Bei jedem weiteren gescheiterten oder erfolgreichen Ansatz: hier einen kurzen Abschnitt ergänzen (Datum, Symptom, Hypothese, Fix, Log-Beweis, Ergebnis), nicht nur CHANGELOG-Zeilen.*
