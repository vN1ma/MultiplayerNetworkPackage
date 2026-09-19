# Walkie-Talkie — Debug-Historie (Stand 2026-09-18)

Dieses Dokument hält fest, **welche Symptome** auftraten, **welche Ursachen** vermutet und bestätigt wurden, **welche Fixes** versucht wurden und **warum der Eigenklang bei großer Entfernung trotzdem wiederkehrte**. Es ist Absicht, dass die gescheiterten Ansätze hier bleiben — sonst wiederholt sich dieselbe Schleife.

Repos: `HOTEL_GAME` (Spiel) + `MultiplayerNetworkPackage` / `com.earshot.voice` (Package).  
Aktueller Package-Stand der Diagnose-Revision: `radio-name-dotfree-v5` (Funkkanal-Namen punktfrei, Capture-Tap folgt dem aktiven Sende-Kanal; Commit siehe Git-Historie).

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

### Phase G — Dual-Sperre + Mess-Logs (`cc68dba`)

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

### Phase I — Capture-Tap folgt dem aktiven Sende-Kanal (`capture-tx-follow-v4`, **superseded durch Phase J**)

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

**Ergebnis (Log `20260918-0703`): Fix grundsätzlich richtig, aber Registration auf dem Funkkanal schlug mit `TapId=-1012` (native „invalid argument") fehl** — plus massiver Konsolen-Fehler-Spam, weil das Self-Heal pro Frame neu registrierte. Ursache siehe Phase J.

### Phase J — Funkkanal-Namen punktfrei machen (`radio-name-dotfree-v5`, Namen bleiben in v6)

**Symptom:** Solo-PTT-Test auf `capture-tx-follow-v4`: Pinning auf `earshot.radio.default` lief immer auf `TapId=-1012`, Proximity-Registration funktionierte parallel einwandfrei (`TapId>0`). Unity-Konsole voller „Tap failed to register".

**Ursache (per Vivox-Runtime-Quellcode belegt):** `VivoxServiceInternal.GetChannelUriByName()` kappt bei Namen mit Punkt alles ab dem **letzten** Punkt — ein Workaround für Unity-Environment-GUIDs in `ChannelId.Name`:

```csharp
if (channelNameToLookup.Contains("."))
    channelNameToLookup = channelName.Substring(0, channelName.LastIndexOf("."));
```

`earshot.radio.default` wird dadurch zu `earshot.radio` gekürzt, findet keine ChannelSession, liefert `null` — und der native `RegisterTapForCaptureSource(80000, null)` quittiert mit `-1012`. Der Proximity-Kanalname ist zufällig punktfrei, deshalb funktionierte genau dort das Pinning. Ein Vivox-Bug, der nur Namen mit Punkten trifft.

**Fix:**

1. `WalkieRules.RadioChannelPrefix` von `earshot.radio.` auf **`earshot-radio-`** geändert — der komplette Funkkanal-Name ist damit punktfrei (`earshot-radio-default`), der Lookup funktioniert. Konstante hat jetzt eine Warnkommentar, damit der Punkt nie wieder reinkommt.
2. Self-Heal in `WalkieSidetoneCapture` gedrosselt: Neuregistrierungs-Versuch (Component-Neustart) max. alle 2 s — beendet den Konsolen-Spam, falls eine Registration je wieder scheitert.
3. `WalkieRulesTests` an neue Namen angepasst. Neue Revision: `radio-name-dotfree-v5`.

**Achtung Breaking Change:** Alte Clients nutzen noch `earshot.radio.*`-Kanäle — Clients mit unterschiedlichen Package-Versionen hören sich im Funk **nicht**. Für Tests müssen alle Clients auf `radio-name-dotfree-v5` sein.

**Erfolgskriterium im nächsten Solo-Log:**

- `revision='radio-name-dotfree-v5'`
- bei PTT-Beginn: `WALKIE Sidetone-Tap auf Kanal 'earshot-radio-default' gepinnt: TapId=<positiv>, autoAcquire=False`
- während PTT: `CAPTURE FLOW: tapChannel='earshot-radio-default', autoAcquire=False` mit `callbacks>0`, `signalBlocks>0` und `inputPeak>0` beim Sprechen
- subjektiv: Sidetone während PTT hörbar (Boden-Walkie in Reichweite)

**Restrisiko:** Falls `inputPeak=0` bleibt trotz erfolgreich registriertem Tap auf `earshot-radio-default`, wäre das ein Vivox-Seiteneffekt von `TransmissionMode.Single` — dann Plan B (Sidetone ohne Kanalbezug, Phase I).

### Phase K — Tap pinnt erst nach TX-Bestätigung (`capture-follows-tx-v6`, aktuell)

**Symptom:** Solo-PTT-Test auf `radio-name-dotfree-v5` (Log `20260918-0710`): Pinning auf `earshot-radio-default` jetzt erfolgreich (`TapId>0`; der einzige `-1012` war der Versuch vor dem abgeschlossenen Kanal-Join) — aber weiterhin `signalBlocks=0`, `inputPeak=0` über alle vier PTT-Zyklen. `sourcePlaying=False` ab der zweiten FLOW-Zeile ist eine Folge, keine Ursache: `VivoxAudioProcessor` pausiert die Tap-Source nach 400 ms `NoMoreData` (20 verpasste Reads). Der native Tap liefert also wirklich nichts.

**Ursachen-Analyse (Log `20260918-0710`):**

- Der Pin lief immer **7–14 ms vor** `FUNK sendet` (dem abgeschlossenen `SetChannelTransmissionModeAsync`-Wechsel): Pin `07:10:57.739` → TX-Wechsel `07:10:57.753`. Der Tap wurde also jedes Mal registriert, während TX noch auf Proximity stand.
- Neu-Bewertung des einzigen funktionierenden Logs (`20260918-0551`): Der Funkkanal-Join war dort erst **nach** PTT-Aus fertig (SYNC r5 mit 996 ms Nachlauf, `joinedChannels=1` erst bei r6). TX blieb also die **ganze Zeit auf Proximity**, und der frische Tap war per Auto-Acquire ebenfalls auf Proximity. Tap und TX passten nur deshalb zusammen — es war nie ein Beweis für „Tap auf Funkkanal funktioniert".
- Auch Phase H (`0652`) passt ins Bild: Der Tap lief dort nominell auf Proximity, aber das Auto-Acquire war zum Join-Zeitpunkt noch aktiv — `OnChannelJoined` hat ihn auf den zuletzt gejointen Kanal (Funk) umregistriert, wiederum vor dem TX-Wechsel.
- Folgerung: Der native Capture-Tap (`vxunity_register_for_capture_source`) liefert offenbar nur Audio für den Sende-Kanal, der **bei der Registrierung** aktiv war. Eine Registrierung vor dem TX-Wechsel latcht den alten Kanal und bleibt danach dauerhaft stumm.

**Fix (v6, `WalkieSidetoneCapture.cs`):**

1. `TryPinTapToActiveChannel` pinnt während PTT nur noch auf den Funkkanal, wenn Vivox ihn live in `IVivoxService.TransmittingChannels` meldet (Snapshot max. alle 0,1 s, da die Abfrage alloziert). Bis zur Bestätigung bleibt der Tap unangetastet — i. d. R. auf Proximity — und liefert dadurch **sofort** Sidetone ab Tastendruck, solange TX noch auf Proximity läuft.
2. Neue Log-Zeile beim Warten: `WALKIE Sidetone-Tap wartet auf TX-Bestaetigung fuer 'earshot-radio-default' (Vivox-Sendekanaele: [...])`.
3. `CAPTURE FLOW` zeigt jetzt `txChannels=[...]` und `tapInTx=True/False` — damit ist die Theorie im nächsten Log direkt verifizierbar.

**Erfolgskriterium im nächsten Solo-Log:**

- `revision='capture-follows-tx-v6'`
- bei PTT-Beginn erst `wartet auf TX-Bestaetigung …`, danach `FUNK sendet auf 'default'` und **danach** `WALKIE Sidetone-Tap auf Kanal 'earshot-radio-default' gepinnt: TapId=<positiv>`
- während PTT: `CAPTURE FLOW: tapChannel='earshot-radio-default', txChannels=[earshot-radio-default], tapInTx=True` mit `signalBlocks>0` und `inputPeak>0` beim Sprechen
- subjektiv: Sidetone ab dem ersten PTT (anfangs über den Proximity-Tap, nach dem Wechsel über den Funk-Tap)

**Restrisiko:** Falls `tapInTx=True` und trotzdem `inputPeak=0` bleibt, liefert der native Tap auch nach bestätigtem TX nichts — dann ist die native Capture-Speisung unter `TransmissionMode.Single` endgültig tot und Plan B wird umgesetzt (Sidetone ohne Kanalbezug: lokales Mikrofon separat anzapfen statt über den Vivox-Capture-Tap).

### Phase L — `AudioSource.mute` nullte den Filter-Datenpfad (`capture-unmute-v7`, Mute-Fix bleibt, Kanal-Following superseded durch Phase M)

**Symptom:** Solo-PTT-Test auf `capture-follows-tx-v6` (Log `20260918-0735`): v6 arbeitete exakt wie designed (Warten auf TX-Bestaetigung, Pin **nach** `FUNK sendet`, `tapInTx=True`) — aber weiterhin `signalBlocks=0`, `inputPeak=0.0000` in allen Zyklen.

**Ursachen-Analyse (Log `20260918-0735` + Git-Archäologie):**

- Entscheidendes Beweisfenster `07:35:28.15–31.4`: Tap auf Proximity gepinnt, TX laut `txChannels=[...]` **ebenfalls auf Proximity** (`tapInTx=True`), und `sourcePlaying=True` — d. h. der native Tap lieferte **echte Daten** in den Streaming-Clip (die `VivoxAudioProcessor`-Pause tritt nur nach 400 ms `NoMoreData` ein; die Quelle lief also). Der Feed-Callback feuerte (`callbacks=67`) — und bekam trotzdem **exakt 0.0000**.
- Git-Zeitleiste: `tapSource.mute = true` kam mit `cc68dba` (06:10, „Dual-Sperre", Revision `capture-hardmute-v2`) — **19 Minuten nach** dem einzigen funktionierenden Lauf `20260918-0551` (05:51, Commit `e46d900` 05:50, ohne Mute). Seither war **jeder** Log stumm.
- Mechanismus: `AudioSource.mute` hält den DSP-Graph aktiv (`OnAudioFilterRead` feuert weiter → `callbacks>0`), **nullt aber die Samples**, die der Filter-Kette übergeben werden. Der Vivox-`VivoxAudioProcessor` schreibt die nativen Daten unabhängig davon in den Clip — deshalb loggt Vivox-sided alles „gesund", während unser Feed nur Nullen sieht. `directOutputPeak=0` (GetOutputData auf gemuteter Source) war ein weiteres, ignoriertes Symptom desselben Mutes.
- Folgerung: Die Kanal-Latch-Theorie (v3–v6) war eine Fehldeutung — das beobachtete „native liefert nichts" in diesem Fenster war falsch, es lieferte sehr wohl; nur der Unity-seitige Abgriff war genullt. (`sourcePlaying=False` in späteren FLOW-Zeilen auf dem Funkkanal bleibt als echte offene Frage, siehe Restrisiko.)

**Fix (v7, `WalkieSidetoneCapture.cs`):**

1. `tapSource.mute = false` bei Erstellung — die Direktausgabe bleibt weiterhin stumm, weil der Feed den Puffer am Ende von `OnAudioFilterRead` nullt (das `e46d900`-Design, das im einzigen guten Lauf 0551 funktionierte).
2. `EnforceDirectOutputMute` → `EnforceDirectOutputUnmuted`: sicherheitshalber erzwingen, dass die Source **nicht** gemutet ist (falls irgendetwas sie erneut mutet, wird entsperrt + Alert geloggt).
3. Revision `capture-unmute-v7`; v6-TX-Bestätigungs-Pinning bleibt unverändert (sinnvolle Hygiene, reduziert Registrierungs-Churn).

**Erfolgskriterium im nächsten Solo-Log:**

- `revision='capture-unmute-v7'`, `hardMute=False` in der Erstellungs-Zeile
- in der Warte-Phase (Tap+TX auf Proximity): `sourcePlaying=True`, `callbacks>0` und jetzt `signalBlocks>0`, `inputPeak>0` beim Sprechen → **Sidetone ab dem ersten PTT hörbar**
- nach `FUNK sendet` + Pin auf `earshot-radio-default`: prüfen, ob `inputPeak>0` bleibt (Funkkanal-Fall)

**Restrisiko:** Falls auf dem Funkkanal (`tapInTx=True`) `sourcePlaying=False` und `inputPeak=0` bleibt **während gesprochen wird**, liefert der native Tap für den Funkkanal wirklich keine Daten — dann Plan B: während Funk-TX den Tap auf Proximity pinnen (Mikro-Audio ist kanalunabhängig) oder lokales Mikrofon-Loopback.

---

### Phase M — Funkkanal-Taps liefern nie Daten; Tap bleibt permanent auf Proximity (`proximity-pin-v9`, aktuell)

**Symptom:** v7-Testläufe (Logs `20260918-093346` und `20260918-094023`, beide `capture-unmute-v7`): `sourceMute=False`, aber `inputPeak=0.0000` in allen FLOW-Zeilen. Nutzer hörte beim ersten Reinsprechen einen ~100-ms-Sidetone-Blitz, danach Stille.

**Ursachen-Analyse (beide v7-Logs):**

- `signalBlocks=5` unmittelbar nach **jedem** Funk-Pin (09:33:55, 09:34:11, 09:40:43) — der Feed sah sehr wohl echte Daten, aber nur ~100 ms lang. Das ist der Restpuffer der vorherigen **Proximity**-Registrierung, den die Source nach dem Umpinnen abspielt, bevor `VivoxAudioProcessor` nach 20× `NoMoreData` pausiert. Genau dieser Restpuffer war der hörbare Blitz.
- Auf dem Funkkanal (`earshot-radio-*`) gepinnte Taps: in **jeder** FLOW-Zeile nach den ersten ~100 ms `sourcePlaying=False` (NoMoreData-Pause) und `signalBlocks=0` — der native Tap liefert für unsere selbstgebauten Funkkanäle **nie** Daten (vermutlich adressiert das Channel-URI kein gültiges Capture-Session-Objekt).
- Auf dem echten Proximity-Kanal gepinnte Taps: `sourcePlaying=True`, `directOutputPeak` bis 0.068557 (09:40:41.370) — native Daten fließen. Auch der einzige gute Lauf 0551 hatte Tap UND TX auf Proximity.
- Nebenerkenntnis: `inputPeak` war ein Momentanwert (letzter Buffer) und zeigte deshalb trotz `signalBlocks=5` immer 0.0000 — Diagnose-Messwert war irreführend.
- Git-Befund: Der laut Notiz existierende v8-Commit (`pin-on-ptt-v8`) fehlt im Repo (HEAD = `fa94709`/v7); alle Läufe ab 09:33 liefen auf v7. Der v8-Ansatz (sofortiger Funk-Pin bei PTT) wäre ohnehin der falsche Weg gewesen, da Funkkanal-Taps nachweislich tot sind.

**Fix (v9, `WalkieSidetoneCapture.cs`):**

1. `TryPinTapToActiveChannel` pinnt **permanent** auf den Proximity-Kanal — Funkkanal-Pinning komplett entfernt, inkl. v6-TX-Bestätigungs-Gate, `IsTransmittingOn`, `reportedWaitingForTxConfirm` und `WalkieRules.ToVivoxRadioChannel`-Nutzung.
2. Kein Re-Pin bei PTT → keine Neu-Registrierung → Latenzpuffer bleibt über PTT-Wechsel erhalten, Sidetone startet sofort.
3. `inputPeak` ist jetzt Fenster-Maximum seit dem letzten FLOW-Log (statt Momentanwert); FLOW-Intervall 2 s → 1 s.
4. Revision `proximity-pin-v9`.

**Erfolgskriterium im nächsten Solo-Log:**

- `revision='proximity-pin-v9'`, genau **ein** Pin auf den Proximity-Kanal nach Verbindung, kein weiterer Pin bei PTT
- Beim Sprechen mit Funk-PTT: `tapChannel=<Proximity-URI>`, `tapInTx=False`, `sourcePlaying=True`, `signalBlocks>0`, `inputPeak>0` → durchgehender Sidetone
- Direktausgabe bleibt stumm (Feed-Nullung), Sidetone nur über `WalkieDeviceOutput`

**Restrisiko:** Falls der Proximity-Tap während Funk-TX **keine** Daten mehr liefert (Capture folgt dem TX-Kanal), bleibt nur Plan C: lokales Mikrofon-Loopback statt Vivox-Capture-Tap — der Funkkanal-Tap ist als Alternative endgültig ausgeschlossen.

---


## 6. Warum es sich anfühlt, als kämen wir nicht weiter

1. **Zwei unabhängige Bugs** (Freeze/Tap-Recovery vs. lokaler Capture-Leak) wurden oft als ein Symptom behandelt.
2. **Sidetone ist Feature und Bug zugleich** — räumlich gewollt, global verboten. Fixes am Falloff wirken nur auf den Feature-Pfad.
3. **Unity/Vivox-Tap-Pipeline** speist zuerst einen Clip und spielt eine Source; Filter-Clear ist nötig, aber nicht immer hinreichend.
4. **Volume-0 vs. Mute vs. Minimalpegel** erzeugen jeweils andere Symptome (tot vs. leise vs. leckend) — ohne getrennte Messwerte wirkt jeder Fix zufällig.
5. **Frühere Logs** waren gut für PTT/Distanz, schlecht für „hört der User trotz Walkie-AUS noch etwas?“. Deshalb wirkten Logs „nutzlos“, obwohl sie den räumlichen Pfad längst freisprachen.

---

## 7. Was die Logs jetzt beweisen sollen (Checkliste)

Nach Package-Update im Spiel (`capture-follows-tx-v6`) und einem Solo-PTT-Test:

1. Datei unter `HOTEL_GAME/EarshotLogs/voice-*.txt` öffnen.
2. Einmalig: `AUDIO DEVICES: …`
3. Nach Verbindung: `WALKIE Vivox-Capture-Tap erstellt … revision='proximity-pin-v9'` und genau ein `WALKIE Sidetone-Tap auf Kanal '<Proximity-URI>' gepinnt … autoAcquire=False` (kein weiterer Pin bei PTT)
4. Periodisch während PTT: `WALKIE CAPTURE FLOW: … inputPeak=… directOutputPeak=… sourceMute=False …`
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
VivoxCaptureSourceTap + AudioSource (mute=false seit v7, volume=1,
persistent auf Proximity-Kanal gepinnt seit v9)
        │
        ├─ OnAudioFilterRead (Feed): Downmix → Gate → WalkieRadioBus (__local__)
        │                              └─ data[] = 0
        │
        └─ Unity-Mix: durch Feed-Nullung kein hörbarer Direktausgang

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

1. Spiel auf Package-Revision `proximity-pin-v9` aktualisieren (alle Clients!, Unity wegen Package-Cache neu starten), Solo-Hörtest + Log prüfen (Phase-M-Kriterien, Abschnitt 5).
2. Wenn Solo ok: Zwei-Client-Test (Freeze + Radio-Effekt).
3. Wenn trotz `tapChannel=<Proximity>` und Sprechen `signalBlocks=0`/`inputPeak=0` bleibt (Capture folgt TX-Kanal): Plan C umsetzen — lokales Mikrofon-Loopback statt Vivox-Capture-Tap (Phase M). Funkkanal-Taps sind endgültig out.
4. Erst wenn Hörtest grün: Phase-4-Checkbox „Hörtest“ in `PROGRESS.md` abhaken.

---

## 12. v10 — Leak-Jagd: „Stimme überall gleich laut, kein 3D“ (2026-09-18, ~10:30)

**Neue Fakten (ändern die Ursachen-Bewertung):**

1. Nutzer-Klarstellung: Das Symptom bestand schon **vor** jedem Sidetone-/Capture-Tap-Code (Session-Start mit der vorherigen KI, gegen 4–6 Uhr). Die Arbeit seit damals hat zum selben Hörbild zurückgeführt — damit scheidet der VivoxCaptureSourceTap als alleinige Ursache weitgehend aus; der Verdacht verschiebt sich auf einen Pfad, der von Anfang an existierte.
2. Solo-Log `voice-20260918-101704`: **keine PEGEL-Zeilen** → kein zweiter Teilnehmer/Client im Vivox-Kanal (Phantom-Client ausgeschlossen).
3. `AUDIO DEVICES` desselben Logs: `activeOutput='Lautsprecher (VB-Audio Virtual Cable)'` — das ist das **Vivox-Ausgabegerät**, und `CABLE Output (VB-Audio Virtual Cable)` taucht zusätzlich als Input-Gerät auf → VB-Cable-Loopback-Konstellation am Host. Ungeklärt: wer `CABLE Output` abhört und warum Vivox-Output auf VB-Cable steht. Unitys eigene AudioSources (Tap, Walkie-Ausgänge) spielen dagegen auf dem Windows-Standard-Ausgabegerät.
4. Code-Verifikation Vivox-Package: `VivoxAudioProcessor` nutzt `GetComponent<AudioSource>()` auf dem Tap-GameObject — es gibt **keine versteckte zweite AudioSource**. Der einzige 2D-Mikro-Pfad in Unity bleibt der Sidetone-Tap selbst.
5. `directOutputPeak` misst vermutlich **vor** dem Filter (GetOutputData liefert die Source-Samples, nicht das gefilterte Ergebnis) — der Wert war daher nie ein Beweis für eine hörbare Direktausgabe.

**v10-Diagnose-Revision `leak-hunt-v10` (`WalkieSidetoneCapture`):**

- **F9-Killswitch**: Hart-Mute der Tap-AudioSource. Mute nullt nachweislich die OnAudioFilterRead-Samples (Beweis 20260918-0735) → der Tap ist danach garantiert stumm, auch die Sidetone-Daten stoppen. `EnforceDirectOutputUnmuted` respektiert den Diagnose-Zustand.
- **`WALKIE AUDIO-INVENTAR`**: alle 2 s während PTT werden alle spielenden AudioSources der Szene geloggt (Name, Clip, spatialBlend, Volume, Mute, Position) — deckt jede versteckte 2D-Quelle auf.
- **`WALKIE LEAK-VERDACHT`**: Alert, wenn eine Nicht-Tap-Source mit `spatialBlend < 0.5` ungemutet spielt.

**Solo-Testprotokoll v10:**

1. PTT + sprechen, weit weg vom Boden-Walkie → Symptom bestätigen.
2. Während die eigene Stimme zu hören ist: **F9 drücken** und weitersprechen.
3. Zusätzlich ohne Build prüfbar: (a) Spiel geschlossen und normal ins Mikro sprechen — hört man sich im Kopfhörer? (Mikro-Monitoring am Client/Headset/Parsec wäre dann die Ursache, kein Spiel-Bug.) (b) Am Host klären, wer `CABLE Output` (VB-Cable) aufnimmt und warum Vivox auf VB-Cable ausgibt.

**Interpretation:**

| Beobachtung | Bedeutung |
|-------------|-----------|
| F9 → Stimme verschwindet | Leak war die Tap-Direktausgabe (trotz Feed-Nullung) → Fix: Mixer-Routing statt Nullung |
| F9 → Stimme bleibt, `LEAK-VERDACHT`-Zeile | die benannte 2D-Source ist der Leak |
| F9 → Stimme bleibt, kein Verdacht im Inventar | Leak außerhalb Unity (Vivox-nativ / OS / VB-Cable / Mikro-Monitoring) |

---

*Abschnitt 11 („Nächste Schritte“) ist durch diesen Abschnitt ersetzt: nächster Schritt ist der v10-Solo-Test mit F9.*

---

## 13. v11 — Ursache bewiesen: Tap-Direktausgabe trotz Feed-Nullung (2026-09-18, ~12:00)

**v10-Solo-Test-Ergebnis (Log `voice-20260918-105955`):**

- Symptom reproduziert: eigene Stimme ueberall gleich laut. **F9 → komplett stumm.**
- Beweiskette (dreistufig):
  1. Beim F9-Druck (11:00:24) war der Spieler >30 m von allen Walkies entfernt: Boden-Walkie `OUT_OF_RANGE` (falloff=0.000 seit 11:00:13), Hand-Walkie durch den `MinSidetoneSelfDistance`-Guard durchgehend stumm (vol=0.00). Die Stimme kam also **nicht** aus den Walkie-Lautsprechern — die räumliche Sidetone funktioniert korrekt (8 m Reichweite, quadratischer Falloff: 2.66 m → falloff 0.445, 7.68 m → 0.002, `OUT_OF_RANGE` ab 8 m).
  2. F9 mutet nur die Tap-AudioSource → deren direkte 2D-Wiedergabe von `StreamClip1` (spatial=0.00, vol=1.00, ungemutet) war der hörbare Leak.
  3. Die Feed-Nullung greift im hörbaren Pfad nicht: dasselbe Log zeigt `signalBlocks=37` (Feed bekam Mikro-Daten und nullte sie) bei gleichzeitig hörbarem Output. `directOutputPeak=0` ist damit endgültig als untauglicher Messwert entlarvt.
- Ursachen-Modell: `VivoxAudioTap` nutzt **kein** OnAudioFilterRead — eine Coroutine (20 ms Takt) schreibt Mikro-Daten per `SetData` in einen 3-s-Ring-Buffer-Clip und `Play()`t ihn. Zur Laufzeit **nach** `Play()` hinzugefügte OnAudioFilterRead-Filter werden von Unity u. U. erst mit einem Neustart der Quelle in die hörbare DSP-Kette verkabelt. Mute wirkt auf einem anderen Stage (nachweislich sogar vor dem Filter) → F9 wirkte, die Nullung nicht.
- 0735-Evidence neu bewertet und bestätigt: `mute=True` am **lebenden** Proximity-Kanal (TapId 1894): `callbacks=67, signalBlocks=0` → Mute nullt die Filter-Daten → dauerhaftes Muten wäre kein Fix (killt die Sidetone).
- VB-Cable/OS-Loopback als Ursache: **widerlegt** (F9 ist ein rein Unity-seitiger Killswitch). Nutzer-Check: kein Selbsthören bei geschlossenem Spiel.

**v11-Fix + Diagnose (`WalkieSidetoneCapture`, Revision `leak-hunt-v11`):**

1. **Feed-vor-Tap-Anlage**: `WalkieVivoxCaptureFeed` wird jetzt vor `VivoxCaptureSourceTap` hinzugefügt — der Filter existiert damit vor jedem `Play()`.
2. **Rewire-Zyklen**: Stop+Play der Tap-Source bei +1 s / +3 s / +7 s nach Erstellung erzwingt die Neuverkabelung des Filters in die hörbare Kette (Log: „WALKIE Tap-Source-Neustart (v11)“).
3. **`outPeak` im AUDIO-INVENTAR**: GetOutputData-Pegel pro spielender Quelle — beweist künftig, welche Quelle wirklich Signal in den Mix gibt.
4. **F10**: Tap-Volume 0/1 — testet, ob Volume (anders als mute) die Filter-Daten überleben lässt. `signalBlocks>0` bei `sourceVolume=0.000` wäre der Beweis, dass volume=0 ein valider Dauer-Fix ist.
5. **F11**: Sidetone-Datenfluss-Abschaltung — bleibt die Stimme hörbar, kommt sie garantiert nicht aus den Walkie-Lautsprechern.

**Testprotokoll v11:**

1. Unity neu starten (Package-Cache!), Log muss `revision='leak-hunt-v11'` zeigen.
2. Solo: PTT + sprechen, weit weg vom Walkie:
   - Stimme weg (und in Walkie-Nähe räumlicher Sidetone hörbar) → **FIX OK**, Rewire war die Lösung.
   - Stimme immer noch überall → **F10** drücken und weitersprechen: Stimme weg → Tap-Direktausgabe bestätigt; FLOW-Log prüfen (`signalBlocks>0` bei `sourceVolume=0.000` → volume=0 wird der Dauer-Fix in v12). Stimme bleibt → nicht die Tap-Source → `outPeak` im INVENTAR zeigt die echte Quelle.
   - Zur Sicherheit **F11**: bleibt die Stimme hörbar, ist sie garantiert nicht aus den Walkies.
3. Wenn weder Rewire noch volume=0 funktionieren (Rewire wirkt nicht UND volume nullt die Filter-Daten): v12-Optionen = stilles AudioMixer-Routing der Tap-Source (Mixer-Asset nötig) oder Reflektions-Lesen des Vivox-Ring-Buffers bei dauerhaft gemuteter Quelle.

---

## 14. v12 — Fix des Sidetone-Leaks: volume=0-Dauerstellung + Clip-Lese-Pfad statt OnAudioFilterRead (2026-09-18, ~13:30)

**v11-Test-Ergebnis (Log `voice-20260918-131804-852-pid22360`):**

- Unity-Neustart war unnötig: Package-Manager-Update reichte (Re-Resolve + Domain-Reload). Log zeigte `revision='leak-hunt-v11'`, packages-lock hash `f847aa2`.
- Normalzustand: Stimme weiterhin überall gleich laut → **Rewire (Stop+Play) hat den Filter NICHT in die hörbare Kette verkabelt.** v11-Theorie widerlegt.
- AUDIO-INVENTAR: Nur der [SIDETONE-TAP] hatte während des Sprechens `outPeak>0` (0.071–0.159); alle anderen Quellen (Ocean, Musik, WalkieOutput) 0.000 → Leak endgültig = Tap-Direktausgabe, jetzt auch per outPeak bewiesen.
- **F10 (volume=0): Stimme komplett weg** → volume stummt die Direktausgabe. ABER: über das gesamte F10-Fenster `inputPeak=0.0000` und `signalBlocks=0` → **Unity nullt bei volume=0 die OnAudioFilterRead-Samples genauso wie bei mute.** volume=0 allein hätte also auch die Sidetone-Daten getötet.
- F11 war nicht diskriminierend: Es schaltete den ganzen Feed stumm („Sidetone aus", callbacks-Reset 47→14) statt nur den Bus-Zufluss — Design-Schwäche des v11-Tests.
- SDK-Code-Lektüre (`VivoxAudioTap.cs`/`VivoxAudioProcessor.cs`, com.unity.services.vivox 16.10.0) liefert den Schlüssel: Die Coroutine `ProcessAudio` (20-ms-Takt) zieht Mikro-Daten per **P/Invoke `DoAudioFilterRead` direkt aus Vivox-Native** in `m_internalBuffer` und schreibt sie per `SetData` in den 3-s-Ring-Buffer-Clip. Die AudioSource ist nur der Abspielmechanismus — **volume/mute betreffen nur Unitys Wiedergabe und die Filter-Samples, nicht den nativen Pull und nicht den Clip-Inhalt.**

**v12-Fix (`WalkieSidetoneCapture.cs`, Revision `leak-hunt-v12`):**

1. **`tapSource.volume = 0f` dauerhaft** bei Erstellung (F10-Beweis). `EnforceSilentDirectOutput()` stellt das jeden Frame sicher.
2. **`WalkieVivoxCaptureFeed` ohne OnAudioFilterRead**: Main-Thread-Pull pro `Update()` liest per Reflektion `m_writePointer`/`m_streamClip` aus dem `VivoxAudioProcessor` und holt die frisch geschriebenen Frames per `clip.GetData` aus dem Ring-Buffer (read-only, keine Doppel-Pulls am nativen Tap). Feste 10-ms-Quanten (stabile Buffer-Allokation), Wrap/Seam-Handling, Resync bei Sprüngen > Clip/4 (Re-Init/Underrun-Bump), Cursor-Mitlauf bei inaktivem Sidetone.
3. Gate/Downmix/Bus-Write unverändert; FLOW-Log zeigt jetzt `pulls`/`pulledFrames`/`clipPeak`.
4. **F9** bleibt Hard-Mute-Killswitch — mit Nebeneffekt: Sidetone läuft jetzt sogar bei mute weiter (Clip-Pfad ist davon nicht betroffen).
5. **F10** ist jetzt die Gegenprobe: LEGACY-LEAK-MODE volume=1 — die Stimme MUSS dann wieder überall gleich laut kommen.
6. Entfallen: Rewire-Zyklen, Feed-vor-Tap-Anlage, `EnsureCapacity`.

**Testprotokoll v12:**

1. Earshot im Package-Manager aktualisieren (Neustart nicht nötig), Log muss `revision='leak-hunt-v12'` und `tapVolume=0.00` zeigen.
2. Solo, PTT + sprechen, weit weg vom Walkie: Stimme muss **weg** sein; in Walkie-Nähe räumlicher Sidetone hörbar. FLOW: `pulls>0`, `pulledFrames` ~48000/s, `clipPeak>0` beim Sprechen.
3. Gegenprobe F10: Stimme kehrt überall gleich laut zurück → beweist rückwirkend den Leak-Pfad. F10 wieder ausschalten.
4. Danach: Two-Client-Test (Freeze + Radio) und Phase-4-„Hörtest"-Checkbox in `PROGRESS.md`.

**Risiken/Annahmen:** Reflektion ist an com.unity.services.vivox 16.10.0 gebunden (Feldnamen `m_AudioProcessor`/`m_writePointer`/`m_streamClip`); bei einem Vivox-Upgrade bricht der Pfad mit einem einmaligen Alert („Reflektions-Zugriff ... fehlgeschlagen"), nicht still. `GetData` auf dem per `AudioClip.Create(..., stream:false)` erzeugten Clip ist erlaubt (das SDK nutzt auf demselben Clip selbst `SetData`).

---

## 15. v12 verifiziert + v13: Tap-Leak behoben — Reststimme kommt nachweislich NICHT aus dem Unity-Client (2026-09-18, ~14:30)

**v12-Test-Ergebnis (Log `voice-20260918-140434-505-pid22360`):**

- `revision='leak-hunt-v12'`, `tapVolume=0,00` ab Start — v12 lief wie geplant.
- **Tap-Direktausgabe stumm:** [SIDETONE-TAP] `outPeak=0,000` über die gesamte Session. F10 (LEGACY-LEAK-MODE, `vol=1,00`) brachte die Roh-Stimme zurück (`outPeak=0,08–0,30`) → **der 2D-Leak ist per Gegenprobe endgültig behoben.**
- **Clip-Lese-Pfad funktioniert:** `pulls≈100/s`, `pulledFrames≈48000/s`, `clipPeak` bis 0,98 beim Sprechen, `signalBlocks` 30–68, kein `ReportPullBroken`.
- **Räumliche Sidetone korrekt:** Alle 8 `OUTPUT AN`-Zeilen mit sauberem Falloff (2,45 m → 0,482; 7,7 m → 0,001; darüber `OUT_OF_RANGE`). Um 14:06 war der Spieler 875 m weg — alle Walkies stumm (`target=0,000`).

**Neues Symptom (Nutzerbericht):** Trotzdem weiterhin eine Stimme **mit** Walkie-Effekt, überall gleich laut; mit F10 zusätzlich eine **lautere Roh-Stimme ohne** Effekt — beide untereinander unterschiedlich laut, einzeln aber entfernungsunabhängig.

**Analyse (vollständige Log-Auswertung + SDK-/Code-Lektüre):** Die Effekt-Stimme kann von diesem Client nicht stammen:

- Der Walkie-Effekt (HP/LP/Crunch/0,2-s-Delay) existiert **ausschließlich** in `WalkieDeviceOutput` — und die waren außerhalb von 8 m um alle Walkies nachweislich stumm.
- Keine Unity-AudioSource mit Signal: `outPeak≈0` überall (wichtig: WalkieOutput injiziert per OnAudioFilterRead in einen stillen Clip — GetOutputData misst VOR dem Filter und zeigt deshalb 0,000; verlässlich ist das `vol=`-Feld im INVENTAR, das außerhalb der Walkie-Nähe 0,00 zeigte).
- Kein Remote-Teilnehmer im Proximity-Roster (0 × `SPIELER da`/PEGEL), kein `Microphone.Start`/CABLE-Loopback in Game oder Package, VoiceTestSpeaker inaktiv.
- SDK-Fakten: `VivoxCaptureSourceTap` liefert nur Mikro-Daten — die **Sendung läuft nativ und ist durch volume=0 NICHT gekillt** (wichtig für den Two-Client-Test). `RegisterTapForParticipantAudio(..., silenceInFinalMix)` zeigt: Vivox spielt Empfangenes **zusätzlich nativ** an `activeOutput` — der liegt hier auf einem VB-Audio-Kabel.

**Hauptverdacht (unbewiesen):** Ein stiller **zweiter Client im Funkkanal** (`earshot-radio-default`) — z. B. zweiter Editor, ein Build oder das Spiel auf einem anderen Gerät. Er redet nicht → im Log unsichtbar (kein Proximity-Roster-Eintrag, keine Reden-/PEGEL-Statistik), **spielt aber die eigene Sendung an SEINEN Walkies mit Walkie-Effekt ab** — für diesen Spieler überall gleich laut, weil die Lautstärke nur von SEINEM Standort abhängt. Hörbar via Parsec (Host-Ton). Alternativ: externes Monitoring des VB-Cables (OBS/Audacity/Windows-„Abhören dieses Geräts“).

**v13-Diagnose (kein Verhaltens-Change):**

1. `WalkieRadioSync` loggt bei jedem Sync die Funkkanal-Teilnehmer: `WALKIE RADIO KANAL '<id>': N Teilnehmer [ICH, ...]` — Alert bei >1 Teilnehmer (schliesst das Log-Blindfeld).
2. Neu: `IVoiceRadioBackend.CopyRadioChannelParticipantIds` (implementiert in `VivoxVoiceBackend` über `VivoxService.Instance.ActiveChannels`, self-tolerant, try/catch-gesichert).
3. `AUDIO DEVICES` warnt jetzt, wenn `activeOutput` ein virtuelles Kabel ist (VB-Audio/CABLE), mit Hinweis auf den Lautstärkemixer-Check.
4. Revision `leak-hunt-v13`.

**Testprotokoll v13:**

1. Package-Manager-Update; Log muss `revision='leak-hunt-v13'` zeigen.
2. Vorher auf dem Host schließen: zweite Unity-Instanzen, Builds, OBS/Audacity.
3. **Windows-Lautstärkemixer bei gehaltener Sendetaste** beobachten: Schlägt nur „Unity Editor“ aus → Quelle ist im Client (weitergraben); schlägt eine andere App aus → externe Quelle gefunden.
4. PTT drücken und Log-Zeile `WALKIE RADIO KANAL 'default': ...` prüfen: „2 Teilnehmer“ + Alert → der zweite Client war die Quelle der konstanten Walkie-Stimme.

**v13-Zwischenbefund — sndvol-Analyse des Nutzers (Host, Default = VB-Cable):**

- Während des Spiels schlagen genau 3 Sessions aus: „Lautsprecher“ (Device-Master), „Parsec mini frame“ (Parsec-Capture), „Hotel Game“ (Unity) — **keine versteckte vierte App** → externe Quelle (OBS/Audacity/Steam) endgültig ausgeschlossen (passend zum Prozess-Scan: 1× Unity-Editor PID 22360, kein Build, kein OBS, Steam läuft nicht).
- Chrome-Tests zeigen: Apps bleiben auf dem Gerät, auf dem sie gestartet wurden; Parsec capturet das Default-Gerät; Wechsel auf „Digitale Ausgabe“ = Stille, weil nichts dorthin spielt.
- **Konsequenz:** Alles über Parsec Gehörte ist der Mix auf dem VB-Cable, und genau EIN Prozess speist ihn: Unity. ABER: Vivox' native Wiedergabe läuft im selben Prozess und erscheint im Mixer ebenfalls als „Hotel Game“ — sndvol kann Unity-Mix und Vivox-Empfang NICHT trennen. (Peak-Meter sind zudem logarithmisch skaliert — räumliches Falloff ist im Meter nicht sichtbar, „gleicher Ausschlag“ ist kein Beweis für konstante Lautstärke.)

**v14-Zusatz: F8 = Vivox-Ausgabe chirurgisch umleiten.**

`HandleDiagnosticHotkeys` (WalkieSidetoneCapture, Revision `leak-hunt-v14`): F8 leitet Vivox' Ausgabegerät auf das erste physische Gerät (kein VB-Audio/CABLE/Steam/Unusable) um, Unitys eigener Ton bleibt unberührt; nochmal F8 stellt zurück. Diskriminiert die letzten beiden Kandidaten für die „konstante Walkie-Stimme“: (a) Unity-Mix → F8 ändert nichts; (b) Vivox-native Wiedergabe (Teilnehmer ohne Tap, Echo-Loop) → F8 entfernt die Stimme sofort. Zusammen mit der v13-Teilnehmerzeile (`WALKIE RADIO KANAL ...`) ist damit jeder Fall eindeutig entscheidbar.

**v14-Testergebnis (Log 20260918-222244, Revision leak-hunt-v14):**

- Funkkanal: `WALKIE RADIO KANAL 'default': 1 Teilnehmer [ICH]` — **kein zweiter Client**, der v13-Hauptverdacht ist damit begraben.
- F8 lief korrekt (Vivox → „Digitale Ausgabe“), die Stimme blieb hörbar → Vivox-native-Wiedergabe ausgeschlossen (war bei 1 Teilnehmer ohnehin unmöglich).
- Nutzer sprach durchgehend 22:23:17–22:24:13; Walkies nur bis 22:23:41 AN (2,1–2,5 m, target ≈0,17), ab 22:23:44 (>8,3 m) alle stumm. Im 22:03-Log zusätzlich: **PTT + Sprechen bei 95 m** (22:04:40–44, clipPeak 0,20–0,59), alle Walkies `OUT_OF_RANGE, falloff=0,000`.
- Nutzer-Checks: kein „Gerät abhören“ auf dem Laptop, keine Stimme ohne PTT (auch in-game nicht) → Laptop-Monitoring ausgeschlossen. `LifeMemoryAudioTap` (Game-Repo, AudioListener-Filter) geprüft: rein lesend (Ringpuffer für Todes-Flashback), kein Ausgabepfad.

**v15-Diagnose — Master-Mix-Sonde + F12 (kein Verhaltens-Change):**

Bleibt als letzte Messlücke: `GetOutputData` an AudioSources misst VOR `OnAudioFilterRead` — injizierte Samples (WalkieOutput, Tap) sind im AUDIO-INVENTAR prinzipbedingt unsichtbar. Ob Unity überhaupt Sprach-Samples Richtung Ausgabegerät schickt, war bisher nicht messbar.

1. `masterPeak`/`masterRms` in den FLOW-Zeilen via `AudioListener.GetOutputData` — misst den ENDTLICHEN Mix am Listener, nach allen Filtern und Injektionen.
2. **F12: Unity-GESAMTAUSGABE stumm** (`AudioListener.volume=0`, Toggle mit Restore des Vorher-Werts) — die Gegenprobe zu F8: Bleibt die Stimme bei gehaltener Sendetaste trotz F12 hörbar, kommt sie garantiert NICHT aus dem Unity-Prozess.

**Testprotokoll v15:**

1. Log muss `revision='leak-hunt-v15'` zeigen.
2. >20 m von allen Walkies entfernen, Sendetaste halten: erst 5 s still (masterPeak-Basis = Ambience), dann normal sprechen. Steigt `masterPeak` deutlich über die Basis → Unity gibt doch Sprach-Samples aus (weitergraben). Bleibt er gleich → Unity gibt nichts her.
3. Gleiche Position, F12 drücken: Kompletter Spielsound verstummt. Stimme trotzdem hörbar → Quelle ist außerhalb von Unity (dann bleibt nur noch die Kette Kabel→Parsec→Laptop oder die Wahrnehmung).

**v15-Testergebnis (Log 20260918-225826, Revision leak-hunt-v15): ENDGÜLTIG KEIN UNITY-PFAD.**

- F12 um 22:59:39 gedrückt (AudioListener-Master 1,000→0); danach `masterPeak`/`masterRms` durchweg **0,000000** — bei gleichzeitig aktivem Sprechen (signalBlocks bis 91/s, clipPeak in der Session bis 0,98). Der Nutzer hat sich während F12 **trotzdem selbst gehört** (auch ohne F12 unverändert).
- Da `masterPeak` den Mix NACH allen Filtern und Injektionen misst, ist damit jeder Unity-Pfad ausgeschlossen: nicht WalkieOutput, nicht der Tap, nicht irgendeine injizierende Quelle, nicht Vivox-nach-Unity. Funkkanal nach wie vor 1 Teilnehmer [ICH].
- **Timing-Beobachtung des Nutzers (neuer Schlüsselbefund):** Beim ersten Reinsprechen nach einigen Sekunden Wartezeit dauerte es einige Sekunden bis zur Selbsthörung; danach kam die Stimme bei jedem Reinsprechen schnell, ohne Wartezeit. Das ist das klassische Verhalten eines **energiegesteuerten Gates mit Hangover (VAD)** — nicht das einer PTT-gekoppelten Leitung. Die PTT-Korrelation ist damit vermutlich Scheinkorrelation: Beim Funkgerät-Sprechen wird lauter/klarer gesprochen, was das Gate öffnet; normales Sprechen ohne PTT bleibt unter der Schwelle.
- **Neuer Hauptverdacht: Host-OS-Loop via „Dieses Gerät abhören“.** Auf dem HOST könnte für „Mikrofon (Parsec Virtual Audio)“ in mmsys.cpl das Abhören aktiviert sein → Wiedergabe auf das Standardgerät (VB-Audio-Kabel) → Parsec capture → Laptop. Wichtig: Das Listen-Routing erscheint **nicht** als App-Session im Lautstärkemixer — der frühere sndvol-Check (3 Sessions) schließt es NICHT aus. Der Nutzer hatte mmsys nur am LAPTOP geprüft, nie am Host. Die blecherne „Funk“-Qualität passt zu Parsecs komprimiertem Mikrofon-Codec, die überall-gleiche Lautstärke zur reinen OS-Ebene (kein Falloff). Das VAD-artige Timing passt zu Parsec-Clients, die Mikrofon erst bei Sprachenergie übertragen.
- **Nächste Tests:** (1) HOST: `mmsys.cpl` → Aufnahme → „Mikrofon (Parsec Virtual Audio)“ → Eigenschaften → Tab „Anhören“ → falls „Dieses Gerät abhören“ angehakt: Häkchen entfernen, dann Gegenprobe >10 m + PTT + Sprechen. (2) Falls nicht gesetzt: LAUT ohne PTT sprechen (entscheidet PTT-vs-Lautstärke) und bei hörbarer Stimme den HOST-Lautstärkemixer beobachten (welche Session schlägt aus? keine = System-Routing).

**v16-Meta-Review (2026-09-19): In-Prozess-Jagd eingestellt — Leit-Verdacht kippt auf die Parsec/RDP-Audio-Kette. Status: remote testebar via T1/T2 (siehe unten), finales Protokoll BLOCKIERT bis physischer Host-Zugriff (Host steht in DE, Nutzer ist remote).**

**v16-Bestandteile (Revision `leak-hunt-v16`, gebaut am 18.09., NICHT gegen die neue Leit-Hypothese getestet):** `VIVOX-RX`-Report alle 2 s unabhängig von PTT (funkRx/proxRx = was die Engine tatsächlich auf Funk-/Proximity-Kanal empfangen würde; Zustand der nativen Ausgabe; masterPeak/masterRms; Teilnehmer-Listen von Proximity- UND Funkkanal), F7 = Vivox-native Ausgabe an/aus (Gegenprobe), optionale Auto-Stummschaltung der nativen Ausgabe nach Login. Letztere ist nach dem Meta-Review **default AUS** (`VivoxVoiceBackend.DiagnosticMuteVivoxNativeOutputOnLogin`): Ein Dauer-Mute würde F8/F7-Gegenproben bedeutungslos machen und Teilnehmer ohne Tap stummen — neue falsche Fährte. v16 ändert im Default-Pfad KEIN Verhalten.

**Nutzer-bestätigte Fakten (Nachtrag 2026-09-19, verbindlich):**

1. Der Leak trat **sowohl bei Parsec- als auch bei RDP-Zugriff** auf. Gemeinsames Merkmal beider Ketten: Mikrofon = Remote-Umleitungsgerät ('Mikrofon (Parsec Virtual Audio)' bzw. 'Remoteaudio'), Host-Standard-Output = virtuelles/gesstreamtes Gerät ('Lautsprecher (VB-Audio Virtual Cable)' bzw. 'Remoteaudio').
2. mmsys am HOST: Das Parsec-Mikrofongerät besitzt **gar keine „Anhören“-Option** → OS-Listen-Loop auf dem Parsec-Mikro endgültig widerlegt (der v15-Hauptverdacht). **'Remoteaudio' (RDP-Eingabegerät) wurde noch NICHT auf „Anhören“ geprüft.**
3. Die Selbsthörung ist **„leicht versetzt“** (exakte Latenz nicht mehr erinnerlich — ein Wert zwischen 0,5 und 2 s ist plausible Netz-RTT Laptop→Host→Laptop; eine genauere Messung ist nicht mehr rekonstruierbar, aus Logs auch nicht, da die Selbsthörung außerhalb jedes Sonden-Messpunkts liegt).
4. sndvol am HOST: Ausschläge bei „Lautsprecher“ (Device-Master), „Parsec“ (App-Session!) und „Hotel Game“ (Unity) — auch bei ausgeschalteten Hintergrundgeräuschen. **Nicht dokumentiert: ob F12 während dieser Beobachtung aktiv war** → die „Hotel Game“-Ausschläge beweisen NICHTS über Vivox-native.
5. Nutzer lehnt eine Aufnahme des Leaks ab — Evidenz nur live/protokollarisch.

**Logische Beweiskette (schließt den Unity-Prozess für das Solo-Symptom aus):** v15-F12-Test: Solo-Funkkanal (1 Teilnehmer [ICH]), masterPeak exakt 0 bei gleichzeitig hörbarer Selbsthörung → Unity-Graph ausgeschlossen (Sonde misst NACH allen Filtern/Injektionen). Vivox-native ist solo **unmöglich**: Gruppenkanäle reflektieren die eigene Sendung nicht (Code verifiziert: `EnsureRadioChannelAsync` nutzt `JoinGroupChannelAsync`; die Echo-Kanal-These `JoinEchoChannelAsync` wurde gesucht und NICHT gefunden), und ohne zweiten Teilnehmer hat die Engine nichts, das sie nativ abspielen könnte. ⟹ Die Quelle liegt **außerhalb des Unity-Prozesses** — und der einzige solche Pfad, der in JEDER Session im Weg stand, ist die Parsec/RDP-Audio-Kette des Hosts.

**Neue Leit-Hypothese: die Remote-Kette spielt das Client-Mikrofon zurück.**

- **Parsec-Variante (alle Sessions bis 23:29):** Die Parsec-Host-App gibt das Mikrofon des verbundenen Clients als „Peer-Audio“ auf dem Host-Standard-Ausgabegerät wieder — genau das zeigt der „Parsec“-App-Session-Ausschlag in sndvol am HOST (Nutzer-Beobachtung; Parsec-Hosts hören ihren Gast standardmäßig). Host-Default-Output war durchgehend 'Lautsprecher (VB-Audio Virtual Cable)' — das Gerät, das Parsec zugleich zum Client capturet und streamt → **Laptop-Mikro → Host-Output → zurück zum Laptop**. Erklärt in einem Zug: leicht versetzt (RTT), überall gleich laut (OS-Ebene, kein räumliches Falloff), F12-unabhängig, blecherner „Funk-Effekt“ (Parsec-Codec-Artefakt, kein Walkie-DSP), Auftreten in jeder Session unabhängig von Unity-Interna.
- **RDP-Variante (Session 23:51, nur 'Remoteaudio'):** Kandidat = „Anhören“ auf dem 'Remoteaudio'-Eingabegerät → Wiedergabe auf 'Remoteaudio' (zugleich Default-Output) → RDP-Audio zurück zum Client. Ungeprüft — remote prüfbar (T2).
- Die PTT-Kopplung ist damit vermutlich **Scheinkorrelation**: Beim Funkgerät-Sprechen wird lauter/klarer gesprochen, was Mic-Gates/VAD öffnet (konsistent mit der v15-Timing-Beobachtung „erst Sekunden, dann sofort“).

**Kontradiktions-Register (bei Wiederaufnahme ZUERST auflösen, nicht überschreiben):**

1. **F8 (v14) vs. sndvol:** v14 schloss Vivox-native aus, weil die Stimme nach der Umleitung blieb — der Gerätewechsel wurde aber nie verifiziert (keine AUDIO-DEVICES-Zeile nach F8; ein Vivox-Device-Wechsel mid-session kann still fehlschlagen). Verdacht: F8 war ein falsches Negativ. Künftig nach jedem F8 eine Re-Log-Zeile erzwingen.
2. **Test A („laut ohne PTT → nicht gehört“) vs. VAD-Timing (v15):** widersprechen sich; Test-A-Bedingungen (Abstand zum Mikro, tatsächliche Lautstärke, Gate-Vorzustand) sind nicht dokumentiert und wurden nie kontrolliert wiederholt.
3. **„Hotel Game“-Ausschläge ohne dokumentierten F12-Zustand:** kein Beweis für Vivox-native (könnten Sidetone/Ambience/einfach kein F12 gewesen sein). Wiederaufnahme nur mit protokolliertem F12-Zustand.

**Totenliste der Theorien (Stand 2026-09-19):** Tap-Direktausgabe 2D (reeler Bug, **gefixt v11/v12** — historische Ursache des „überall gleich laut“ bis v10) · Laptop-seitiges Mic-Monitoring (widerlegt, v14 Nutzer-Checks) · OS-„Anhören“ auf dem Parsec-Mikro (widerlegt — Gerät hat gar keinen Tab) · zweiter Client / Ghost-Teilnehmer (widerlegt, Kanal-Zeilen aller Sessions) · Vivox-Echo-Kanal (widerlegt, Code-Verifikation 2026-09-19) · Vivox-native Solo-Reflexion (unmöglich, Gruppenkanal reflektiert Eigen-Sendung nicht) · Unity-Graph inkl. WalkieOutput/Tap/injizierender Quellen (widerlegt, masterPeak=0 bei F12 + hörbarer Stimme) · räumliche Sidetone (irrelevant, bei Testdistanzen >8 m konstant stumm) · Vivox-native bei fehlendem Tap (trot für Solo — für echte 2-Spieler-Fälle weiter denkbar, aber nicht Ursache dieses Symptoms).

**JETZT remote machbare Tests (ohne physischen Host-Zugriff — VOR jedem weiteren Code-Graben ausführen):**

- **T1 (entscheidend):** Spiel/Editor am Host **schließen**. Per Parsec verbinden (Client-Mikrofon an), normal sprechen. Selbsthörung (leicht versetzt)? **JA → Remote-Kette bewiesen, Earshot und Game sind unschuldig** → Issue wandert auf OS/Parsec-Ebene (Host-Default-Output vom VB-Cable weg / Parsec-Host-Peer-Audio). **NEIN → T2.**
- **T2:** Gleicher Versuch per RDP (Mikrofonübertragung im mstsc-Client aktiviert). Vorher im RDP-Desktop am Host: mmsys → Aufnahme → **'Remoteaudio' → „Anhören“-Tab prüfen** (Hauptverdacht der RDP-Kette). Selbsthörung? JA → RDP-Kette bestätigt (vermutlich genau dieser Haken).
- **T3:** Erst falls T1 UND T2 negativ ausfallen: Spiel starten, Walkie aufnehmen, PTT, sprechen — jetzt haben die v16-`VIVOX-RX`-Sonden erstmals saubere Beweiskraft (funkRx/proxRx auswerten, F7-Gegenprobe, Latenz grob notieren: „sofort“ vs. „merklich versetzt“).
- **T4 (optional, nicht diskriminierend):** Parsec-Client-Mikrofon für die Verbindung deaktivieren, Spiel an, PTT: Log muss Capture-Stillstand zeigen. Unterscheidet Ketten-vs-In-Prozess NICHT (beide brauchen das Mikro) — nur Plausibilitäts-Check.

**BLOCKIERT bis physischer Host-Zugriff — finales Protokoll („wenn du wieder am Host bist“):**

0. Vorbedingungen: lokale Anmeldung (kein Parsec, keine RDP-Sitzung), physisches Headset als Wiedergabe- UND Aufnahme-Default, Parsec-Host-App (auch Tray) beenden, VB-Cable aus dem Default-Output.
1. mmsys.cpl → Aufnahme → jedes virtuelle Gerät ('Remoteaudio', 'CABLE Output (VB-Audio Virtual Cable)', Steam, …) → „Anhören“-Status prüfen und dokumentieren (Parsec-Mikro hat bekannterweise keinen Tab).
2. Solo-Editor-Test: Walkie aufnehmen, PTT, normal sprechen. Selbsthörung? **NEIN → Remote-Kette endgültig bestätigt, Issue schließen** (Earshot-Code unverändert lassen, v16-Flag bleibt aus). **JA → erstmals saubere In-Prozess-Beweislage** → v16-Sonden auswerten, Latenz klassifizieren (sofort <100 ms = OS-Monitoring; merklich versetzt = Netz-Loop), danach Zwei-Client-Test mit zweitem physischem Rechner.
3. Ergebnis hier als eigener Abschnitt dokumentieren (Datum, Symptom, Beweis).

**Hotel-Game-Audit (2026-09-19, abgeschlossen — KEINE Ursache im Spiel, KEINE Änderungen nötig):** `WalkiePlayerController` (Owner-only-PTT, sauberes Release bei Input-Block/Drop) ✓ · `WalkieWorldItem` (Earshot-API korrekt benutzt; TestAudio-Taste T = räumlicher 12-m-Clip, kein Leak-Pfad) ✓ · `NetworkPlayerSetup` (AudioListener-Hygiene aktiv) ✓ · `LifeMemoryAudioTap`/`LifeMemoryRecorder`/`DeathFlashbackPlayer` (rein lesende Ringpuffer) ✓ · `PlayerDeathHandler` (keine Audio-Interaktion) ✓ · Szene: 'Hotel Music' 2D = Menümusik, kein Sprachpfad; die LEAK-VERDACHT-Flags der Sonde sind für dieses Symptom irrelevant. Szene und HOTEL_GAME-Repo wurden bewusst nicht angetastet (Nutzerregel).

**Artefakt-Status:** v16 wird als **Diagnose-Revision** committet (NICHT als Fix validiert; im Default-Pfad kein Verhaltens-Change — Übernahme ins Spiel später per Package-Manager-Update wie üblich). Auto-Mute default AUS. Echo-Kanal-These geprüft und verworfen. Bei Wiederaufnahme gilt: **erst T1–T3, dann erst wieder Code-Änderungen.**

**v16.1 (2026-09-19): Chrome-Gegenprobe am Host — Remote-Kette WIDERLEGT, Leak ist strikt PTT-gekoppelt. Verdacht kippt zurück In-Prozess (H1: Engine empfängt eigene Sendung).**

**Nutzer-Test (remote, am Host):** Chrome-Mikrofon-Test am HOST — Mikro funktioniert, **keine** Selbsthörung (weder bei Parsec- noch bei RDP-Verbindung), außer die Website-Option „sich selbst anhören“ wird explizit aktiviert. Zusätzlich verbindlich: **In-game ohne Walkie KEINE Selbsthörung** (Proximity sendet dauerhaft!) — nur bei gehaltener Walkie-Taste.

**Bewertung:**

1. Jede Mikrofon-Schleife auf OS-/Streaming-Ebene (Parsec-Peer-Audio, RDP-„Anhören“, VB-Cable-Loop) wäre NICHT PTT-gekoppelt — Proximity-Sprechen würde sie genauso auslösen. → Die v16-Meta-Leithypothese (Remote-Kette) ist **TOT**. Die „Parsec“-Ausschläge in sndvol brauchen eine neue Deutung: plausibel ist Parsecs Loopback-**Capture** des Default-Outputs, das als Session-Meter erscheint — kein Mic-Playback.
2. Der Leak ist strikt an den Funk-Sendezustand gekoppelt → der Täter wird mit `SetRadioTransmittingAsync(true)` scharfgeschaltet. In-Prozess-Kandidaten: (a) Sidetone-Pfad (Feed → WalkieDeviceOutput; per Design PTT-gekoppelt UND räumlich/distanz-gated — müsste bei >8 m still sein), (b) ein Vivox-native Mechanismus während TX.
3. **Paradox:** v15-F12 (masterPeak=0 + hörbar) sagt „nicht Unity-Graph“; der Solo-Gruppenkanal sagt „nicht Vivox-native“; der Chrome-Test sagt „nicht OS“. Mindestens eine dieser Aussagen ist falsch — schwächstes Glied ist die F12-**Beobachtung** (nie wiederholt; Hangover/Wahrnehmung möglich).
4. **Elegante Auflösung, die alle Fakten vereint (neue H1):** Bekommt die Vivox-Engine aus irgendeinem Grund die eigene Sendung zurück (self-participant), dann gilt alles gleichzeitig: Die RX-Taps in Unity sind per Design stumm (volume=0) → masterPeak bleibt 0 → der Ton überlebt F12 → die native Wiedergabe spielt ihn → auch solo hörbar → strikt PTT-gekoppelt. **Genau das misst die v16-`VIVOX-RX`-Sonde (`funkRx`).**

**Neues Hypothesen-Ranking:**

- **H1:** Vivox-native gibt die eigene Sendung wieder (Engine empfängt Self-Audio). Beweis: `funkRx>0` bei PTT; Gegenprobe F7 (native stumm → Ton weg). Falls bewiesen: `VivoxVoiceBackend.DiagnosticMuteVivoxNativeOutputOnLogin = true` wäre der Dauer-Fix.
- **H2:** F12-Beobachtung war fehlerbehaftet → Sidetone-/Unity-Pfad ist der Täter (z. B. Distanz-Guard versagt). Beweis: `masterPeak>0` bei PTT, oder F9/F11-Killswitch entfernt den Ton.
- **H3:** Teilweise legitimer Sidetone (nahe Walkies) + Wahrnehmung. Ausschluss durch Testdisziplin: >20 m Distanz.

**Testprotokoll v16 (im Spiel, remote machbar — Package im Spiel auf Commit `83b512d` aktualisieren; Log muss `revision='leak-hunt-v16'` zeigen):**

1. >20 m von allen Walkies entfernen, PTT halten, 10 s normal sprechen, loslassen. `VIVOX-RX`-Zeile prüfen:
   - `funkRx>0` → **H1 bewiesen** (Engine empfängt eigene Sendung; serverseitig/SDK). Direkt F7 drücken (native stumm): Ton weg → Fix gefunden (Auto-Mute-Flag aktivieren).
   - `funkRx=0`, aber `masterPeak>0` → Unity-Pfad (H2) → AUDIO-INVENTAR-Zeilen nach der spielenden Quelle durchsuchen.
   - `funkRx=0`, `masterPeak=0`, Ton trotzdem hörbar → F12-Fall reproduziert → native ohne RX-Tap-Sichtbarkeit oder Wahrnehmung → F7 drücken (native AN) und Lautstärke vergleichen; F12 drücken und Log-Zeile bestätigen.
2. F12 drücken (Log-Zeile prüfen!), weitersprechen: Ton weg? (F12-Beobachtung endlich sauber wiederholen.)
3. F9 (Capture-Tap-Hard-Mute) und F11 (Sidetone-Datenfluss kappen) je einzeln testen: Ton weg?
4. Ergebnis hier als eigener Abschnitt dokumentieren.

---

## 16. v16.2 — Testergebnis v16: F12 reproduziert (Leak ist NICHT Unity); RX-Sonden waren blind (OOB-Bug gefixt); F11 nie ausgelöst (2026-09-19, ~07:30)

**Ausgewertet: 4 Logs `voice-20260919-065653 / -065925 / -070213 / -070355` (pid20120, `revision='leak-hunt-v16'`).**

Nutzer-Beobachtungen + Log-Beweise:

1. **Normal:** Selbsthörung ✓ (erwartet).
2. **F9 (dreimal, sauber geloggt, `sourceMute=True`):** Selbsthörung unverändert — ABER die FLOW-Zeilen zeigen während F9 `clipPeak≈0,002`, `signalBlocks=0` (Mute nullt die SDK-Daten, wie in v10 bewiesen). Der Sidetone-Datenfluss war also nahezu stumm, der Ton kam trotzdem → **nicht der Sidetone-/Buss-Pfad** (H2 endgültig tot).
3. **F11: nie ausgelöst.** In allen 4 Logs fehlt jede `DIAGNOSE F11`-Zeile (F9/F12 sind geloggt). Die Nutzer-Beobachtung „F11: mich nicht mehr gehört“ stammt vermutlich aus einem Moment ohne gehaltene Sendetaste → **Datenpunkt verworfen**. F11 muss wiederholt werden (Log-Zeile prüfen!).
4. **F12 (zweimal, sauber geloggt, 07:00:47 und 07:04:39):** `listenerVol=0,000`, `masterPeak=0,000028` — Unity nachweislich unhörbar stumm; die eigene Stimme blieb trotzdem hörbar (Hintergrund weg). → **Die v15-F12-Beobachtung ist reproduziert: Der Leak kommt NICHT aus dem Unity-Prozess. H3 tot.**
5. **„Invalid parameter“-Fehler (Unity-Konsole; Editor.log 37× `SoundManager.cpp(815) m_Sound->lock`):** v16-RX-Sonden-Bug in `ReadTapRingPeak` — `AudioClip.GetData` füllt IMMER den kompletten Buffer (`data.Length`); `firstFrames` begrenzte nur die Peak-Auswertung, nicht das Lesen. Bei Ring-Wrap (frischer Tap, `writePointer < 9600`) las `GetData` über das Clip-Ende hinaus → nativer lock-Fehler + **unzuverlässige `funkRx`/`proxRx`=0-Werte (Sonden blind)** — deshalb konnte v16 nicht messen, ob die Engine die Eigen-Sendung empfängt. **Fix v16.2:** genau ein `GetData` ab Offset 0 mit Buffer in Clip-Größe (~3 s), Peak danach im Speicher über die letzten 0,2 s vor dem Schreibcursor; `AbsMax` entfällt.
6. **Testdisziplin-Verstoß:** Heute stand der Nutzer 1–4 m neben Walkies (z. B. `WALKIE OUTPUT AN: mode=SIDETONE, reason=AUDIBLE, 3,77 m` → legitimer Sidetone-Nachbarschafts-Pfad MIT Funk-Effekt zusätzlich aktiv). Außerdem frische Tap-Rebuilds durch Kanal-Wechsel (TapId 1→6→16, Kanal-Hash wechselte je Session). Leak-Tests nur mit >20 m Abstand.
7. **Geräte (bestätigt):** `input='Mikrofon (Parsec Virtual Audio)'`, Vivox-native Output = `'Lautsprecher (VB-Audio Virtual Cable)'`, `vivoxOutVol=0` (SDK-`OutputDeviceVolume` — interner Wert, offenbar ohne reale Wirkung, native Ausgabe blieb hörbar).

**Bewertung:** Nach F12-Reproduktion (nicht Unity) + F9 (Daten≈0, Ton trotzdem da → nicht Sidetone) + Chrome-Test (kein OS-Loop) ist **H1 (Vivox-native spielt die eigene Sendung) die einzige verbliebene Erklärung**. Genau die Sonden, die H1 beweisen sollten, waren blind — jetzt gefixt. Das v16.1-Paradox ist aufgelöst: „Solo-Gruppenkanal kann nichts reflektieren“ war eine Annahme, keine Messung — die Messung war kaputt.

**Testprotokoll v16.2 (im Spiel, remote machbar — Package im Spiel auf Commit `NACH DIESEM COMMIT` aktualisieren; Log muss `revision='leak-hunt-v16.2'` zeigen):**

1. >20 m von allen Walkies entfernen, PTT halten, 10 s normal sprechen, loslassen. `VIVOX-RX`-Zeile prüfen (jetzt verlässlich):
   - `funkRx>0` oder `proxRx>0` → **H1 bewiesen** (Engine empfängt die eigene Sendung zurück) → sofort F7 drücken (native stumm): Ton weg? → Dauer-Fix: `VivoxVoiceBackend.DiagnosticMuteVivoxNativeOutputOnLogin=true` aktivieren.
   - beide `0`, Ton trotzdem hörbar → native Wiedergabe ohne Kanal-Echo (Capture-Monitoring der Engine?) → F7 entscheidet: Ton weg = native bestätigt, Kanal unschuldig.
2. **F11 wiederholen** und die Log-Zeile `DIAGNOSE F11: ... BLOCKIERT` prüfen (nur dann ist der Test gültig): Ton weg?
3. Auf die Unity-Konsole achten: Der „Invalid parameter“-Spam muss **WEG** sein. Falls weiterhin Spam: Quelle ist das SDK selbst (Stereo-`SetData` mit Frame-Offset in `VivoxAudioProcessor.cs:234`) → dann Sonden-Ergebnisse mit Vorsicht lesen.
4. Ergebnis hier dokumentieren; bei F7-Positiv Auto-Mute-Flag aktivieren und Gegenprobe mit zweitem Client fahren (hören Remotes weiterhin?).

## 17. v16.2 — Testergebnis: H1 endgültig tot (kein Kanal-Echo); F11 killt die Selbsthörung; Session-2-Ton war DESIGN (3,3 m); Session-1-Widerspruch offen (2026-09-19, 07:28–07:31)

**Ausgewertet: 2 Logs `voice-20260919-072827 / -073122` (pid20120, `revision='leak-hunt-v16.2'` ✓).**

Fix-Verifikation v16.2:

- **„Invalid parameter"-Spam WEG:** Editor.log weiterhin exakt 37 Treffer (= Stand vor dem Fix, keine neuen während der Tests, Editor lief durch) → GetData-OOB-Fix wirkt.
- **Sonden jetzt verlässlich:** `clipPeak=0,13–0,98` beim Sprechen (realistische Mikrofon-Pegel statt Blindwerten).

Beweise:

1. **H1 (Vivox-native Echo) endgültig widerlegt.** Während hörbarer Sprache (`ptt=True`, `clipPeak` bis 0,43) blieben **`funkRx=0,000000` und `proxRx=0,000000`** — die Engine empfängt die eigene Sendung auf keinem Kanal zurück; natives Playback hat kein Signal. F7 (4 Zyklen mute/entmute) ohne jede Wirkung — konsistent. H1 aus der Hypothesenliste streichen; `VivoxVoiceBackend.DiagnosticMuteVivoxNativeOutputOnLogin` wird nicht gebraucht (bleibt als Werkzeug, default aus).
2. **F11 killt die Selbsthörung** (Nutzerbericht, 2 Zyklen Session 1 mit `ptt=True` im Fenster + Session 2) → der hörbare Ton hängt **kausal an unserem Sidetone-Feed/Bus**.
3. **Session 2 (073122): Die Selbsthörung war DESIGN.** `WALKIE OUTPUT AN: device='WalkieTalkie (1)', mode=SIDETONE, stream='__local__', reason=AUDIBLE, distance=3,30m, target=0,121, actual=0,063` + INVENTAR `vol=0,12` — exakt das Soll-Verhalten (sich am ANDEREN Gerät hören). F11 blockt den Feed → stumm. **Kein Leak.** (Abstandsdisziplin wieder verletzt: 3,3 m statt >20 m.)
4. **Session 1 (072827): Widerspruch, offen.** Beide Walkies außer Reichweite (51–137 m, `OUT_OF_RANGE`, `target=0`), eigenes Gerät `OWN_DEVICE_TX, target=0`, alle spielenden Unity-Quellen `vol=0` (INVENTAR vollständig — kein Cap), kein einziges `OUTPUT AN`, kein Spatializer/Mixer im Projekt (`AudioListener.volume` wäre absolut) — trotzdem: F7-Fenster hörbar, F11-Fenster stumm. Kein protokollierter Pfad erklärt das. **Beweislücke:** Während der F11-Fenster gab es KEINE FLOW-Zeilen (FLOW lief nur bei aktivem Feed) → nicht nachweisbar, ob im 1. F11-Fenster überhaupt gesprochen wurde (masterPeak dort flach 0,006–0,009 vs. 0,02–0,05 sonst bei PTT+Sprache).

Architektur-Klarheit (SDK-Quellcode `com.unity.services.vivox@16.10.0` gelesen):

- Der „Sidetone-Tap" ist ein **`VivoxCaptureSourceTap`** (liefert das LOKALE MIKROFON; `vxunity_register_for_capture_source`), kein Kanal-Audio. Das Pinnen auf den Proximity-Kanal betrifft nur die Registrierung.
- Die RX-Sonden sind `VivoxChannelAudioTap` (empfangener Kanal-Audio, nur REMOTE-Teilnehmer). `proxRx=0` in Solo ist **korrekt**: Vivox sendet die eigene Stimme nicht zurück.
- Sidetone-Datenpfad: Mikrofon → Engine-Capture → SDK-StreamClip → unser Feed (Reflektion) → `WalkieRadioBus` → `WalkieDeviceOutput` (Unity, volumengesteuert, 0,2 s Delay, EQ, Crunch).
- **F9 war ein No-Op:** `tapSource.mute` nullt weder den SDK-Clip (wird nativ gefüllt) noch den Bus. Der v16-Rückschluss „Sidetone-Datenpfad unschuldig" (gestützt auf den blinden `clipPeak≈0,002`) war ein Messartefakt.
- `VivoxCaptureSinkTap` (Audio-Push IN die Engine) wird von uns nirgends genutzt.

**Fix v16.3 (dieser Commit):** FLOW-/INVENTAR-Logging läuft jetzt auch bei F11-Block (`pttActive` von `wanted` entkoppelt); neue Felder **`micPeak`** (lokales Mikrofon direkt am Capture-Tap — Sprachnachweis alle 1–2 s, auch bei geblocktem Feed) und **`feedBlocked`** in FLOW- und VIVOX-RX-Zeilen; Sonden-Buffer pro Tap (kein Re-Alloc durch Mono-/Stereo-Wechsel); F11-Meldungstext mit beidseitiger Beweislogik; Revision `leak-hunt-v16.3`.

**Testprotokoll v16.3 (entscheidend — jede Zeile ohne `revision='leak-hunt-v16.3'` ist altes Package):**

1. >20 m von ALLEN Walkies (kein Walkie in 8 m Reichweite!), PTT halten und DURCHGEHEND laut sprechen.
2. **F11** mitten im Sprechen drücken, 10 s weiter sprechen, F11 lösen. Ton weg? — Log muss im Fenster `feedBlocked=True` UND `micPeak>0,05` zeigen, sonst ist der Test ungültig.
3. Direkt danach **F12** mitten im Sprechen, 10 s weiter sprechen. Ton weg?
4. Interpretation:
   - **F11 stumm + F12 NICHT stumm** (bei nachgewiesener Sprache in beiden Fenstern) → Ton ist bus-abhängig, aber nachweislich nicht im Unity-Mix → es existiert ein nicht protokollierter Pfad → nächste Ebene: OS-Seite vermessen (Peak-Meter am Host-Ausgang `Lautsprecher (VB-Audio Virtual Cable)` während F12/F11, Parsec-/VB-Cable-Kette).
   - **F11 stumm + F12 auch stumm** → alles konsistent: Der Ton lief über Unity (Walkie-Sidetone) → Ursache war Nähe/Duplikat, kein „Leak" → OWN_DEVICE_TX-/Selbst-Abstand-Guards gezielt prüfen.
   - **F11 NICHT stumm** (bei `micPeak>0`) → die Session-1-Beobachtung war ein Artefakt (keine Sprache im Fenster) → H2/H3-Bewertung neu aufrollen.

---

## 18. v16.3 — ROOT CAUSE BEWIESEN: OnAudioFilterRead-Injektion in `WalkieDeviceOutput` umgeht sämtliche Unity-Lautstärkeregeln; Fix v16.4 (2026-09-19, 09:04–09:06)

**Nutzer-Test v16.3 (Log `voice-20260919-090425-727`, pid20120, `revision='leak-hunt-v16.3'` ✓), 4 Szenarien mit durchgehendem Sprechen:**

| Szenario | Höreindruck | Log-Beweis (FLOW, 1 Hz) |
|---|---|---|
| Normal | Selbsthörung | `masterPeak` sprachkorreliert (0,04–0,11) |
| F11 (Feed-Block) | KEINE Selbsthörung | `feedBlocked=True`, `micPeak=0,11–0,37` (Sprachnachweis ✓), `masterPeak=0,005–0,010` ≈ Hintergrund, keine Sprache |
| F12 (Master=0) | NUR Stimme, kein Hintergrund | `listenerVol=0,000` ABER `masterPeak=0,016–0,065` sprachkorreliert; Hintergrund ≈ 0 |
| F11+F12 | komplett stumm | `masterPeak≈0,000001` |

**Rückblicks-Korrektur:** Auch das v16-Log (070355) zeigt in 1-Hz-Abtastung `07:04:40.313 masterPeak=0,046328` bei `listenerVol=0` — die frühere Lesart „masterPeak≈0 bei F12 ⇒ Leak nicht aus Unity" (v15/v16) war ein Sampling-Artefakt: die VIVOX-RX-Zeilen (2-s-Takt) trafen meist Sprechpausen. Diese Schlussfolgerung war falsch und wird hiermit zurückgenommen.

**Position während des Tests:** Nutzer 0,99 m am eigenen Gerät (`reason=OWN_DEVICE_TX, target=0,000`), zweites Gerät 61–126 m (`OUT_OF_RANGE, target=0,000`) — alle sichtbaren Quellen vol=0, kein `OUTPUT AN`. **Der offene Session-1-Widerspruch („hörbar trotz vol=0 überall") ist damit aufgeklärt: kein Widerspruch, sondern der Leak selbst.**

**Root Cause:** `WalkieDeviceOutput.OnAudioFilterRead` überschreibt `data` vollständig mit den Delay-Ring-Samples in vollem Pegel (`data[baseIdx+c]=outgoing`). Unity wendet `AudioSource.volume`/`.mute` und `AudioListener.volume` VOR `OnAudioFilterRead` auf den Datenstrom an (v12-Beweis: Tap-Filter-Input war bei volume=0 genullt) — wer `data` überschreibt, umgeht damit ALLE Lautstärkeregeln. Konsequenzen:

- Sidetone (und genauso Remote-Funk-Audio!) spielte von JEDEM eingeschalteten, nicht sendenden Gerät im Kanal in vollem Pegel — unabhängig von Distanz (120 m!), `OWN_DEVICE_TX`, `OUT_OF_RANGE` und F12.
- `PushSamples` hat kein Distanz-Gate; die Distanzregel lief allein über `source.volume` — die wirkungslos war. F11 war der einzige wirksame Kill-Schalter, weil der Ring dann leer bleibt.
- Die Distanz-Dämpfung von Remote-Funk-Stimmen an Walkie-Geräten war über dieselbe Injektion ebenfalls volumen-immun (Nebenbefund, durch den Fix mitbehandelt).

**Fix v16.4 (`WalkieDeviceOutput.cs`, Revision `leak-hunt-v16.4`):**

1. `OnAudioFilterRead` multipliziert jetzt `outgoing * outputVolume` mit `outputVolume = smoothedVolume * GlobalListenerVolume` (auf 0..1 geclampt) — die Lautstärke wird autoritativ im Filter durchgesetzt, unabhängig davon, an welcher DSP-Stage Unity Volume anwendet.
2. `source.volume` bleibt konstant 1 (EnsureAudio + LateUpdate) — verhindert Doppel-Dämpfung.
3. Neuer statischer Spiegel `WalkieDeviceOutput.GlobalListenerVolume` (volatile), pro LateUpdate aus `AudioListener.volume` gecacht — F12 (und jede künftige globale Stummschaltung) greift damit auch für die Walkie-Lautsprecher.
4. OUTPUT-Diagnosezeile loggt jetzt `actual=smoothedVolume` + `master=` (source.volume ist konstant 1 und wäre aussagelos).

**Testprotokoll v16.4 (im Spiel; Log muss `revision='leak-hunt-v16.4'` zeigen):**

1. PTT + Sprechen in >8 m Abstand zu allen anderen Walkies → KEINE Selbsthörung mehr; Gegenprobe <8 m am zweiten Gerät → Selbsthörung MIT Distanz-Falloff (deutlich leiser als zuvor).
2. F12 während PTT + Sprechen → jetzt KOMPLETT stumm (auch die eigene Stimme). Falls nicht: verbleibender Pfad ist OS-/Parsec-/VB-Cable-Seite.
3. Remote-Test (2. Client): Distanz-Dämpfung der Funk-Stimme prüfen (nah laut, fern leise, >8 m stumm) — durch den Fix erstmals tatsächlich wirksam.
4. Editor.log „Invalid parameter" weiter beobachten (sollte bei 37 bleiben).

**Ergebnis (Nutzer-Test v16.4 im Spiel, 2026-09-19): BESTANDEN.** Nach Package-Update bestätigt der Nutzer: `MaxHearingDistance` an einem Walkie hochgestellt → **sofort hörbarer Effekt**. Die Reichweiten-/Falloff-Einstellung reagiert damit erstmals live auf das Tuning — vor dem Fix war der komplette `WalkieDeviceOutput`-Pfad volumen-immun, weshalb Reichweiten-Tuning nie hörbar wurde. Da die Leak-Selbsthörung denselben Code-Pfad nutzt, ist sie konsequent mitbehoben. Glasklar-Tuning-Doku (Formel, Stellschrauben, goldene OnAudioFilterRead-Regel) ergänzt in `docs/walkie-talkie-game-integration.md`. Offen: F12-Vollverifikation + Remote-2-Client-Distanztest (Protokollpunkte 2–3), Editor.log-Beobachtung (Punkt 4).

---

*Dokument angelegt 2026-09-18. Bei jedem weiteren gescheiterten oder erfolgreichen Ansatz: hier einen kurzen Abschnitt ergänzen (Datum, Symptom, Hypothese, Fix, Log-Beweis, Ergebnis), nicht nur CHANGELOG-Zeilen.*
