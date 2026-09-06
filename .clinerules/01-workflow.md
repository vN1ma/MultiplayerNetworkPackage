---
description: Wie an diesem Projekt gearbeitet wird – immer aktiv
---

# Arbeitsweise

- Nur am aktuell aktiven Arbeitspaket arbeiten (siehe `docs/PROGRESS.md`).
- Vor größeren Änderungen den betroffenen Abschnitt aus `docs/earshot-voice-plan.md` lesen — nicht raten, nicht die ganze Datei zusammenfassen.
- Keine Refactorings "nebenbei", die nicht Teil der aktuellen Aufgabe sind. Separat als Vorschlag nennen, nicht einfach umsetzen.
- Bei strukturellen Entscheidungen (neue Ordnerstruktur, neue Abhängigkeit, Breaking Change an der öffentlichen API) erst kurz zusammenfassen und nachfragen, bevor Code geschrieben wird.
- Kein Code, der das Leitprinzip aus `00-project.md` verletzt (eine Komponente, Zero-Config als Standard).

## Bei jeder abgeschlossenen Änderung

1. Zugehörigen Checkbox-Eintrag in `docs/PROGRESS.md` abhaken.
2. Eintrag in `CHANGELOG.md` ergänzen (neueste Einträge oben):

```
## [YYYY-MM-DD] – Kurztitel
- Was geändert wurde (1–3 Sätze)
- Warum (falls nicht offensichtlich aus dem Titel)
- Betroffene Dateien/Ordner
```

Kein Rumlabern, kein "verbessert die Codequalität" ohne konkrete Aussage.
