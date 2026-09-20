# Debug-Tastenbelegung (F-Tasten)

> Zentrale Belegungstabelle aller F-Tasten im Earshot-Voice-Package und im HOTEL_GAME.
> Führe jede Änderung hier nach, damit keine Taste doppelt belegt wird.
> Stand: 2026-09-20 (v17).

## Belegungstabelle

| Taste | Funktion | Komponente / Ort | Status |
|---|---|---|---|
| **F7** | **Voice Graph Debug HUD** ein-/ausblenden (unten rechts: eigene Zone/Position, Schallweg zu Kollegen & Walkies mit Tür-Offenheiten, Luftlinie vs. Laufweg) | `VoiceGraphDebugHUD` (automatisch am „Earshot Voice Runtime"-Objekt) | **AKTIV** (Default) |
| F6 | **ENTFALLEN (v18):** Der Funk-Sendemodus-Wechsel Single↔ALL ist obsolet — es gibt keinen zweiten Vivox-Sendekanal mehr, Funk läuft über den Proximity-Kanal (siehe CHANGELOG v18.0). | — | frei |
| F8 | Vivox-Ausgabe auf ein anderes physisches Gerät umleiten (Leak-Hunt: trennt Vivox-native vom Unity-Mix) | `WalkieSidetoneCapture` | AUS (default, siehe unten) |
| F9 | Tap-Hard-Mute (Not-Killswitch der Direktausgabe) | `WalkieSidetoneCapture` | AUS |
| F10 | Tap-Volume Legacy-Modus volume=1 (Gegenprobe „Leak war die Direktausgabe") | `WalkieSidetoneCapture` | AUS |
| F11 | Sidetone-Datenfluss zu den Walkie-Geräten kappen | `WalkieSidetoneCapture` | AUS |
| F12 | Unity-Gesamtausgabe stumm schalten (AudioListener-Master; greift seit v16.4 auch für Walkies) | `WalkieSidetoneCapture` | AUS |
| F1–F5, F13+ | frei | — | frei |

Im HOTEL_GAME selbst sind **keine** F-Tasten belegt (Stand 2026-09-19, `Assets/`-Durchsuchung).

## Leak-Hunt-Hotkeys (F7–F12) wieder aktivieren

Die Diagnose-Hotkeys aus der Leak-Jagd (v10–v16.4) sind seit v16.5 **per Default deaktiviert**,
weil der Root-Cause gefixt und im Spiel bestätigt ist und F7 jetzt das Graph-HUD belegt.

Aktivieren (nur während Play):

1. ImHierarchy/Scene das Objekt **„Earshot Voice Runtime"** suchen (wird zur Laufzeit erzeugt, `DontDestroyOnLoad`).
2. Komponente **Walkie Sidetone Capture** anklicken.
3. Haken **„Diagnostic Hotkeys Enabled"** setzen.

Beim Wegnehmen des Hakens werden die aktiven Leak-Hunt-Diagnose-Zustände automatisch zurückgesetzt
(F8-Gerätewechsel, F9-Mute, F10-Volume, F11-Feed-Block, F12-Master) — es bleibt nichts verstellt.
**F6 ist seit v18 frei:** Der ehemalige Sendemodus-Wechsel ist obsolet (kein zweiter Sendekanal
mehr, siehe CHANGELOG v18.0 / debug-history Abschnitt 21).

**Achtung F7-Konflikt:** Sind die Leak-Hunt-Hotkeys aktiv, feuern F7 doppelt (HUD-Toggle UND
Vivox-native-Mute). In dem Fall am „Earshot Voice Runtime"-Objekt in der Komponente
**Voice Graph Debug HUD** die Toggle-Taste umstellen — oder die Leak-Hunt-Keys aus lassen.

## Historie der Belegung

- v10–v16.4: F7–F12 durchgehend Leak-Hunt-Diagnose (F7 zuletzt Vivox-native-Mute-Gegenprobe).
- v16.5 (2026-09-19): Leak-Hunt-Keys default AUS; **F7 = Voice Graph Debug HUD** (neu).
  Vollständige Diagnose-Historie: `walkie-talkie-debug-history.md`.
