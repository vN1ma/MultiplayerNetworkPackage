# Altes Earshot restlos aus dem eigenen Unity-Projekt entfernen

Diese Anleitung gilt fuer dein **Spielprojekt** (nicht fuer `DevProject` in diesem Repo).
Ziel: `com.earshot.coop` und alles, was der alte Setup-Wizard angelegt hat, ist weg.
Danach kannst du `com.earshot.voice` neu einbinden — das steht am Ende kurz, der
eigentliche Einbau kommt erst, wenn die Konsole hier fehlerfrei ist.

Mach vorher eine Sicherung (Git-Commit oder Kopie des Projektordners). Unity dabei
schliessen, sobald ein Schritt das verlangt.

---

## 1. Paket entfernen

Im **Package Manager** (`Window > Package Manager`):

1. Oben links auf **In Project** stellen.
2. **Earshot** / `com.earshot.coop` suchen und **Remove**.

Falls das Paket nicht in der Liste steht, liegt es als Ordner im Projekt:

- `Packages/com.earshot.coop/` — ganzen Ordner loeschen
- oder in `Packages/manifest.json` die Zeile `"com.earshot.coop": ...` entfernen

Git-URL war typischerweise:

```
https://github.com/vN1ma/MultiplayerNetworkPackage.git?path=/DevProject/Packages/com.earshot.coop
```

Diese URL darf nirgendwo mehr stehen (manifest, eigene Notizen, CI).

Unity danach einmal oeffnen. Erwartet: viele Compilerfehler — das ist richtig, die
naechsten Schritte raeumen die Referenzen ab.

---

## 2. Eigenen Code durchsuchen

Im Projektordner nach diesen Zeichenketten suchen (Rider / VS / Editor-Suche) und
jede Trefferdatei bereinigen:

| Suchen | War | Ersetzen / tun |
|---|---|---|
| `using Earshot;` | alter Namespace | Zeile loeschen |
| `Coop.` | `HostAsync`, `JoinAsync`, `LeaveAsync`, `StateChanged` | entfernen oder durch euer eigenes Multiplayer ersetzen |
| `CoopVoice.` | Mute, Lautstaerke, Geraete, `ListenerOverride` | erst spaeter durch `EarshotVoice.` ersetzen, jetzt entfernen |
| `CoopSettings` | Settings-Asset | entfernen |
| `CoopPlayer` | Player-Komponente | entfernen |
| `PlayerRegistry` | Spielerliste | entfernen |
| `CoopBootstrap` | Autostart | entfernen |
| `CoopQuickMenu` / `CoopPauseMenu` | Host/Join- und Escape-UI | entfernen |
| `CoopState` / `CoopLog` / `CoopServices` | Kern | entfernen |

Eigene Skripte, die nur Earshot gewrappt haben, komplett loeschen oder leer machen,
bis das Projekt wieder kompiliert.

---

## 3. Komponenten von Prefabs und aus Szenen nehmen

Auf **Player-Prefab** (Root) und in jeder Gameplay-Szene die fehlenden Skripte
entfernen. Unity zeigt sie als **Missing Script** (gelbes Warnsymbol).

Alte Pflicht- und Wizard-Komponenten:

- `CoopPlayer`
- `CoopBootstrap`
- `CoopQuickMenu`
- `CoopPauseMenu`
- `Player Spawner` (`Earshot/Player Spawner`)
- `Interactor` (`Earshot/Interactor`)
- `Network Door`
- `Voice Debug Overlay`
- `Voice Test Speaker` (altes Menue `Earshot/Voice Test Speaker`)
- `Lobby Camera`, `Demo First Person`, `Playtest Local Host`
- `Quick Start Layout`

Alte Voice-Komponenten (Namespace war `Earshot.Voice`, Menue `Earshot/...`):

- `Voice Zone`
- `Voice Portal`
- `Voice Transparent`
- `Voice Session Recorder`

Nicht erschrecken: dieselben Namen gibt es im **neuen** Paket wieder. Die alten
Instanzen sind trotzdem tot — das Skript gehoerte `com.earshot.coop`. Lieber
jetzt alle Missing Scripts entfernen und spaeter frisch setzen
(`Add Component` → `Earshot Voice/...`).

