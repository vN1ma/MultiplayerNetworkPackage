# Earshot Proximity Chat — Integrationsanleitung

Diese Anleitung ist für jemanden geschrieben, der Unity und Voice-Chat zum ersten Mal einrichtet. Jeder Schritt sagt **wohin klicken** und **warum**.

Dieser Ordner ist **nur der Proximity-Chat** (Stimmen in der Spielwelt, je näher desto lauter). Er enthält **kein** Multiplayer, keine Szenen und kein Lobby-System. Das andere Spiel hat den Multiplayer schon. Hier kommt nur die Stimme dazu.

---

## Zuerst: drei verschiedene „Accounts“ (das verwechselt fast jeder)

Wenn du ein fremdes Spiel von GitHub klonst und in **deinem** Unity mit **deinem** Account öffnest, gibt es drei getrennte Türen. Eine offene Tür öffnet die anderen **nicht**.

| Tür | Was sie ist | Was sie erlaubt | Was sie **nicht** erlaubt |
|---|---|---|---|
| **GitHub** | Der Code (Skripte, Szenen, Prefabs) | Klonen, Branches, Commits | Unity-Cloud-Dienste wie Vivox |
| **Unity Editor / Unity Hub** | Mit welcher Unity-ID du lokal arbeitest (oben rechts im Editor steht die E-Mail) | Das Projekt öffnen, Play drücken, Skripte schreiben | Credentials vom Dashboard des anderen holen, wenn du dort kein Mitglied bist |
| **Unity Cloud / Dashboard** | Das Online-Projekt hinter `cloud.unity.com`, an das das Spiel **verknüpft** ist. Dort wohnen Authentication und Vivox | Voice-Server, Spieler-Login für Stimme | Das hat **nichts** mit GitHub-Rechten zu tun |

**Wichtig:** Du brauchst **nicht** sein Unity-Passwort. Du musst auch **nicht** in Unity Hub mit seinem Account angemeldet sein. Im Gegenteil: bleib mit **deinem** Unity-Account im Editor. Er muss dich nur auf dem **Dashboard** als Mitglied einladen — so wie man jemanden in Google Docs einlädt, ohne das Passwort zu teilen.

**Mach das nicht:** Im Editor das Cloud-Projekt **unlinken** und ein **neues** Unity-Cloud-Projekt mit deinem Account anlegen. Dann hast du eigene Vivox-Schlüssel. Deine Stimme und seine Stimme laufen auf **zwei verschiedenen Servern** und hören sich nie.

---

## Dein Fehler: `HTTP/1.1 403 Forbidden` bei Vivox

Die Meldung kommt aus:

`Unity.Services.Vivox.Editor.VivoxApiClient` → `GetAndSetVivoxCredentials`

**Was Unity gerade versucht hat**

1. Im geklonten Projekt steht eine **Project ID** (eine lange UUID). Die gehört **seinem** Unity-Cloud-Projekt, nicht deinem persönlichen.
2. Das Vivox-Paket im Editor sagt: „Ich hole die Voice-Schlüssel automatisch vom Dashboard.“
3. Dafür benutzt es **deine** Unity-Anmeldung (die E-Mail oben rechts im Editor).
4. Das Dashboard antwortet **403 Forbidden** = „Diese Person darf die Schlüssel dieses Projekts nicht lesen.“

Das ist **kein** Bug in den Skripten und **kein** GitHub-Problem. Typische Ursachen, oft mehrere gleichzeitig:

1. Du bist **kein Mitglied** seiner Unity-Organisation / seines Cloud-Projekts.
2. Vivox ist auf dem Dashboard **noch nicht eingeschaltet** (nur der Owner kann Dienste anschalten).
3. Du bist mit der **falschen Unity-E-Mail** im Editor angemeldet (private Gmail statt der Adresse, die er eingeladen hat).
4. Authentication ist nicht an — Vivox 16 braucht beides.

**Fix in einem Satz:** Er lädt deine Unity-E-Mail aufs Dashboard ein **und** schaltet Authentication + Vivox an. Danach Unity Editor neu starten. Die 403 muss weg sein, **bevor** ihr Voice im Spiel testet.

---

## Wer macht was?

### Nur der Projektinhaber (Owner) kann das

Diese Dinge kannst du mit deinem Account **nicht** erledigen, auch wenn du Admin auf GitHub bist.

