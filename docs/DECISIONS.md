# Entscheidungen & technische Fakten

Format für jeden Eintrag (siehe `.clinerules/05-knowledge-capture.md`):

```
## [YYYY-MM-DD] Kurztitel
Kontext: woher kam die Info (z. B. "Vivox-Doku zu Fade Models")
Entscheidung/Fakt: ...
Auswirkung: welches Arbeitspaket/welche Datei betroffen ist
```


## [2026-09-06] NGO-ID-Sync-Muster für den Netcode-Adapter
Kontext: Früherer Abspaltungs-Entwurf im (lokalen, gitignored) Ordner `ProximityChatExport/` – Beispiel `ProxVoiceNetcodeGlue`
Entscheidung/Fakt: UGS-Player-ID über eine owner-schreibende `NetworkVariable<FixedString64Bytes>` synchronisieren; Owner bindet beim Spawn mit `AuthenticationService.Instance.PlayerId`, Remotes reagieren auf `OnValueChanged` und binden nach. Einfachstes Muster ganz ohne RPCs.
Auswirkung: Phase 1 / WP „NetcodeVoicePlayer-Adapter" in `com.earshot.coop` – als Referenzmuster übernehmen.

## [2026-09-06] Vivox-403-Onboarding: drei getrennte Berechtigungs-Ebenen
Kontext: `ANLEITUNG.md` im lokalen `ProximityChatExport/` (Integrationsanleitung für ein Setup zu zweit im geklonten Projekt)
Entscheidung/Fakt: GitHub-Zugang, Editor-Login und Unity-Cloud-Projekt sind drei getrennte Berechtigungen; Vivox-Credentials-Fehler (403 Forbidden) hängen allein an Cloud-Projekt-Mitgliedschaft plus aktiviertem Authentication/Vivox im Dashboard, nicht am GitHub-Repo. Anonymous Sign-in genügt, Identity-Provider sind optional.
Auswirkung: Phase 5 / WP „Vivox-Credentials-UX" in `docs/earshot-voice-plan.md` – Doku-/Onboarding-Arbeit darauf stützen; ausführliche Schritt-für-Schritt-Anleitung liegt lokal im Export-Ordner.

## [2026-09-06] 2-Sekunden-Ringbuffer ist kein Delay
Kontext: Discord-How-to "Proximity Voice Chat zu deinem Spiel hinzufügen" (Game-SDK-Beispiel)
Entscheidung/Fakt: Ein 2 s großer Ringbuffer zwischen Audio-Thread und Unity-Audio-Thread ist eine Obergrenze gegen Timing-Jitter, kein Wartezustand — korrekt gelesen wird sofort abgespielt. Ein echtes 2 s Delay deutet auf falsche Write-/Read-Position-Behandlung oder Warten auf einen gefüllten Puffer hin.
Auswirkung: Phase 1 / Bug 1 — bei der Delay-Suche die eigene Puffer-/Polling-/Wartelisten-Logik inspizieren, nicht von der Vivox-/SDK-Buffergröße als Ursache ausgehen.

## [2026-09-06] Vivox-Kanal bleibt flach 2D, kein natives 3D-Positional
Kontext: Chat-Analyse zu Vivox-Fähigkeiten (native 3D positional, Conversational/Audible Distance, Fade Model) vs. ROADMAP-Leitplatte 1
Entscheidung/Fakt: Vivox kann Distanz/Richtung nativ, aber Occlusion, Portale und der Raum-Graph brauchen lokale Kontrolle über die VoicePipeline — beides parallel würde doppelt dämpfen. Earshot nutzt Vivox weiter als flachen 2D-Kanal; Räumlichkeit entsteht ausschließlich lokal. Vivox-Positional kommt höchstens später als Culling-Vorfilter in Betracht (ROADMAP v1.5).
Auswirkung: Modifier-Design in `com.earshot.voice`; Basis-Distanz-Falloff bleibt bewusst eigene Logik, wird aber nicht um Vivox-Positional ergänzt.

## [2026-09-06] Walkie-Talkie V1: Funk ersetzt die Mund-Stimme
Kontext: Brainstorming im Projektchat zu Übertragungsgeräten (Walkie-Talkie als Welt-Objekt)
Entscheidung/Fakt: Solange ein Spieler über ein Walkie funkt, wird seine Stimme bei allen Empfängern am eigenen Gerät abgespielt (blechernes EQ-Band, Position = Geräte-Anchor) und die Mund-Stimme gedämpft — nicht beides parallel. Bewusste V1-Vereinfachung gegen das Vivox-Risiko von Mehrfach-Taps pro Sprecher. Später evtl. Verfeinerung Richtung dominanter Pfad mit Überblendung.
Auswirkung: Künftige Walkie-/Übertragungsgeräte-Phase in `docs/earshot-voice-plan.md`; Vivox-Taps bleiben 1 : 1 pro Sprecher.

## [2026-09-06] Trennung Voice-Transport ↔ Multiplayer extern bestätigt
Kontext: Discord-How-to beschreibt für Produktion exakt dieses Muster: Voice-Netz liefert Audio pro Spieler-ID, das Spiel mappt es auf die Player-Objekte, Unity macht Spatial Audio.
Entscheidung/Fakt: Das `IProximityVoicePlayer`-Konzept aus Phase 1 entspricht dem von Discord für Produktion empfohlenen Ansatz (Player-Lifecycle gehört dem Multiplayer, die Voice-Schicht registriert nur Player). Kein Alternativ-Design nötig.
Auswirkung: Bestätigt die Phase-1-Architektur in `docs/earshot-voice-plan.md`; kein Umbau des Plans daraus.

