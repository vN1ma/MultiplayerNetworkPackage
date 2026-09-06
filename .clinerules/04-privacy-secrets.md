---
description: Datenschutz & Secrets – immer aktiv, kritisch, gilt für JEDE Datei
---

# Keine persönlichen/lokalen Daten im Repo

Dieses Repo ist öffentlich. Vor jedem `git add`/Commit-Vorschlag prüfen:

- Keine absoluten lokalen Pfade (z. B. `C:\Users\...`, `/home/...`) in Code, Kommentaren, Doku oder Configs. Relative Pfade oder Platzhalter verwenden.
- Keine echten Vivox-/UGS-/API-Keys, Tokens, Projekt-IDs, Client-Secrets im Code oder in Beispiel-Configs. Diese gehören in `.env`/lokale Config-Dateien, die über `.gitignore` ausgeschlossen sind — im Repo nur Platzhalter wie `<your-vivox-token>`.
- Kein Rechnername, Benutzername, Uni-/Firmen-interne Namen in Kommentaren, Logs oder Beispieldaten.
- Wenn der Nutzer Inhalte aus Browser/anderen Quellen reinkopiert, die persönliche Daten enthalten könnten (E-Mail-Adressen, Namen, Screenshots mit sichtbarem lokalen Pfad): nur die technisch relevante Information übernehmen, den Rest nicht in Doku oder Code zitieren.
- Vor jedem Commit-Vorschlag kurz benennen, was committet wird. Bei Unsicherheit über Sensibilität eines Inhalts: nachfragen statt selbst zu entscheiden.

## .gitignore muss mindestens enthalten
`*.env`, alle Vivox-/UGS-Config-Dateien mit echten Credentials, `.vs/`, `*.csproj.user`, alles unter `docs/local/` (Ablage für rein lokale Notizen, die nie committet werden).
