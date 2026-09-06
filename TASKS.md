# Earshot – Arbeitsauftrag

> Veraltet für den aktuellen Stand. Produkt ist `com.earshot.voice` unter
> `DevProject/Packages/com.earshot.voice/`. Aktueller Plan: `docs/earshot-voice-plan.md`.
> Altes Coop-Paket aus dem eigenen Spiel entfernen: `docs/altes-earshot-entfernen.md`.

Diese Datei ist die Arbeitsanweisung für die Fortsetzung von Earshot. Der Architektur-
Hintergrund steht in `PLAN.md`; hier stehen nur konkrete, abgeschlossene Aufgaben.

**Sprache:** Alle Kommentare, Tooltips und Log-Ausgaben auf Deutsch, ohne Umlaute
(`ae`, `oe`, `ue`, `ss`). Der Code selbst – Klassen, Methoden, Felder – ist englisch.
Das ist im gesamten bestehenden Code so und muss so bleiben.

---

## 1. Ist-Stand

Fertig und kompilierend (Unity 6000.5.7f1, keine Fehler):

| Bereich | Dateien |
|---|---|
| Kern | `Runtime/Core/` – `Coop`, `CoopSettings`, `CoopServices`, `CoopState`, `CoopLog`, `CoopBootstrap` |
| Sitzung | `Runtime/Session/` – `SessionController`, `RelayTransportProvider`, `ITransportProvider`, `SceneCoordinator` |
| Spieler | `Runtime/Player/` – `CoopPlayer`, `PlayerRegistry` |
| Stimme | `Runtime/Voice/` – `CoopVoice`, `VoiceRuntime`, `VoicePipeline`, `VoiceEmitter`, `VivoxVoiceBackend`, `IVoiceBackend`, `VoiceProfile`, `VoiceContext`, `VoiceSample`, `IVoiceModifier`, `VoiceZone`, `VoicePortal` |
| Module | `Runtime/Voice/Modifiers/` – `DistanceFalloffModifier`, `OcclusionModifier`, `PortalModifier`, `ZoneModifier` |
| UI | `Runtime/UI/CoopQuickMenu` |

Noch nicht vorhanden: Spawner, Editor-Werkzeuge, Welt-Bausteine, Tests, Samples.

---

## 2. Leitplanken

Diese Punkte sind bewusst so entschieden und dürfen **nicht** ohne Rückfrage geändert
werden. Wer sie umwirft, bricht Verhalten, das an anderer Stelle vorausgesetzt wird.

1. **Vivox läuft als flacher 2D-Kanal.** Niemals Vivox' eingebaute 3D-Positionierung
   aktivieren. Die gesamte Räumlichkeit entsteht lokal in `VoicePipeline`. Mit Vivox-3D
   würde der Dienst die Lautstärke selbst berechnen und Türen und Wände wirkungslos machen.
2. **`SceneCoordinator` setzt `LoadSceneMode.Additive`.** Ohne das entlädt Netcode beim
   Beitritt die Szenen des Clients und zerstört fremde Projekte. Nicht anfassen.
3. **`VoicePipeline` ist die einzige Stelle mit Raycasts.** Module bekommen fertige Fakten
   im `VoiceContext` und dürfen selbst nicht in die Physik greifen – sonst vervielfacht
   sich die Last pro Modul.
4. **Module verändern nur `VoiceSample`, nie `VoiceContext`.** Der Kontext ist `in`, das
   Sample ist `ref`. Diese Signatur bleibt.
5. **`VoiceEmitter` ist die einzige Stelle, die eine `AudioSource` anfasst.**
6. **Das Tap-GameObject gehört Vivox.** Niemals `Destroy` auf das GameObject eines
   Sprechers, nur auf die eigene `VoiceEmitter`-Komponente.
7. **Keine `async void`-Methoden** außer den bereits vorhandenen Ereignis-Brücken
   (`CoopVoice.Queue`). Alles andere gibt `Task` zurück.
8. **Kein `FindObjectsSortMode`.** Unity 6.5 hat es abgekündigt. Vorhandenes Muster mit
   `#if UNITY_6000_5_OR_NEWER` aus `CoopBootstrap.cs` übernehmen.
9. **Keine Geheimnisse ins Repo.** Nichts an `.gitignore` entfernen. Keine Projekt-IDs,
   Organisations-IDs oder Zugangsdaten in Code, Kommentaren oder Beispielen.
10. **`package.json` deklariert keine Samples, solange die Ordner fehlen.** Der
    Package Manager wirft sonst dauerhaft `DirectoryNotFoundException`.

---

## 3. Aufgaben, in dieser Reihenfolge

Jede Aufgabe ist einzeln abschließbar. Nach jeder Aufgabe: Unity kompilieren lassen und
erst weitermachen, wenn die Konsole fehlerfrei ist.

### A1 – PlayerSpawner

**Datei:** `Runtime/Player/PlayerSpawner.cs`

Netcode spawnt das Player-Prefab von selbst, wenn es im `NetworkManager` hinterlegt ist.
Diese Komponente kümmert sich nur um die Startpositionen.

