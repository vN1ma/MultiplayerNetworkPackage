---
description: Teststandards – nur aktiv bei Testdateien
paths:
  - "**/*Tests.cs"
  - "**/Tests/**"
---

# Tests

- Für jeden neuen Modifier bzw. jede neue Pipeline-Stufe: EditMode-Test ohne Netzwerkabhängigkeit.
- Für Bugfixes (z. B. Delay- oder Modifier-Bug): zuerst einen fehlschlagenden Test schreiben, der den Bug reproduziert, dann fixen. Kein Fix ohne reproduzierenden Test davor.
