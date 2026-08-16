# Earshot

**English first. German follows.** · [Deutsch](#earshot-deutsch)

Plug-and-play **co-op multiplayer** and **proximity voice chat** for **Unity 6**.

Host and join with a short code over Unity Relay. Players stay in sync with Netcode for GameObjects. Voice is a flat Vivox 2D channel; **distance, walls, doors, and rooms** are applied locally so you keep full control of how speech sounds.

Package ID: `com.earshot.coop` · Namespace: `Earshot` · Unity: **6000.0 or newer**

This repository ships **the package only**. The local Unity playtest house is not included (it is regenerated in the Editor).

---

## What this package does

| Feature | How it works |
|---|---|
| Join by code | Unity Relay — no port forwarding, VPN, or Hamachi |
| 2–8 players | Host–client. If the host leaves, the session ends |
| Movement sync | Netcode for GameObjects + `NetworkTransform` on your player |
| Proximity voice | Vivox carries audio; Earshot shapes it (never Vivox 3D) |
| Walls | Physics line test. Colliders on the Default layer block sound |
| Doors | `VoicePortal` on the door. Open = almost clear, closed = muffled |
| Rooms | Optional `VoiceZone` volumes for reverb / same-room checks |
| UI included | On-screen host/join (`CoopQuickMenu`), Escape pause (`CoopPauseMenu`) |

**Not included (on purpose):** dedicated servers, anti-cheat, WebGL voice, save games, your gameplay.

---

## Install into an existing Unity 6 project

Do **not** copy the whole GitHub folder into someone else’s game. Do **not** run **Testraum bauen** in a project that already has a level. That menu builds a demo house and would fight your scenes.

### 1. Git on the machine

Git must be installed ([git-scm.com](https://git-scm.com/)). Unity’s Package Manager uses it for Git URLs.

### 2. Add the package

In the **target** project (Unity 6):

1. `Window > Package Manager`
2. `+` → **Add package from git URL**
3. Paste:

```
https://github.com/Nima385i/MultiplayerNetworkPackage.git?path=/DevProject/Packages/com.earshot.coop
```

Unity pulls **only** `DevProject/Packages/com.earshot.coop`. It does not import the playtest house.

**Alternative without GitHub:** copy the folder `DevProject/Packages/com.earshot.coop` into your project’s `Packages/` directory, or Package Manager → **Add package from disk** → select that folder’s `package.json`.

### 3. Setup wizard (does not overwrite your work)

1. `Tools > Earshot > Setup`
2. Assign **your player prefab**
3. **Prüfen** (Inspect), then **Anwenden** (Apply)

This only adds missing invisible pieces: `Assets/Resources/EarshotSettings.asset`, a default voice profile under `Assets/Earshot/`, a `NetworkManager` if none exists, `CoopPlayer` + `NetworkObject` on the prefab if missing. Existing files are left as they are. Scene edits can be undone with Ctrl+Z.

Then: `Tools > Earshot > Prüfen` until the list is green.

### 4. Your player prefab

On the **root** of the prefab:

- `NetworkObject`
- `CoopPlayer` — drag the head / camera into **Voice Anchor**
- `NetworkTransform` (otherwise others will not see you walk)
- Your movement script, **owner-only** (same idea as Netcode: only the owner moves the capsule)

One `AudioListener` on the local player’s camera/head. Disable lobby/menu listeners when gameplay starts.

### 5. Your gameplay scene

- Keep your walls, floors, stairs, props.
- Walls need **colliders** (Default layer is the occlusion mask in the default profile).
- Add an empty GameObject and `Add Component` → **Earshot / Coop Quick Menu** (host/join overlay). Optional: **Coop Pause Menu** (Escape: volume, mute, leave, quit).
- `Player Spawner` with spawn transforms if you want fixed spawn points.

### 6. Doors (proximity)

On the door object that has the collider:

- `Voice Portal`
- Drive `Openness`: `1` = open, `0` = closed, from your existing door script

Without a portal, a closed door is just another wall.

### 7. Unity Cloud (required for internet + real microphones)

This is **not** Windows Settings → System. It is inside the Unity Editor.

1. Stay signed in to Unity Hub and the Editor.
2. `Edit > Project Settings` → **Services** (left list).
3. **Use an existing cloud project** or **Create a new cloud project**, then link it.
4. In the [Unity Dashboard](https://cloud.unity.com) for **that same** project: enable **Vivox Voice and Text** (`Development > Products`). Open **Relay** / Multiplayer once if it is not already on.
5. Back in the Editor: `Project Settings > Services > Vivox`. You should see credentials load. **Test Mode** can stay off if you use anonymous Unity Authentication (Earshot does).

Everyone who plays **this game** must use **the same** cloud project. A second Unity account is fine; do not create a second cloud project for the same game.

Then **build after linking**, so IDs are baked into the player. Send the **whole build folder** (`.exe` and `_Data`), not the exe alone.

### 8. Play with a friend (internet)

1. Host: start the game → **Spiel hosten (Internet)** / Host → copy the code.
2. Friend: enter the code → **Beitreten (Internet)** / Join.
3. Relay works across countries (for example Norway ↔ Germany). Both need internet. No VPN.

**Local same-PC test** (no account): `HostLocalAsync` / join `127.0.0.1` port `7777`, or the quick menu buttons **dieser PC**. That path has **no real microphone** (Vivox is off).

---

## Optional: try Earshot itself (demo house)

Only if you cloned this repo to **develop the package**, not to drop it into a finished game:

1. Open `DevProject` in Unity **6000.5** (or any 6000.x).
2. `Tools > Earshot > Testraum bauen` — creates two rooms and a door. This **replaces** that demo scene; it does not touch unrelated projects if you never run it there.
3. Link Services as above for internet voice.
4. Play → internet host → send the code. Builds: `File > Build Profiles > Build`.

Do not look for mic/speaker options under Windows **System**. They are in the in-game Escape menu after a session, and Vivox device lists appear after an **internet** join.

---

## Code you actually need

```csharp
using Earshot;

string code = await Coop.HostAsync();   // internet host, returns join code
await Coop.JoinAsync("ABC123");
await Coop.LeaveAsync();

Coop.StateChanged += state => { /* Hosting, Connected, Offline */ };
```

Voice starts by itself when a session starts. Mute:

```csharp
CoopVoice.ToggleMicrophone();
CoopVoice.GameVolume = 0.8f;
```

After death, if the camera should not be the ear:

```csharp
CoopVoice.ListenerOverride = otherPlayer.VoiceAnchor;
CoopVoice.ListenerOverride = null;
```

---

## Controls (demo / quick menu)

| Input | Action |
|---|---|
| WASD, mouse | Move / look (demo player) |
| E | Interact / door (if `Interactor` is on the player) |
| Escape | Pause: volume, mute, devices, leave, quit |
| F1 | Toggle host/join overlay during a session |
| F3 | Voice debug numbers (Editor; hidden in player builds by default) |

---

## Troubleshooting

| What you see | What to do |
|---|---|
| “Project not linked to a UGS project” | `Edit > Project Settings > Services` — link a cloud project, then rebuild |
| Join code fails | Same cloud project in both builds; code has no spaces; host still in session |
| You hear yourself / a beep, not the other player | You are on an old playtest with a debug probe. Current package has no probe. Rebuild from this repo |
| Hear each other for half a second, then silence | Rebuild this version: voice stays audible until it is attached to the avatar; capsules are not treated as walls |
| No voice, movement works | Internet session (not “dieser PC”); Vivox enabled on the dashboard; mic selected in Escape; not Remote Desktop |
| Walls do nothing | Colliders on Default layer; `VoicePortal` on doors; players in line of sight through the **opening**, not through the frame only |
| Sound around a corner is “through the wall” | Known limit of v0.1: one straight ray. Room–portal graph is planned in `ROADMAP.md` |
| Friend cannot join from another country | Use **Internet** host/join, not local IP. Relay does the rest |
| Services looks empty | Wrong place if you opened Windows Settings. Use the Unity Editor window. Sign in to Hub first |
| Package Manager Git URL fails | Install Git, restart Unity, check the `?path=/DevProject/Packages/com.earshot.coop` suffix |

---

## What is in this repository

```
README.md, PLAN.md, TASKS.md, ROADMAP.md, LICENSE.md
DevProject/Packages/com.earshot.coop/    ← the Unity package (this is the product)
```

**Not in git:** Unity `Library/`, the generated playtest scene, Project Settings with Cloud IDs, Vivox secrets.

Future work (corners, stairwells, reconnect) is described in [ROADMAP.md](ROADMAP.md). Architecture: [PLAN.md](PLAN.md).

---

## License

MIT — [LICENSE.md](LICENSE.md)

---
---

# Earshot (Deutsch)

Plug-and-play **Koop-Multiplayer** und **Proximity Voice** für **Unity 6**.

Hosten und Beitreten per Code über Unity Relay. Bewegung über Netcode for GameObjects. Stimme läuft bei Vivox als **flacher 2D-Kanal**; **Entfernung, Wände, Türen und Räume** rechnet Earshot lokal.

Paket-ID: `com.earshot.coop` · Namespace: `Earshot` · Unity: **6000.0 oder neuer**

Dieses Repo enthält **nur das Paket**. Die lokale Testhaus-Szene liegt nicht im Git (im Editor neu bauen).

---

## Was es kann

- Join-Code über Relay — keine Ports, kein VPN
- 2–8 Spieler, Host-Client (Host weg = Session vorbei)
- Proximity Voice: Wände dämpfen, Türen über `VoicePortal`
- Mitgelieferte UI: Host/Join, Escape-Pause (Lautstärke, Mikro, Verlassen)

**Nicht enthalten:** Dedicated Server, Anti-Cheat, WebGL-Stimme, Spielstände, Gameplay.

---

## In ein bestehendes Unity-6-Projekt einbauen

**Nicht** den ganzen GitHub-Ordner ins fremde Spiel kopieren. **Nicht** `Tools > Earshot > Testraum bauen` in einem Projekt mit fertigem Level — das erzeugt ein Demo-Haus.

### 1. Git installieren

Ohne Git kann Unity keine Git-URL laden.

### 2. Paket hinzufügen

Im **Zielprojekt**:

`Window > Package Manager` → `+` → **Add package from git URL**:

```
https://github.com/Nima385i/MultiplayerNetworkPackage.git?path=/DevProject/Packages/com.earshot.coop
```

Damit kommt nur das Paket, nicht die Testumgebung.

Oder: Ordner `DevProject/Packages/com.earshot.coop` nach `Packages/` kopieren / **Add package from disk**.

### 3. Wizard

`Tools > Earshot > Setup` → eigenes Player-Prefab → **Prüfen** → **Anwenden**.  
Legt nur fehlende Technik an, überschreibt vorhandene Dateien nicht.

Danach `Tools > Earshot > Prüfen`, bis alles grün ist.

### 4. Player-Prefab

Am Root: `NetworkObject`, `CoopPlayer` (Voice Anchor = Kopf), `NetworkTransform`, Movement nur für den Owner. Genau ein aktiver `AudioListener` am lokalen Kopf.

### 5. Eigene Szene

Wände mit Collider (Default-Layer). Leeres Objekt mit **Coop Quick Menu**. Türen: **Voice Portal**, `Openness` 1/0 an eure Tür-Logik koppeln.

### 6. Unity Cloud (Internet + echtes Mikro)

Nicht Windows **Einstellungen → System**. Sondern:

1. In Hub und Editor eingeloggt
2. `Edit > Project Settings` → **Services**
3. Existing oder new **cloud project** linken
4. Dashboard: **Vivox Voice and Text** an, Relay/Multiplayer einmal öffnen
5. Editor: `Project Settings > Services > Vivox` — Zugangsdaten erscheinen

Alle Spieler **dieses** Spiels brauchen **dasselbe** Cloud-Projekt. Anderer Unity-Account ist egal; kein zweites Cloud-Projekt für dasselbe Spiel.

**Nach** dem Linken bauen. Dem Freund den **ganzen Build-Ordner** schicken (EXE + `_Data`).

### 7. Mit Freund spielen

Host: **Spiel hosten (Internet)**, Code schicken.  
Freund: Code, **Beitreten (Internet)**.  
Geht auch DE ↔ NO. Nicht „dieser PC“ — das ist nur localhost ohne Vivox.

---

## Code

```csharp
using Earshot;

string code = await Coop.HostAsync();
await Coop.JoinAsync("ABC123");
await Coop.LeaveAsync();
CoopVoice.ToggleMicrophone();
```

---

## Probleme

| Symptom | Ursache / Fix |
|---|---|
| UGS nicht verknüpft | Project Settings → Services, dann neu bauen |
| Kein Join | Gleiches Cloud-Projekt, Host noch drin, Internet-Buttons |
| Piepton / nur eigene Stimme | Alte Testkugel — dieses Paket hat keine mehr, neu bauen |
| Halbe Sekunde Stimme, dann tot | Diese Version neu bauen (Bindung + Kapseln keine Wände) |
| Um die Ecke wie durch Beton | Bekannt, v0.1 nur Sichtlinie — siehe `ROADMAP.md` |
| Services leer | Windows-System ist falsch; Unity Editor, eingeloggt |

---

## Repo-Inhalt

Nur Dokumentation und `DevProject/Packages/com.earshot.coop`.  
Nicht im Git: `Library/`, Testhaus, Cloud-IDs.

Weiterer Plan: [ROADMAP.md](ROADMAP.md) · Architektur: [PLAN.md](PLAN.md)

## Lizenz

MIT — [LICENSE.md](LICENSE.md)