### Network-Stuecke nur entfernen, wenn Earshot sie gebracht hat

Der Setup-Wizard hat oft mitangelegt:

- `NetworkManager` in der Szene
- `NetworkObject` und `NetworkTransform` am Player-Prefab

**Behalten**, wenn euer eigenes Spiel Netcode for GameObjects weiter nutzt.
**Entfernen**, wenn ihr Netcode nur wegen Earshot hattet.

---

## 4. Assets loeschen

Der Wizard und Preflight haben typischerweise das hier angelegt:

```
Assets/Resources/EarshotSettings.asset
Assets/Earshot/DefaultVoiceProfile.asset
Assets/Earshot/DemoPlayer.prefab
```

Dazu oft weitere Voice-Profile und Modifier-Assets unter `Assets/Earshot/`.
Den ganzen Ordner `Assets/Earshot/` loeschen, wenn darin nur Earshot-Kram liegt.

Importierte Samples (Package Manager → Samples → Import) liegen oft unter:

```
Assets/Samples/Earshot/
```

Diesen Ordner ebenfalls loeschen.

Alte Logs (nur Diagnose, kein Spielstand):

```
EarshotLogs/
```

im Projektroot oder neben `Assets/` — darf weg.

---

## 5. Unity-Pakete, die nur Earshot gezogen hat

`com.earshot.coop` hat diese Abhaengigkeiten mitgebracht:

- `com.unity.netcode.gameobjects`
- `com.unity.services.multiplayer` (Relay / Sessions)

**Nur entfernen**, wenn ihr sie nicht selbst braucht. Vivox
(`com.unity.services.vivox`) und Unity Authentication koennen bleiben — das neue
Paket braucht sie wieder.

Multiplayer Play Mode / Multiplayer Tools / Multiplayer Center nur deinstallieren,
wenn ihr sie ausschliesslich fuer Earshot-Tests genutzt habt.

---

## 6. Editor-Menues und Testraum

Nach dem Entfernen des Pakets duerfen diese Menues **nicht mehr** existieren:

- `Tools > Earshot > Setup`
- `Tools > Earshot > Pruefen`
- `Tools > Earshot > Testraum bauen`
- `Tools > Earshot > MPPM-Testraum bauen`

Wenn sie noch da sind, steckt irgendwo noch eine Kopie von `com.earshot.coop`
(zweiter Ordner, vergessene Datei unter `Assets/`).

Der Testraum (`Tools > Earshot > Testraum bauen`) hat in **diesem** Repo nur
die lokale Dev-Szene gebaut. In einem fremden Spiel solltet ihr den Befehl
nie ausgefuehrt haben. Falls doch: die erzeugte Demo-Szene und
`Assets/Earshot/DemoPlayer.prefab` loeschen.

---

## 7. Abschluss-Check

1. `Edit > Project Settings > Player` — keine eigene Define, die `EARSHOT` oder
   aehnliches setzt (gab es standardmaessig nicht).
2. Projektweite Suche: `com.earshot.coop`, `CoopPlayer`, `CoopVoice`,
   `CoopSettings`, `EarshotSettings`, `PlayerRegistry` — **0 Treffer**.
3. Jede Szene und jedes Prefab oeffnen, das ihr angefasst habt:
   keine **Missing Script**-Warnung mehr.
4. Console: **0 Compile-Fehler**. Warnungen zu fehlendem Vivox sind ok, bis das
   neue Paket wieder da ist.

Erst wenn dieser Check gruen ist: neues Paket einbinden.

---

## 8. Neues Paket ins Spiel (Git-URL, Team)

Dieses Repo ist die Quelle. Das Spiel referenziert das Paket per Git-URL.
Mitspieler brauchen `com.earshot.voice` **nicht** lokal — Unity holt es von GitHub.
Aendern tut ihr nur hier, dann pushen, im Spiel das Paket updaten.

Voraussetzung: dieser Stand ist auf `origin/main` gepusht. Sonst findet die URL
das Paket nicht.

### 8.1 Paket laden (einmal, im Spielprojekt)

`Window > Package Manager` → `+` → **Add package from git URL**:

```
https://github.com/vN1ma/MultiplayerNetworkPackage.git?path=/DevProject/Packages/com.earshot.voice
```

