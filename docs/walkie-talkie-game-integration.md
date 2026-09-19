# Walkie-Talkie — Mitnahme ins Game-Repo

Stand: Phase 4 in `com.earshot.voice`. Dieses Dokument erklärt die Idee, was das Package schon macht, und was du im Spiel nachbauen musst.

---

## Idee in einem Satz

Ein Walkie ist ein **Welt-Objekt** mit `EarshotWalkieTalkie`: eingeschaltet empfängt es Funk am Geräte-Ort (Hand, Handgelenk, Boden). In der Hand + Linksklick = senden. Während du sendest, hörst du niemanden **fremd** über Funk (**Half-Duplex**), aber deine **eigene** Stimme kommt **versetzt** aus den anderen Walkies (**Sidetone**). Mehrere Sender: nur wer **zuerst** angefangen hat, ist hörbar, bis er aufhört.

---

## Half-Duplex + Sidetone + First-Speaker

| Regel | Verhalten |
|-------|-----------|
| Half-Duplex | Während PTT kein Empfang **anderer** Stimmen |
| Sidetone | Während PTT hörst **du** dich verzögert an allen **anderen** Geräten desselben Kanals (z.B. Boden) |
| First-Speaker | Mehrere funken: Zuhörer hören nur den, der zuerst gestartet hat; danach der Nächste |
| Alle Geräte | Jedes eingeschaltete Empfangs-Walkie spielt (Fan-out), nicht nur eines |

---



## Was das Package macht (`com.earshot.voice`)


| Baustein        | Datei / API                                | Verhalten                                                                                |
| --------------- | ------------------------------------------ | ---------------------------------------------------------------------------------------- |
| Komponente      | `EarshotWalkieTalkie`                      | An Objekt hängen. Kanal-ID, An/Aus, CanTransmit, PTT, EQ, Delay                          |
| Vivox-Funkkanal | `VivoxVoiceBackend` + `IVoiceRadioBackend` | Separater Kanal `earshot.radio.{id}` pro logischer ID                                    |
| Senden          | `SetTransmitting(true)`                    | Mikro nur in den Funkkanal (Mund/Proximity auf dem Draht stumm)                          |
| Empfang         | `WalkieDeviceOutput` + Bus                 | Stimme an **allen** Empfangs-Geräten, blechern, 3D, Delay                                |
| Sidetone        | `WalkieSidetoneCapture`                    | Eigene Stimme während PTT versetzt an anderen Walkies                                    |
| First-Speaker   | `WalkieTalkArbitration`                    | Nur wer zuerst gefunkt hat, bis er aufhört                                               |
| Leak            | Unity Spatial Audio                        | Wer nah am Gerät steht, hört den Funkton vom Geräte-Transform                            |
| Half-Duplex     | Registry + Bus                             | Lokal sendend → kein Fremdempfang (Sidetone bleibt)                                      |
| Mund dämpfen    | `WalkieRules.MouthVolumeScale`             | Solange derselbe Sprecher funkt und Radio-Audio ankommt, Proximity leiser                |

Der lokale Sidetone-Capture öffnet kein zweites Mikrofon. Er liest genau einen
`VivoxCaptureSourceTap`, nullt dessen Filterpuffer und hält dessen eigene `AudioSource`
zusätzlich gemutet. Die Stimme darf deshalb ausschließlich über `WalkieDeviceOutput`
hörbar werden. Bei Diagnosebedarf zeigen `WALKIE CAPTURE FLOW` und `AUDIO DEVICES`
getrennt Eingangssignal, Callback-Zahl, direkten Source-Ausgang und Vivox-Gerätewahl.


**Nicht im Package:** Mesh, Animator, Hand-/Hüft-Slots, E/G/Q/LMB, Netzwerk-Besitz „wer hält welches Walkie“.

---



## Spielregeln (Soll-Verhalten)

```
Gerät AUS                         → stumm
Gerät AN (Hand / Handgelenk / Boden) → Empfang an diesem Gerät
In Hand + LMB                     → senden; eigene Stimme versetzt an anderen Walkies
                                  → kein Fremdempfang (Half-Duplex)
Anderer sendet, ich nicht         → ich höre ihn an allen meinen Empfangs-Walkies
Mehrere senden gleichzeitig       → Zuhörer hören nur den Ersten, bis der aufhört
Beliebig viele Walkies            → gleiche ChannelId = gleiches Netz
```

---



## Was du im Game-Repo baust



### 1. Prefab

- Mesh / Material / Animator (dein Look)
- `EarshotWalkieTalkie` drauf
- Optional leeres Child als `AudioAnchor` (Lautsprecher-Punkt)



### 2. Input → Package-API