| Aufgabe | Warum nur er |
|---|---|
| Dich in die **Unity-Organisation** bzw. ins **Cloud-Projekt** einladen | Rechte auf `cloud.unity.com` hat nur Owner/Manager |
| **Vivox** für das Cloud-Projekt einschalten | Dienste an/aus nur der Organisations-Owner |
| **Authentication** einschalten (Anonymous reicht) | ebenfalls ein Dienst auf seinem Cloud-Projekt |
| Environment wählen (`production` o.ä.) und bei Bedarf Credentials erzeugen | liegt auf seinem Dashboard |
| Optional: Vivox-Credentials ins Projekt eintragen, falls du kein Dashboard-Zugang bekommen sollst | die Schlüssel gehören seinem Projekt |

### Das kannst du mit deinem Account

| Aufgabe | Hinweis |
|---|---|
| Repo klonen, in Unity öffnen, mit **deiner** Unity-ID angemeldet bleiben | oben rechts im Editor prüfen |
| Diesen Ordner ins Projekt legen, Packages prüfen, Komponenten auf Charaktere | steht weiter unten |
| Play Mode, Mikrofon, Kopfhörer, Logs | sobald 403 weg ist |
| Einladung auf dem Dashboard **annehmen** | Link in der E-Mail, **dieselbe** Adresse wie im Unity Editor |

### Was ihr beide braucht, aber jeder bei sich

- Unity 6 (6000.x)
- Headset beim Test zu zweit auf **einem** PC (sonst hörst du dich über Lautsprecher selbst — das ist Echo, kein Bug)
- Windows: Mikrofon für Unity/den Build in den System-Datenschutz-Einstellungen erlaubt

---

# Teil A — Checkliste für den Projektinhaber

Schick ihm diesen Teil. Er braucht **deine Unity-E-Mail** (die im Editor oben rechts, oft dieselbe wie Unity Hub).

### A1. Dich ins Cloud-Projekt einladen