- `MonoBehaviour`, Menü `Earshot/Player Spawner`.
- Feld `Transform[] spawnPoints` mit Tooltip.
- Verteilt Spieler reihum auf die Punkte, anhand ihrer Reihenfolge in
  `PlayerRegistry.Players`.
- Sind keine Punkte gesetzt: Ring mit 2 m Radius um den Ursprung, plus einmalige
  `CoopLog.Warn`-Meldung.
- Positionierung nur auf dem Host (`NetworkManager.Singleton.IsServer`), sonst
  überschreiben sich Client und Server gegenseitig.
- Hängt sich an `PlayerRegistry.PlayerAdded`.
- Gizmos: nummerierte Kugeln an den Spawnpunkten.

**Fertig, wenn:** Zwei Instanzen im Multiplayer Play Mode an verschiedenen Punkten starten.

---

### A2 – NetworkDoor

**Datei:** `Runtime/World/NetworkDoor.cs`

Die Tür, die den akustischen Effekt sichtbar macht.

- `NetworkBehaviour`, Menü `Earshot/Network Door`.
- `NetworkVariable<bool> isOpen`, schreibbar nur vom Server.
- `ServerRpc` zum Umschalten, von jedem Client aufrufbar.
- Felder: `Transform hinge`, `float openAngle = 90f`, `float openSeconds = 0.6f`.
- Dreht das Scharnier weich über `openSeconds`.
- **Kernpunkt:** Setzt in jedem Frame der Bewegung `VoicePortal.Openness` auf den
  Fortschritt der Animation (0 bis 1). Dadurch wird die Tür akustisch genau dann
  durchlässig, wenn sie sichtbar aufgeht.
- `RequireComponent(typeof(VoicePortal))`.
- Öffentliche Methoden `Open()`, `Close()`, `Toggle()`.

**Fertig, wenn:** Beide Spieler sehen dieselbe Türstellung, und die Stimme dahinter wird
beim Zufallen hörbar dumpf.

---

### A3 – Interaktion

**Dateien:** `Runtime/World/NetworkInteractable.cs`, `Runtime/World/Interactor.cs`

- `NetworkInteractable`: abstrakte Basis mit `abstract void OnInteract(ulong clientId)`
  und einem Feld `string prompt = "Benutzen"`.
- `NetworkDoor` erbt davon und schaltet bei `OnInteract` um.
- `Interactor`: auf dem lokalen Spieler. Raycast nach vorn (Reichweite einstellbar,
  Standard 3 m), meldet das getroffene `NetworkInteractable` über
  `public event Action<NetworkInteractable> Focused` (mit `null`, wenn nichts getroffen
  wird). Kein eigenes UI in dieser Klasse.
- Der Interactor darf **nur** auf dem lokalen Spieler laufen (`IsLocalPlayer` prüfen).

---

### A4 – VoiceDebugOverlay

**Datei:** `Runtime/UI/VoiceDebugOverlay.cs`

Ohne dieses Werkzeug ist jeder Klangfehler blindes Raten.

- IMGUI-Overlay, standardmäßig aus, per Feld einschaltbar.
- Zeigt pro Eintrag aus `VoiceRuntime.Instance.Emitters`: Spielername, Entfernung,
  aktuelle Lautstärke, Tiefpassfrequenz, Hallanteil, ob ein Portal im Weg ist.
- Werte aus `VoiceEmitter.Current` lesen, nichts neu berechnen.
- Zusätzlich eine Zeile mit dem Zustand: Sitzung, Sprachkanal, Mikrofon stumm ja/nein.

---

### A5 – Setup-Wizard

**Datei:** `Editor/EarshotSetupWindow.cs`

Das ist das Herzstück des Plug-and-Play-Versprechens und die **gefährlichste** Aufgabe,
weil sie fremde Projekte verändert. Bitte besonders sorgfältig.

Menüpunkt: `Tools > Earshot > Setup`.

**Zwingende Regeln:**

- **Zweistufig.** Erst „Prüfen", dann eine Liste dessen, was geändert würde, dann erst
  „Anwenden". Niemals sofort schreiben.
- **Nichts überschreiben.** Existiert eine Datei oder Komponente bereits, wird sie in der
  Liste als „vorhanden, bleibt unverändert" geführt.
- **Alles über `Undo`.** Jede Szenenänderung mit `Undo.RegisterCreatedObjectUndo` bzw.
  `Undo.AddComponent`, damit Strg+Z sie zurücknimmt.
- **Keine Layer-, Tag- oder Physics-Änderungen.** Zu invasiv. Stattdessen im Ergebnis
  darauf hinweisen, was der Nutzer selbst einstellen sollte.

Schritte des Wizards:

1. `Assets/Resources/EarshotSettings.asset` anlegen, falls nicht vorhanden.
2. `Assets/Earshot/DefaultVoiceProfile.asset` anlegen, dazu die vier Modifier-Assets
   (`DistanceFalloff`, `Occlusion`, `Portal`, `Zone`) und in das Profil eintragen.