| Taste (Vorschlag) | Spiel-Aktion | Package-Aufruf                                                              |
| ----------------- | ------------ | --------------------------------------------------------------------------- |
| **Q**             | Ein/Aus      | `walkie.SetPowered(!walkie.PoweredOn)`                                      |
| **E**             | Aufheben     | Parent an Hand-Slot, `SetCanTransmit(true)`                                 |
| **G**             | Ablegen      | Parent lösen / Welt, `SetCanTransmit(false)`, ggf. `SetTransmitting(false)` |
| **LMB** halten    | Reinsprechen | `SetTransmitting(true)` / beim Loslassen `false`                            |


Am Handgelenk (passiv): `SetPowered(true)`, `SetCanTransmit(false)` — nur Empfang.

### 3. Netzwerk (dein Multiplayer)

Das Package kennt **kein** Netcode. Sync im Spiel z.B.:

- Welches Objekt welcher Spieler hält
- `PoweredOn` / `ChannelId` (falls änderbar)
- Position wenn auf dem Boden

PTT und CanTransmit können lokal bleiben: Vivox trägt die Stimme nur, wenn **dieser** Client `SetTransmitting(true)` setzt. Remotes hören über den Funkkanal, sobald sie ein eingeschaltetes Gerät auf derselben `ChannelId` haben.

#### ⚠️ SetTransmitting/SetCanTransmit NIE per Netzwerk-Callback auf allen Clients aufrufen

Das ist die häufigste Fehlerquelle bei zwei+ Spielern. Wenn du z.B. `IsTransmitting` als `NetworkVariable<bool>` synchronisierst und in `OnValueChanged` auf **jedem** Client (Besitzer **und** Remote) `walkie.SetTransmitting(newValue)` aufrufst, denkt der **fremde** Client fälschlich, ER würde gerade senden:

- sein lokaler Vivox-Capture-Sidetone wird fälschlich aktiviert
- er hört ein Phantom-Sidetone von sich selbst
- `LocalIsTransmitting` ist bei ihm fälschlich `true`, blockiert lokal den Fremdempfang (Half-Duplex greift grundlos)

**Richtig:** `SetTransmitting`/`SetCanTransmit` nur auf der Instanz aufrufen, die dem **lokalen** Client gehört (z.B. `if (networkObject.IsOwner) walkie.SetTransmitting(pressed);`). Auf allen Clients einmal beim Spawn zusätzlich aufrufen:

```csharp
walkie.SetLocalOwnership(networkObject.IsOwner);
```

Das Package ignoriert `SetTransmitting(true)` mit einer Log-Warnung, wenn `SetLocalOwnership(false)` gesetzt wurde (Default ist `true`, also unverändertes Verhalten ohne diesen Aufruf — nur für Einzelspieler-Tests sicher). Willst du bei Remote-Spielern trotzdem eine visuelle "sendet gerade"-Anzeige (LED etc.), löse das über eine eigene, rein optische NetworkVariable — nicht über `SetTransmitting`.

#### ⚠️ Kein zweites, unsynchronisiertes Walkie-Objekt pro Spieler (z.B. Ego-Sichtmodell)

