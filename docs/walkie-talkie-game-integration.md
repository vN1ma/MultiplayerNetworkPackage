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
- **Mouth Volume While Transmitting** — Restlautstärke Nähe-Mund beim Funken
- **Max Hearing Distance** — Leak-Reichweite um das Gerät
- **Audio Anchor** — optionaler Transform für die Hörposition

---



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



## Checkliste Game-Repo

- [ ] Prefab mit `EarshotWalkieTalkie`
- [ ] E aufheben / G ablegen / Q Power / LMB PTT verdrahten
- [ ] `SetCanTransmit` nur in der Hand
- [ ] Besitz/Position über euer Netz syncen
- [ ] Package per Git-URL updaten und Hörtest zu zweit (ein Gerät am Boden, eines in der Hand)

---



## Bewusst später / nicht V1

Squelch-Knacksen, Batterie, Reichweitenlimit Funkstrecke, weiche Überblendung Mund↔Funk, UI-Sprech-Indikator — siehe Phase 5 / Rest in `docs/PROGRESS.md`.