Oder in `Packages/manifest.json` des **Spiels**:

```json
"com.earshot.voice": "https://github.com/vN1ma/MultiplayerNetworkPackage.git?path=/DevProject/Packages/com.earshot.voice"
```

Unity schreibt den genauen Commit nach `packages-lock.json`. Diese Datei
**mitcommitten**, damit alle denselben Stand haben.

Kompilieren lassen, Konsole: 0 Fehler. Vivox und Authentication kommen als
Abhaengigkeit mit.

### 8.1b Nach einer Aenderung an Earshot

1. Hier aendern, committen, **diesen** Repo pushen.
2. Im Spiel: Package Manager → Earshot Voice → **Update**, oder
   `packages-lock.json` neu aufloesen lassen.
3. Die geaenderte `packages-lock.json` im **Spiel-Repo** committen und pushen.
   Erst dann haben die anderen den neuen Stand.

### 8.2 Settings (einmal)

Nicht das alte `EarshotSettings` wiederverwenden.

1. Im Project-Fenster Rechtsklick → `Create > Earshot Voice > Voice Profile`
2. Speichern z.B. unter `Assets/EarshotVoice/DefaultVoiceProfile.asset`
3. Rechtsklick → `Create > Earshot Voice > Settings`
4. Speichern als **`Assets/Resources/EarshotVoiceSettings.asset`** (Name und
   `Resources/`-Ordner muessen so heissen)
5. Im Settings-Asset das Voice Profile zuweisen

### 8.3 Player-Prefab

Auf **jeden** spielbaren Charakter (lokal und remote), Root oder Kopf:

`Add Component` → **Earshot Voice / Proximity Voice**

Mehr muss am Prefab nicht stehen. Voice Anchor leer lassen, ausser der Mund
sitzt an einem Kind namens nicht `Head`/`Camera` — dann das Kopf-Transform
reinziehen.

### 8.4 Verbinden, sobald alle im Match sind

Irgendwo in eurem Sitzungsstart (nicht im Hauptmenue):

```csharp
using Earshot.Voice;

await EarshotVoice.ConnectAsync(lobbyOderMatchId, spielerName);
```

`lobbyOderMatchId` muss bei allen Spielern **derselbe** String sein.
Beim Verlassen: `await EarshotVoice.DisconnectAsync();`

Wer das noch nicht verdrahten will: leeres Objekt in der **Spielszene**,
Komponente **Earshot Voice / Connect In Scene (optional)**, gleichen
Kanalnamen eintragen.

### 8.5 Identitaet im eigenen Multiplayer (wichtig)

Ohne eigenes Netzwerk reicht Zero-Config: der eine Avatar gilt als lokal,
die PlayerId kommt von Unity Authentication.

Mit eurem eigenen Multiplayer (Netcode, Mirror, Photon, …) **muss** nach
Spawn/Sync `Bind` laufen, sonst halten sich alle Avatare fuer lokal:

```csharp
using Earshot.Voice;
using Unity.Services.Authentication;

var voice = GetComponent<EarshotProximityVoice>();

// Auf dem eigenen Charakter:
voice.Bind(AuthenticationService.Instance.PlayerId, local: true, spielerName);

// Auf jedem anderen:
voice.Bind(synchronisierteUgsPlayerId, local: false, nameDesAnderen);
```

Die synchronisierte ID ist die Unity-Authentication-`PlayerId` des jeweiligen
Spielers, nicht die Netcode-Clientnummer. Die muessen ihr selbst ueber euer
Netzwerk schicken (NetworkVariable, RPC, euer eigenes Sync).

### 8.6 Unity Cloud

Wie bisher: dasselbe Cloud-Projekt, Vivox und Authentication an.
`Edit > Project Settings > Services` — nach dem Linken neu bauen.

### 8.7 Kurz testen

1. `Earshot Voice / Voice Test Speaker` auf ein Objekt hinter einer Wand —
   Play, heranlaufen: Distanz und Dumpf ohne zweiten Rechner.
2. Zu zweit: `ConnectAsync` mit gleichem Kanal, `Bind` auf beiden Avataren,
   Internet-Build oder zwei Rechner.