3. `NetworkManager` in der Szene anlegen, falls keiner existiert, mit
   `UnityTransport` als Transport.
4. Feld zur Auswahl des Player-Prefabs. Ausgewähltes Prefab prüfen auf
   `NetworkObject` und `CoopPlayer`; fehlt eines, anbieten es hinzuzufügen.
5. Prefab im `NetworkManager` als Player Prefab eintragen.
6. Abschlussbericht mit allem, was getan wurde, und was noch von Hand nötig ist
   (UGS-Projekt verknüpfen, Vivox aktivieren).

---

### A6 – PreflightValidator

**Datei:** `Editor/EarshotPreflight.cs`

Menüpunkt `Tools > Earshot > Prüfen`. Reine Diagnose, ändert nichts.

Prüft und meldet jeweils mit Klartext-Erklärung, warum es wichtig ist:

- Einstellungs-Asset vorhanden und ein `VoiceProfile` zugewiesen?
- Profil hat Module? (Leeres Profil = nur Entfernung, keine Wände.)
- `OcclusionLayers` nicht leer?
- Genau ein aktiver `AudioListener` in der Szene?
- `NetworkManager` vorhanden, Player-Prefab gesetzt, Prefab hat `CoopPlayer`?
- Projekt mit UGS verknüpft? (`CloudProjectSettings.projectId` nicht leer.)
- Mindestens ein `VoicePortal` ohne Collider? (Häufiger Fehler, Portal wirkt dann nie.)
- `VoiceZone` ohne `isTrigger`?

---

### A7 – EditMode-Tests

**Datei:** `Tests/EditMode/VoiceMathTests.cs`

Nur reine Rechenlogik testen, nichts mit Szene oder Netzwerk.

- `VoiceSample.Clamp` hält alle Werte in ihren Grenzen und korrigiert einen Hochpass
  oberhalb des Tiefpasses.
- `VoiceSample.MoveTowards` mit `t = 1` erreicht das Ziel exakt; mit `t = 0` ändert
  sich nichts.
- Frequenz-Interpolation verhält sich logarithmisch: der Mittelwert zwischen 500 und
  2000 Hz liegt bei etwa 1000 Hz, nicht bei 1250 Hz.
- `VoiceProfile.EvaluateDistanceFalloff` liefert bei Distanz 0 den Wert 1 und bei
  Distanz über der Hörweite den Wert 0.
- Jedes der vier Module: sinnvolle Ober- und Untergrenze prüfen (etwa: geschlossenes
  Portal senkt die Lautstärke, offenes lässt sie unverändert).

---

### A8 – Samples

**Ordner:** `Samples~/QuickStart/`, `Samples~/CustomVoiceModifier/`

Erst anlegen, **dann** den `samples`-Block wieder in `package.json` eintragen und den
Platzhalter `_samplesNote` entfernen. Nicht in der umgekehrten Reihenfolge – siehe
Leitplanke 10.

- **QuickStart:** Zwei Räume, eine Wand dazwischen, eine Tür mit `NetworkDoor` und
  `VoicePortal`, je eine `VoiceZone` pro Raum, ein `CoopQuickMenu` für Host und Join.
  Das ist die kleinste Szene, in der man den Effekt tatsächlich hört.
- **CustomVoiceModifier:** Ein kommentiertes Beispielmodul, etwa ein Funkgerät-Effekt
  (Hochpass, harte Begrenzung, keine Entfernungsdämpfung) mit
  `Order = VoiceModifierOrder.Transmission`.

---

### A9 – Dokumentation

- `README.md` im Wurzelverzeichnis um einen Abschnitt „Installation für Mitspieler"
  ergänzen: Package-Manager, „Add package from git URL", danach `Tools > Earshot > Setup`.
- `CHANGELOG.md` im Paket auf den tatsächlichen Stand bringen.
- In `README.md` das Zuschauer-Thema erwähnen: `CoopVoice.ListenerOverride`.

---

## 4. Prüfung nach jeder Aufgabe

1. Unity in den Vordergrund holen, Kompilierung abwarten.
2. Konsole mit allen drei Filtern prüfen. **Null Fehler, null neue Warnungen.**
3. Bei neuen öffentlichen Klassen: Sind alle Felder mit `[Tooltip]` versehen?
4. Kommentare erklären das *Warum*, nicht das *Was*. Kein Kommentar, der nur die
   darunterstehende Zeile wiederholt.

---

## 5. Wann anhalten und fragen

Nicht raten, sondern die Aufgabe abbrechen und nachfragen, wenn:

- eine Änderung an einer der zehn Leitplanken nötig scheint,
- eine Vivox-, Netcode- oder Multiplayer-Services-API anders heißt als erwartet und
  die richtige Signatur nicht aus der installierten Paketquelle ablesbar ist,
- eine Aufgabe verlangt, bestehende Dateien umzubauen statt zu ergänzen,
- unklar ist, ob eine Änderung fremde Projekte beschädigen könnte.

Lieber eine offene Frage als eine erfundene API.
