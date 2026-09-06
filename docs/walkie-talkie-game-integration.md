# Walkie-Talkie — Mitnahme ins Game-Repo

Stand: Phase 4 in `com.earshot.voice`. Dieses Dokument erklärt die Idee, was das Package schon macht, und was du im Spiel nachbauen musst.

---

## Idee in einem Satz

Ein Walkie ist ein **Welt-Objekt** mit `EarshotWalkieTalkie`: eingeschaltet empfängt es Funk am Geräte-Ort (Hand, Handgelenk, Boden). In der Hand + Linksklick = senden. Während du sendest, hörst du niemanden über Funk (**Half-Duplex**). Der Ton kommt leicht **versetzt** an (**Walkie-Delay**). Optik, Input und Pickup/Drop gehören ins Spiel.

---

## Half-Duplex (kurz)

Wie ein echtes Funkgerät: **nur eine Richtung gleichzeitig**. Solange du PTT hältst, ist Empfang stumm. Redet der andere, während du auch hältst, hört keiner den anderen über Funk. Telefon wäre Full-Duplex (beide gleichzeitig).

---

## Was das Package macht (`com.earshot.voice`)

| Baustein | Datei / API | Verhalten |
|----------|-------------|-----------|
| Komponente | `EarshotWalkieTalkie` | An Objekt hängen. Kanal-ID, An/Aus, CanTransmit, PTT, EQ, Delay |
| Vivox-Funkkanal | `VivoxVoiceBackend` + `IVoiceRadioBackend` | Separater Kanal `earshot.radio.{id}` pro logischer ID |
| Senden | `SetTransmitting(true)` | Mikro nur in den Funkkanal (Mund/Proximity auf dem Draht stumm) |
| Empfang | `VoiceRuntime` Radio-Pfad | Stimme am nächsten **eingeschalteten** Gerät auf dem Kanal, blechern (High-/LowPass), 3D |
| Leak | Unity Spatial Audio | Wer nah am Gerät steht, hört den Funkton vom Geräte-Transform |
| Half-Duplex | `WalkieRules.ShouldPlayReceivedRadio` | Lokal sendend → kein Empfang |
| Delay | `WalkieAudioDelay` | Lokale Verzögerung am Empfänger (Inspector, Default ~0,2 s) |
| Mund dämpfen | `WalkieRules.MouthVolumeScale` | Solange derselbe Sprecher funkt und Radio-Audio ankommt, Proximity leiser |

**Nicht im Package:** Mesh, Animator, Hand-/Hüft-Slots, E/G/Q/LMB, Netzwerk-Besitz „wer hält welches Walkie“.

---

## Spielregeln (Soll-Verhalten)

```
Gerät AUS                         → stumm
Gerät AN (Hand / Handgelenk / Boden) → Empfang an diesem Gerät
In Hand (CanTransmit) + LMB halten → senden; Empfang aus (Half-Duplex)
Anderer sendet, ich nicht          → ich höre ihn am Gerät (mit Delay)
Beide gleichzeitig LMB             → keiner hört den anderen über Funk
Beliebig viele Walkies             → gleiche ChannelId = gleiches Netz
```

---

## Was du im Game-Repo baust

### 1. Prefab

- Mesh / Material / Animator (dein Look)
- `EarshotWalkieTalkie` drauf
- Optional leeres Child als `AudioAnchor` (Lautsprecher-Punkt)

### 2. Input → Package-API

| Taste (Vorschlag) | Spiel-Aktion | Package-Aufruf |
|-------------------|--------------|----------------|
| **Q** | Ein/Aus | `walkie.SetPowered(!walkie.PoweredOn)` |
| **E** | Aufheben | Parent an Hand-Slot, `SetCanTransmit(true)` |
| **G** | Ablegen | Parent lösen / Welt, `SetCanTransmit(false)`, ggf. `SetTransmitting(false)` |
| **LMB** halten | Reinsprechen | `SetTransmitting(true)` / beim Loslassen `false` |

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
  WalkieAudioDelay.cs        ← lokales Delay

Runtime/Voice/
  IVoiceBackend.cs           ← VoicePathKind, IVoiceRadioBackend
  VivoxVoiceBackend.cs       ← Multi-Kanal + TransmissionMode.Single
  VoiceRuntime.cs            ← Radio-Pfad auswerten, Mund dämpfen
  VoiceEmitter.cs            ← PathKind + Delay-Hook
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
