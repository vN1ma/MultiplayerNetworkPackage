---
description: Wie externer Kontext (Browser, Docs, Fehlermeldungen) persistiert wird – immer aktiv
---

# Externen Kontext festhalten statt verlieren

Wenn der Nutzer Inhalte einfügt (Browser-Ausschnitt, Doku-Zitat, Fehlermeldung, Forenpost) und daraus eine Entscheidung, ein Fakt oder eine Einschränkung folgt, die für spätere Arbeitspakete relevant bleibt:

1. NICHT den eingefügten Text selbst dokumentieren (unnötiger Kontext-Ballast, möglicherweise fremdes Copyright).
2. NUR das destillierte Ergebnis festhalten — die Entscheidung/den Fakt in 1–3 eigenen Sätzen.
3. Zielort je nach Tragweite:
   - Betrifft es Reihenfolge oder Umfang eines Arbeitspakets → im passenden Abschnitt von `docs/earshot-voice-plan.md` ergänzen (nach Rückfrage, siehe `01-workflow.md`).
   - Ist es ein kleinerer technischer Fakt, der den Plan nicht verändert (z. B. "Vivox Fade Model X verhält sich wie Y") → Eintrag in `docs/DECISIONS.md`.
   - Ist es nur für die aktuelle Aufgabe relevant, ohne Bedeutung danach (z. B. ein einmaliger Tippfehler-Hinweis) → nicht dokumentieren.

Im Zweifel: lieber kurz in `docs/DECISIONS.md` festhalten, als riskieren, dass die Information beim nächsten Task-Neustart verloren ist.

## Format für docs/DECISIONS.md
```
## [YYYY-MM-DD] Kurztitel
Kontext: woher kam die Info (z. B. "Vivox-Doku zu Fade Models")
Entscheidung/Fakt: ...
Auswirkung: welches Arbeitspaket/welche Datei betroffen ist
```
