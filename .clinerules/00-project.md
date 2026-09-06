---
description: Kernkontext zu Earshot – immer aktiv, bewusst kurz halten
---

# Projekt: Earshot Proximity Voice

Earshot ist ein Unity-Package für räumlichen Proximity-Voice-Chat (Vivox-Backend).
Aktuell laufender Umbau: Abkopplung von eigenem Multiplayer zu einem eigenständigen Voice-Paket.

Quelle der Wahrheit für Reihenfolge und Arbeitspakete: `docs/earshot-voice-plan.md`.
Nicht selbst neu zusammenfassen, umsortieren oder Phasen vorgreifen — bei Bedarf gezielt den relevanten Abschnitt öffnen.

## Nicht verhandelbares Leitprinzip

Wer Earshot nutzt, zieht **eine Komponente** (`EarshotProximityVoice`) auf den Player. Fertig.

Jede Änderung, die einen zweiten Pflichtschritt für den Nutzer einführt (manuelle IDs setzen, zweite Komponente, Netzwerk-Code verkabeln), ist falsch — es sei denn, sie landet im optionalen Advanced-Modus, nicht im Standardweg.

## Aktuelle Phase

Steht in `docs/PROGRESS.md`, Abschnitt "Aktuell". Vor jeder Aufgabe dort nachsehen. Nicht an Arbeitspaketen späterer Phasen arbeiten, auch wenn es naheliegend erscheint.
