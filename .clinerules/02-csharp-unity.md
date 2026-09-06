---
description: C#/Unity Codestandards – nur aktiv bei .cs-Dateien
paths:
  - "**/*.cs"
---

# C#/Unity Standards

- Namespace-Konvention strikt einhalten: `Earshot.Voice.*` für alles im neuen `com.earshot.voice`-Paket, `Earshot.Coop.*` für den bestehenden Multiplayer-/Netcode-Adapter. Niemals mischen.
- `com.earshot.voice` darf keine harte Abhängigkeit zu `Unity.Netcode` oder anderen Multiplayer-Assemblies haben — das ist der Kernzweck der Abkopplung, nicht versehentlich wieder reinziehen.
- Öffentliche API-Oberfläche minimal halten. Alles, was nicht Teil der Nutzer-facing API ist: `internal`.
- Neue Inspector-Felder/Optionen: Default-Wert ist immer der Zero-Config-Fall, niemals ein Wert, der erst manuell korrekt gesetzt werden muss.
- Public-Klassen/Methoden in `com.earshot.voice` bekommen XML-Doc-Kommentare.
- Kein `Debug.Log`-Spam im Regelbetrieb — Logging hinter einem `EarshotDebug`-Flag.