1. Im Browser [https://cloud.unity.com](https://cloud.unity.com) öffnen, mit **seinem** Account anmelden.
2. Oben die richtige **Organization** wählen (falls er mehrere hat).
3. **Projects** öffnen und **das Projekt** wählen, mit dem das Unity-Spiel schon verknüpft ist (gleiche Project ID wie im Editor unter `Edit > Project Settings > Services`).
4. Links **Members** (manchmal unter **Development > Members**).
5. **Add members** / **Invite**.
6. **Deine E-Mail** eintragen — exakt die Unity-ID, mit der du im Editor angemeldet bist.
7. User type mindestens **User** (besser **Contributor** / Manager, wenn die UI das so nennt). Du musst das Projekt **sehen** und Dienste **nutzen** dürfen. Billing-Rechte brauchst du nicht.
8. Einladung absenden.

Du bekommst eine E-Mail von Unity. **Annehmen.** Danach in Unity Hub/Editor einmal ab- und wieder anmelden, oder Editor neu starten.

Wenn die Einladung an die falsche Adresse geht (z.B. Uni-Mail, aber Unity Hub nutzt Gmail), bleibt der 403.

### A2. Authentication einschalten (oft sieht die Seite „leer“ aus — das ist okay)

Vivox weiß erst nach einem Login, **wer** der Spieler ist. Dafür ist „Unity Authentication“ da. Das ist **nicht** der Unity-Editor-Login und **nicht** GitHub. Es ist ein unsichtbarer Spieler-Login **im laufenden Spiel** (darf komplett anonym / ohne Passwort sein).

**Im Unity-Projekt muss vorher nichts extra gebaut werden**, damit diese Dashboard-Seite funktioniert. Kein Skript, keine Szene, kein Prefab. Das Package `Authentication` braucht ihr erst später im Editor zum Play-Test.

Was ihr auf der Seite oft seht:

- Text in die Richtung **„No identity providers“** / keine Anbieter / leer
- oben rechts ein Button **Add**

Das **Add** ist **nicht** der Startknopf für Voice. Es legt nur optionale Logins an (Google, Apple, Steam, Unity Player Accounts, Benutzername+Passwort). **Für Proximity-Chat braucht ihr das nicht.** Anonymous Sign-in ist eingebaut und steht deshalb oft **gar nicht** in der Add-Liste. Die leere Liste ist normal.

Was der Owner tun soll:

1. Dashboard → dasselbe Cloud-Projekt (nicht ein anderes).
2. **Products** / **Player Authentication** / **Authentication** öffnen.
3. Falls ein großer Button **Get started**, **Enable** oder **Set up** da ist: den klicken und durchklicken, bis der Dienst für dieses Projekt an ist.
4. Den Button **Add** oben rechts **nicht** drücken, außer ihr wollt später bewusst Google/Steam. Für den Start: Seite so lassen.
5. Oben auf der Seite die **Environment** prüfen (`production` ist in Ordnung) — dieselbe wie unter Vivox und im Unity-Editor unter `Project Settings > Services`.

Fertig. Es gibt kein Häkchen „Anonymous an“, das ihr zwingend setzen müsst. Sobald der Dienst enabled ist, darf `SignInAnonymouslyAsync` (das macht `ProxVoice.ConnectAsync` von selbst, falls noch niemand eingeloggt ist) eine `PlayerId` holen.

### A3. Vivox einschalten

1. Dashboard → **Products** → **Vivox Voice and Text Chat** (manchmal unter Communication).
2. Onboarding / **Enable**. Engine: **Unity**.
3. Am Ende gibt es **Credentials**: Server, Domain, Issuer, Token Key. Die zieht der Editor später automatisch, **wenn** du Mitglied bist.
4. Fertig. Er muss dir die Schlüssel **nicht** per Chat schicken, wenn die Einladung geklappt hat.

Nur der Owner kann diesen Dienst anschalten. Wenn Vivox aus ist, bekommst auch du als Mitglied oft Fehler beim Credentials-Abruf.

### A4. Danach bei dir prüfen (Owner sagt Bescheid)

Wenn Teil A durch ist, bei dir:

1. Unity Editor: oben rechts **deine** E-Mail, die eingeladen wurde.
2. `Edit > Project Settings > Services`  
   - Projekt ist **linked** (Name + Project ID sichtbar).  
   - **Nicht** Unlink drücken.  
   - Environment: dasselbe wie bei ihm, meist `production`.
3. In derselben Settings-Liste **Vivox** öffnen.  
   Unter Environment Configuration sollten Felder wie Server / Domain / Issuer **von selbst voll** sein.
4. Console: **kein** `403 Forbidden` / `GetAndSetVivoxCredentials` mehr.

Wenn die Felder leer bleiben: einmal Play drücken und wieder stoppen (Unity lädt Credentials oft erst dann), Editor neu starten. Immer noch 403 → Einladung nicht angenommen, falsche E-Mail, oder Vivox auf dem Dashboard noch aus.

### A5. Notlösung, falls er dich nicht einladen will

Dann muss **er** die Credentials einmalig ins Projekt legen:

1. Dashboard → Vivox → Credentials kopieren.
2. Im Editor `Project Settings > Vivox` von Automatic auf **Custom / Manual**.
3. Server, Domain, Issuer, Key eintragen.

Der **Token Key ist ein Geheimnis**. Nicht ins öffentliche Repo, nicht in Discord-Screenshots. Besser ist immer die Einladung (Teil A1).

---

# Teil B — Was du im geklonten Projekt machst

Erst wenn der 403 weg ist. Sonst kompiliert der Chat vielleicht, verbindet im Play Mode aber nicht.

## B1. Skripte so ins Hotel-Projekt, dass der Kollege sie nach dem Push hat

In Unity unter `Packages` einen Ordner anzulegen ist **ausgegraut**. Das ist normal. Den Ordner machst du im **Windows-Explorer**, oder du nimmst den einfachen Weg über `Assets` (empfohlen).

`Add package from disk` zeigt nur auf den Ordner auf **deinem** PC. Beim Push bekommt der Kollege die Skripte **nicht**. Deshalb kopierst du den Inhalt in das Hotel-Repo und entfernst danach das Disk-Package.

### Schritt 1 — Ordner in Assets anlegen (in Unity)

1. Unity: links das **Project**-Fenster (Dateien, nicht Hierarchy).
2. Ordner **Assets** einmal anklicken.
3. Rechtsklick in den leeren Bereich → **Create → Folder**.
4. Name: `EarshotProximity` (ein Wort, kein Leerzeichen).

Du hast jetzt `Assets/EarshotProximity`. Der Ordner ist noch leer.

### Schritt 2 — Alles aus ProximityChatExport reinkopieren (Windows-Explorer)

1. Unity kannst du offen lassen.
2. Windows-Explorer öffnen (Win + E).
3. Zu diesem Ordner gehen (Quelle, das Earshot-Repo):

   `<Pfad zum geklonten Earshot-Repo>\ProximityChatExport`

4. **Alles darin markieren** (Strg + A): die `.cs`-Dateien, `Modifiers`, `Examples`, `package.json`, `ANLEITUNG.md`, `Earshot.Proximity.asmdef`, usw. Den äußeren Ordner `ProximityChatExport` selbst nicht extra drumwickeln — der Inhalt soll **direkt** in `EarshotProximity` liegen.
5. Strg + C.
6. Im Explorer hierhin wechseln:

   `<Pfad zu deinem Unity-Projekt>\Assets\EarshotProximity`

7. Strg + V. Unity importiert automatisch (unten rechts Fortschritt).

Danach musst du in Unity unter `Assets/EarshotProximity` Dateien wie `ProxVoice.cs`, `ProxVoicePlayer.cs`, den Ordner `Modifiers` sehen.

### Schritt 3 — Das alte „Add package from disk“ entfernen

Sonst gibt es jede Klasse **zweimal** und Unity spinnt.

1. Unity: **Window → Package Manager**.
2. Oben links das Dropdown (oft „In Project“ oder „Unity Registry“): auf **In Project** stellen. Falls du das Package nicht siehst, **My Registries** und nochmal die Liste durchgehen.
3. Links **Earshot Proximity Chat** (oder `com.earshot.proximity`) anklicken.
4. Unten rechts **Remove**. Warten, bis Unity fertig ist.

Falls Remove fehlt oder das Package danach immer noch da ist:

1. Im Explorer: `<Pfad zu deinem Unity-Projekt>\Packages\manifest.json` mit Editor (Notepad) öffnen.
2. Eine Zeile suchen, in der `earshot` oder `ProximityChatExport` oder `file:` plus dein Pfad steht, z.B.

   `"com.earshot.proximity": "file:<Pfad-zum-Earshot-Repo>/ProximityChatExport"`

3. Diese **eine** Zeile löschen. Komma an der Zeile davor/danach so lassen, dass die JSON-Liste gültig bleibt (kein Doppelkomma, kein Komma direkt vor `}`).
4. Speichern. Zurück zu Unity; es lädt neu.

### Schritt 4 — Prüfen

- Console: **keine** Fehler `The type … already contains a definition` / doppelte Klasse.
- `ProxVoicePlayer` und **Connect In Scene** sind weiter am Prefab bzw. in der Szene.
- Commit + Push **im Ordner deines Spiel-Projekts**. Dann hat der Kollege nach dem Pull `Assets/EarshotProximity`.

Nicht beides behalten: Disk-Package **und** Kopie in Assets. Nach Schritt 3 nur noch Assets.

### Wenn die Console `Unity.Services.Vivox` / `VivoxParticipant` nicht findet

Die Skripte in `Assets` sind jetzt normales Spiel-C#, nicht mehr ein Package. Beim Entfernen des Disk-Pakets nimmt Unity oft **Vivox mit weg**. Zusätzlich stört die Datei `Earshot.Proximity.asmdef` in Assets.

1. **Window → Package Manager** → oben **Unity Registry**.
2. Suche **Vivox** → **Install** (16.x). Warten bis fertig.
3. Im Project-Fenster: `Assets/EarshotProximity/Earshot.Proximity.asmdef` anklicken → Entf / Delete. Die `.meta` dazu darf mit weg. **Nicht** die `.cs` Dateien löschen.
4. Die `package.json` in `Assets/EarshotProximity` kannst du auch löschen, die braucht Assets nicht.
5. Warten bis Unity unten rechts fertig kompiliert.

Danach dürfen die CS0234/CS0246-Fehler weg sein. Wenn noch `already contains a definition` kommt, ist das Disk-Package noch da → Schritt 3 (Remove) wiederholen.

## B2. Packages prüfen

`Window > Package Manager` → Unity Registry. Es müssen da sein:

| Package | Wofür in einfachen Worten |
|---|---|
| **Vivox** (`com.unity.services.vivox`, 16.x) | Transportiert die Stimme übers Internet |
| **Authentication** (`com.unity.services.authentication`) | Gibt jedem Spieler eine ID, ohne die Vivox nicht einloggt |
| **Services Core** | Unterbau, kommt meist automatisch mit |

Wenn das Spiel **schon** Unity Lobby/Relay/Auth nutzt: dieselben Packages, dieselbe Project ID. Nur Vivox dazunehmen, falls es fehlt.

Wenn das Spiel Photon, Steam, Mirror oder etwas Eigenes nutzt: der Multiplayer bleibt. Für Stimme braucht ihr trotzdem **sein** Unity-Cloud-Projekt mit Auth + Vivox. Die Auth-`PlayerId` muss ihr über **seinen** Netzcode zu den Character-Objekten bringen (Abschnitt B6).

## B3. Ein AudioListener, Mikrofon

- In der Spielszene darf **genau ein** `AudioListener` aktiv sein (meist auf der lokalen Kamera). Mehrere gleichzeitig machen 3D-Audio kaputt. Inaktive Listener auf einer ausgeschalteten Lobby-Kamera sind okay.
- Windows: Einstellungen → Datenschutz → Mikrofon → Unity / deine EXE erlauben.
- Android später: Berechtigung `RECORD_AUDIO`. iOS: Microphone Usage Description in den Player Settings.

## B4. Assets anlegen (einmalig, landet im Git)

Unity findet Einstellungen zur Laufzeit nur, wenn sie in einem Ordner namens **`Resources`** liegen. Der Name muss **genau so** heißen.

1. Im Project-Fenster: Rechtsklick in `Assets` → `Create > Folder` → `Resources` (falls es den Ordner noch nicht gibt).
2. Rechtsklick in `Assets/Resources` → `Create > Earshot Proximity > Settings`.
3. Die Datei **genau** `ProxVoiceSettings` nennen (ohne Leerzeichen). Der Code sucht `Resources/ProxVoiceSettings`.

Inspector von `ProxVoiceSettings`:

- **Voice Enabled:** an
- **Voice Profile:** erst anlegen (nächster Schritt), dann hier draufziehen
- **Microphone Muted On Join:** nur an, wenn das Mikrofon erst nach einer Taste loslegen soll
- **Verbose Logging:** an, solange ihr einrichtet

Voice Profile:

1. Irgendwo unter `Assets` (nicht zwingend Resources): `Create > Earshot Proximity > Voice Profile` → z.B. `DefaultVoiceProfile`.
2. Vier Module anlegen und **in die Liste Modifiers** des Profils ziehen (Reihenfolge in der Liste ist egal):

| Create-Menü | Dateiname (Vorschlag) | Was es tut |
|---|---|---|
| `Earshot Proximity/Voice Modifiers/Distance Falloff` | `DistanceFalloff` | weiter weg = leiser |
| `Earshot Proximity/Voice Modifiers/Occlusion` | `Occlusion` | Wand dazwischen = dumpfer |
| `Earshot Proximity/Voice Modifiers/Portal` | `Portal` | Tür auf/zu |
| `Earshot Proximity/Voice Modifiers/Zone` | `Zone` | Raum-Hall |

Profil-Inspector:

- **Max Hearing Distance:** z.B. `25` (Meter). Weiter weg = Stille.
- **Distance Falloff:** Kurve, links laut, rechts 0.
- **Occlusion Layers:** Layer, auf denen **Wände und Böden** liegen. Nicht leer. Den Layer der **Spieler** hier **nicht** ankreuzen, sonst zählt der eigene Körper als Wand.
- **Zone Layers:** Layer der Raum-Trigger, falls ihr `Voice Zone` nutzt.
- **Enable Reverb:** an, wenn Räume hallen sollen.
- **Evaluations Per Second:** 20–30.

Ohne Module im Profil nimmt der Code zur Laufzeit Defaults. Ein Asset ist trotzdem besser, weil ihr Layer und Meter seht.

`DefaultVoiceProfile` auf das Feld **Voice Profile** in `ProxVoiceSettings` ziehen. Speichern. Committen, damit er dieselben Assets hat.

## B5. Character-Prefab: welche Komponente wohin

Ihr ändert **keine** Szene aus diesem Ordner (es gibt keine). Ihr nehmt **seine bestehenden Character-Prefabs**.

### Pflicht auf jedem spielbaren Character (du und die anderen)

1. Prefab öffnen (Doppelklick auf den Character im Project-Fenster).
2. Das **Wurzel-Objekt** anwählen (ganz oben in der Hierarchy des Prefabs, nicht nur den Kopf).
3. `Add Component` → **Prox Voice Player** (`Earshot Proximity/Voice Player`).

Inspector:

- **Voice Anchor:** das Transform am **Kopf oder Mund**. Leer = die Füße/Wurzel, klingt falsch. Hat das Prefab ein Kind `Head` oder `Camera`, wird das oft automatisch genommen. Sonst den Kopf per Hand aus der Hierarchy auf das Feld ziehen.
- **Is Local Player:** **nicht** fest im Prefab anhaken. Das setzt der Code in `Bind`.
- **Player Id:** **nicht** von Hand eintragen.

### Das darfst du überspringen (nicht Pflicht)

**Kamera:** Du musst nichts anfassen. Hören passiert automatisch am Kopf des lokalen Characters, sonst am `AudioListener` der Kamera.

**Türen, Räume, Mute-Taste, Voice Transparent:** Nur Feinschliff. Ohne sie funktioniert Voice trotzdem: näher = lauter, weiter weg = leiser. Wände und Türen klingen dann nicht besonders, das ist okay für den ersten Test.

---

## B6. Zwei Pflicht-Dinge, sonst bleibt es still

Die Komponenten auf dem Character reichen **nicht**. Stell dir Discord vor: der Character hat ein Headset (`ProxVoicePlayer`), aber niemand ist im Channel und niemand hat den Namen am Avatar kleben.

| Schritt | Pflicht? | Was passiert ohne |
|---|---|---|
| `ProxVoicePlayer` auf dem Character (B5) | ja, hast du hoffentlich schon | gar keine Kopplung an den Avatar |
| **Connect** (unten, ein Objekt in der Szene) | **ja** | komplett still, Vivox startet nie |
| **Bind** (Character = diese Stimme) | für den ersten Test zu **zweit** oft noch ohne machbar, danach **ja** | du hörst den anderen evtl. flach/2D oder am falschen Kopf |
| Türen / Zonen / Kamera | nein | nur langweiligerer Klang |

### Pflicht 1 — Connect: ein leeres Objekt in der Spielszene

Kein Code schreiben. Kein fremdes Multiplayer-Skript öffnen.

1. Die **Szene öffnen, in der man tatsächlich rumläuft** (nicht Hauptmenü, nicht Lobby-UI).
2. Hierarchy: Rechtsklick → `Create Empty`. Name z.B. `ProximityVoice`.
3. `Add Component` → **Connect In Scene** (`Earshot Proximity/Connect In Scene`).
4. Inspector: **Channel Name** auf etwas Einfaches setzen, z.B. `test`.
5. **Genau denselben** Text muss bei ihm in seinem Editor stehen. Sonst sitzt ihr in zwei verschiedenen Voice-Räumen und hört euch nicht.
6. Szene speichern.

Wenn dieses Objekt aktiv wird (Szene geladen), tritt jeder Client dem Kanal `test` bei. Wenn die Szene wieder zu ist, gehen sie raus.

Später könnt ihr den Channel Name durch die echte Lobby-Id ersetzen, damit mehrere Matches sich nicht ins Gehege kommen. Für den ersten Test reicht `test`.

### Pflicht 2 — Bind: erst später, oder jetzt wenn Netcode for GameObjects

**Bind** sagt: „Dieser Character in der Welt ist Spieler *xyz* aus Vivox.“ Ohne das weiß Voice nicht, an welchen Kopf sie die Stimme hängen soll.

Beim **ersten Test nur zu zweit** kannst du Bind **erstmal weglassen**. Es gibt einen Notnagel: die Stimme wird an „den anderen Character“ gehängt, wenn genau ein Mitspieler in der Szene ist. Dann solltet ihr euch schon hören, sobald Connect läuft.

Wenn das nicht klappt, oder sobald mehr als zwei Spieler da sind, braucht ihr Bind. Dann so:

**Falls das Spiel Unity Netcode for GameObjects nutzt** (Prefab hat oft `NetworkObject`):

1. Datei `Examples/ProxVoiceNetcodeGlue.cs.txt` kopieren.
2. Ins Spiel legen (z.B. neben die anderen Character-Skripte), Endung in `.cs` ändern.
3. Die Kommentarzeichen `//` vor dem Code **entfernen** (oder den Inhalt aus der Datei in ein neues Skript `ProxVoiceNetcodeGlue` packen).
4. Komponente auf **dasselbe** Character-Prefab legen wie `ProxVoicePlayer`.

**Falls das Spiel Photon / Mirror / etwas Eigenes nutzt:** Bind muss dort rein, wo der Character spawnt — das ist eine Aufgabe für den, der den Multiplayer gebaut hat. Die zwei Aufrufe sind:

```csharp
using Earshot.Proximity;
using Unity.Services.Authentication;

// eigener Character nach Spawn:
GetComponent<ProxVoicePlayer>().Bind(AuthenticationService.Instance.PlayerId, local: true);

// Character eines Mitspielers, sobald seine Auth-ID über das Netz da ist:
GetComponent<ProxVoicePlayer>().Bind(remoteUgsPlayerId, local: false);
```

Die ID ist die Unity-Authentication-`PlayerId`, nicht die Netcode-Nummer.

Stummschalten später: `ProxVoice.ToggleMicrophone()`. Für den ersten Test unnötig.

---

## B7. Checkliste vor dem ersten Test zu zweit

- [ ] 403 weg, Vivox-Felder in Project Settings voll
- [ ] Im Editor mit der **eingeladenen** Unity-E-Mail angemeldet
- [ ] Cloud-Projekt **nicht** auf ein privates Projekt von dir umgestellt
- [ ] `ProxVoiceSettings` in `Assets/Resources/`, Profil mit Occlusion-Layern zugewiesen
- [ ] `ProxVoicePlayer` auf dem Character, Voice Anchor am Kopf
- [ ] Leeres Objekt `ProximityVoice` in der **Spielszene** mit `Connect In Scene`, Channel Name bei allen gleich (z.B. `test`)
- [ ] Bind erst nötig wenn 2-Spieler-Test ohne Bind nicht reicht, oder bei mehr als zwei Spielern
- [ ] Ein aktiver `AudioListener`, Kopfhörer wenn zwei Clients auf einem PC
- [ ] Console: `[Proximity] Sprachkanal betreten` und `Stimme empfangen von …`

Logs: neben dem Projektordner (eine Ebene über `Assets`) liegt `EarshotLogs/voice-….txt`.

---

## B8. Typische Fehler

| Symptom | Bedeutung | Was tun |
|---|---|---|
| `HTTP/1.1 403 Forbidden` / `GetAndSetVivoxCredentials` | Dein Unity-Account darf die Vivox-Schlüssel **seines** Cloud-Projekts nicht lesen | Teil A: einladen + Vivox/Auth an. Falsche E-Mail prüfen. Nicht unlinken |
| Credentials-Felder leer, kein 403 | Editor hat noch nicht gezogen, oder Vivox-Paket frisch | Play/Stop, Editor neu. Dashboard: Vivox wirklich enabled |
| Gar keine Stimme, kein 403 | `ConnectAsync` nie aufgerufen, oder Voice Disabled im Settings-Asset | Console nach `[Proximity]` filtern |
| Nur einer hört den anderen | Der andere ist stumm, oder `Bind` fehlt auf einer Seite | Mikrofon-Taste, Auth-IDs vergleichen (müssen zu Vivox-Teilnehmer-IDs passen) |
| 2 Sekunden Ton, dann tot | Alte Kopie ohne den Loop-Fix | Skripte aus **diesem** Ordner neu übernehmen |
| Stimme bleibt 2D / folgt nicht | `PlayerId` ungleich Vivox-ID | Dieselbe Auth-Id syncen, Anchor setzen |
| Alles klingt „hinter der Wand“ | Occlusion-Layer enthält Spieler oder Riesen-Collider | Layer prüfen, `Voice Transparent` |
| Echo von sich selbst | Zwei Builds, ein Raum, Lautsprecher | Kopfhörer |
| Kurzer Aussetzer nach Gerätewechsel | Vivox startet Audio neu | Gerät nicht jedes Frame setzen |
| `Remoteaudio` als Gerät im Editor | Normal im Multiplayer Play Mode | Im echten Build Default System Device |

---

## API-Kurzreferenz

```csharp
ProxVoice.ConnectAsync(channel, displayName);
ProxVoice.DisconnectAsync();
ProxVoice.IsConnected;
ProxVoice.LocalPlayerId;          // nach Auth, das ist die ID für Bind
ProxVoice.MicrophoneMuted;
ProxVoice.ToggleMicrophone();
ProxVoice.HeardVoiceVolume;       // 0..1, nur fremde Stimmen
ProxVoice.GameVolume;             // AudioListener.volume
ProxVoice.ListenerOverride;       // z.B. Kopf beim Zuschauen nach dem Tod
```

Es gibt **keine** Szene und **kein** Setup-Fenster in diesem Export.