Falls ihr fürs Ego-Modell (First-Person-Ansicht der Hand) ein **separates** GameObject/Prefab mit eigener `EarshotWalkieTalkie` verwendet (getrennt vom „echten", vernetzten Welt-Objekt), muss dessen Zustand exakt gespiegelt werden (`SetPowered`, `SetCanTransmit`, `SetTransmitting` — alle drei), sonst denkt das Package, es gäbe ein zweites Gerät auf dem Kanal, das nie sendet und daher ganz normal Sidetone abspielen darf — und das sitzt dann direkt an deinem Kopf.

Das Package hat dafür eine Sicherung (`MinSidetoneSelfDistance`, 0,5 m): Sidetone wird an einem Gerät, das quasi an deiner eigenen Hörposition klebt, nie abgespielt, egal was sein eigenes `IsTransmitting` sagt. Zusätzlich loggt jedes `SetTransmitting(true)` alle anderen Geräte auf demselben Kanal mit Entfernung ins Session-Log (`WALKIE DEBUG: ...`) — damit siehst du sofort, ob es ein zweites Objekt gibt.

### 4. Viele Geräte

Einfach mehrere Prefab-Instanzen. Gleiche `channelId` → gleiches Funknetz. Verschiedene IDs → getrennte Netze.

---



## Minimal-Beispiel (Spielcode-Skizze)

```csharp
// Am lokalen Spieler / Interact-System — Pseudo
void OnPickup(EarshotWalkieTalkie walkie) {
    AttachToHand(walkie);
    walkie.SetCanTransmit(true);
}

void OnDrop(EarshotWalkieTalkie walkie) {
    walkie.SetTransmitting(false);
    walkie.SetCanTransmit(false);
    PlaceInWorld(walkie);
}

void Update() {
    if (heldWalkie == null) return;
    if (Input.GetKeyDown(KeyCode.Q))
        heldWalkie.SetPowered(!heldWalkie.PoweredOn);
    heldWalkie.SetTransmitting(Input.GetMouseButton(0) && heldWalkie.PoweredOn);
}
```

Proximity-Chat bleibt unverändert: `EarshotProximityVoice` auf dem Player. Walkie ist **optional** zusätzlich.

---



## Inspector-Felder (Package)

- **Channel Id** — logische ID (`default`, `ops`, …)
- **Start Powered** — beim Spawn an?
- **Can Transmit** — oft zur Laufzeit vom Spiel gesetzt
- **Radio Volume / HighPass / LowPass** — Blech-Sound
- **Transmission Delay Seconds** — Walkie-Delay
- **Radio Crunch** — Alter-Funk-Charakter (Bit-/Sample-Reduktion + Verzerrung), 0 = aus
- **Mouth Volume While Transmitting** — Restlautstärke Nähe-Mund beim Funken
- **Max Hearing Distance** — Leak-Reichweite um das Gerät
- **Audio Anchor** — optionaler Transform für die Hörposition

---



## Lautstärke & Reichweite — glasklar (Stand v16.4, 2026-09-19)

**Das Wichtigste zuerst:** Seit dem v16.4-Fix wirken **alle Lautstärke- und Reichweiten-Einstellungen wieder wirklich hörbar** — inklusive `Max Hearing Distance`. Davor umging ein Bug sämtliche Lautstärkeregeln (Details unten). Im Spiel bestätigt: `Max Hearing Distance` an einem Walkie hochgestellt → sofort hörbarer Effekt.

### Welche Stellschrauben es gibt (Multiplikationskette)

```
hörbare Lautstärke = Basis × Distanz-Falloff × Global × Listener-Master
```

| Stellschraube | Wo | Wirkung |
|---|---|---|
| **Basis** | `Radio Volume` (fremde Funk-Stimme) bzw. `ActiveSidetoneWorldVolume` (eigene Stimme als Sidetone an fremden Geräten) | Grundpegel 0–1 |
| **Distanz-Falloff** | berechnet aus `Max Hearing Distance` | Formel unten |
| **Global** | `EarshotVoice.HeardVoiceVolume` | szene-global für alle Walkies |
| **Listener-Master** | `AudioListener.volume` (wird für den Filter gespiegelt) | z. B. F12-Diagnose |

### Die Distanz-Formel

```
hörbar nur wenn  d < MaxHearingDistance
falloff = (1 − d / MaxHearingDistance)²      mit d geklemmt auf mind. 0,9 m (Nahfeld-Deckel gegen Rückkopplung)
```

Beispiel `MaxHearingDistance = 8 m`:

| Distanz | Falloff |
|---|---|
| 0,9–2 m | 0,56–1,00 |
| 4 m | 0,25 |
| 6 m | 0,06 |
| 7,7 m | 0,001 |
| ≥ 8 m | 0 (`OUT_OF_RANGE`) |

**Wichtig beim Tuning:** `MaxHearingDistance` erhöhen vergrößert nicht nur die Reichweite, sondern macht den Ton **auch in mittleren Distanzen lauter** (weil `1 − d/max` steigt). Wer nur die Reichweite erweitern, die Nah-Lautstärke aber behalten will, senkt `Radio Volume` entsprechend mit.

Zusätzliche Abschaltungen (Lautstärke 0, unabhängig von der Formel):

- **OWN_DEVICE_TX** — das Gerät sendet gerade selbst → eigener Lautsprecher stumm (Half-Duplex).
- **OUT_OF_RANGE** — Zuhörer weiter als `MaxHearingDistance` entfernt.
- Kein bekannter Zuhörer-Ort → lieber still als versehentlich volle Lautstärke.

### Warum das früher nicht wirkte (die goldene Regel)

Der Walkie-Ton entsteht in `OnAudioFilterRead` (Delay-Ring + Radio Crunch) und **überschreibt** dabei `data` komplett. Unity wendet `AudioSource.volume`/`.mute` und `AudioListener.volume` aber **VOR** `OnAudioFilterRead` auf den Datenstrom an — wer dort `data` überschreibt, **umgeht alle Lautstärkeregeln**. Deshalb seit v16.4:

1. Die Lautstärke wird **im Filter selbst** durchgesetzt: `outputVolume = smoothedVolume * GlobalListenerVolume` (geclampt 0–1).
2. `source.volume` bleibt konstant 1, Unity-Rolloff konstant 1 — die Distanz rechnen wir selbst (sonst doppelt gedämpft).
3. Der `AudioListener.volume`-Master wird zusätzlich gespiegelt, damit globale Stummschaltung auch für Walkie-Lautsprecher greift.

> **Eiserne Regel für zukünftige Audio-Arbeit:** Wer in `OnAudioFilterRead` `data` überschreibt, muss jede gewünschte Lautstärke **selbst im Filter multiplizieren** — `AudioSource.volume`, `.mute` und `AudioListener.volume` wirken dann nicht mehr. (Beweis: Log `20260919-090425`, Debug-Historie Abschnitt 18.)

## Wo im Package der Code liegt

```
Runtime/Walkie/
  EarshotWalkieTalkie.cs     ← öffentliche Komponente
  WalkieRules.cs             ← Half-Duplex, Kanalnamen, Mund-Skala (testbar)
  WalkieTalkieRegistry.cs    ← alle Geräte + lokaler PTT-Zustand
  WalkieRadioSync.cs         ← Vivox-Funkkanäle joinen/lassen
  WalkieSidetoneCapture.cs   ← eigene Stimme während PTT
  WalkieTalkArbitration.cs   ← First-Speaker-Lock
  WalkieRadioBus.cs          ← Fan-out an alle Empfangs-Geräte
  WalkieDeviceOutput.cs      ← Lautsprecher + Delay am Gerät
  WalkieRadioTapFeed.cs      ← Vivox-Funk → Bus
```

Tests: `Tests/EditMode/WalkieRulesTests.cs`

---



## Debug: F7 = Graph-Debug-HUD (v16.5)

Zum Testen, wie Schall durchs Hotel „reist": **F7** im Spiel drückt → unten rechts erscheint
das `VoiceGraphDebugHUD` (wird automatisch mit dem VoiceRuntime erzeugt, kein Setup im Game-Repo).

- **DU:** eigene `VoiceZone` + Position (anhand des aktiven `AudioListener`)
- **Pro Remote-Spieler und Walkie** eine Karte: Ziel-Zone, **Luftlinie vs. LAUFWEG** (identisch
  zur Rechnung der Mund-Stimme inkl. Türen-/Ecken-Aufschlag), Türen-Kette mit Offenheitsgrad
  (z. B. `Du -> [Tuer 100%] -> [Tuer 15% ZU] -> Ziel`), Portal-/Raum-Zähler, Geschlossenheit
- Kein Weg durch Türen → „KEIN Weg durch Tueren -> Occlusion/Sichtlinie"
- **Walkie-Karten tragen eine gelbe Warnung:** der Geräteton dämpft nach Luftlinie
  (`MaxHearingDistance`), NICHT nach Graph — genau diese Lücke sichtbar zu machen ist der
  Sinn des HUDs (Designfrage in `docs/OFFENE-PUNKTE.md`)
- Checkbox im HUD: **Welt-Linien** (grün = offene Tür, rot = geschlossene; nur bei aktivierten
  Gizmos in Scene/Game sichtbar, durch Wände sichtbar)

Voraussetzungen: Damit der Graph rechnet, brauchen Räume `VoiceZone`- und Türen
`VoicePortal`-Trigger (siehe oben). Ohne Zonen zeigt das HUD „keine Zone" und der Voice-Fallback
(Occlusion/Sichtlinie) gilt.

Die alten Leak-Hunt-Tasten F8–F12 sind **default aus**; wiedereinschaltbar über
`Diagnostic Hotkeys Enabled` an der WalkieSidetoneCapture-Komponente am „Earshot Voice Runtime"-
Objekt (nur während Play). Volle Belegungstabelle: `docs/debug-keys.md`.

---

## Checkliste Game-Repo

- [ ] Prefab mit `EarshotWalkieTalkie`
- [ ] E aufheben / G ablegen / Q Power / LMB PTT verdrahten
- [ ] `SetCanTransmit` nur in der Hand
- [ ] `SetLocalOwnership(networkObject.IsOwner)` einmal beim Spawn — **PTT/CanTransmit nie auf fremden Clients aufrufen**
- [ ] Besitz/Position über euer Netz syncen
- [ ] Package per Git-URL updaten und Hörtest zu zweit (ein Gerät am Boden, eines in der Hand)

---



## Bewusst später / nicht V1

Squelch-Knacksen, Batterie, Reichweitenlimit Funkstrecke, weiche Überblendung Mund↔Funk, UI-Sprech-Indikator — siehe Phase 5 / Rest in `docs/PROGRESS.md`.