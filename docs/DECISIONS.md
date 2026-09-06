# Entscheidungen & technische Fakten

Format für jeden Eintrag (siehe `.clinerules/05-knowledge-capture.md`):

```
## [YYYY-MM-DD] Kurztitel
Kontext: woher kam die Info (z. B. "Vivox-Doku zu Fade Models")
Entscheidung/Fakt: ...
Auswirkung: welches Arbeitspaket/welche Datei betroffen ist
```


## [2026-09-06] Hoertest-Ton ist Testaudio, E ist Pause
Kontext: Nutzer legt `Testaudio.mp3` ins Repo-Root und will den Clip an den Emittern, ohne bei jedem E von vorn.
Entscheidung/Fakt: Der Clip liegt als Resources-Asset im Paket. E pausiert/unpausiert; Neustart nur nach Ende. Synthetischer Loop bleibt Fallback.
Auswirkung: ~21 MB im Paket. Nach Package-Update Hoertest neu erzeugen oder einfach Play.

## [2026-09-06] Klangfarbe ist optional an der Quelle
Kontext: Nutzer will Dumpf/Hall am angeklickten Test-Emitter einstellen, und dasselbe an alles haengen was Ton spielt.
Entscheidung/Fakt: `VoiceSourceColor` ist optional (wie Zone/Portal), nicht am Player Pflicht. Testlautsprecher bekommen sie automatisch. Wirkt nach der Welt-Pipeline; an einer normalen AudioSource faerbt sie den Clip direkt.
Auswirkung: Hoertest-Bloecke im Inspector justierbar. Keine zweite Pflichtkomponente auf dem Player.

## [2026-09-06] Hoerregler leben am Player, nicht im Pflicht-Asset
Kontext: Nutzer erwartet Distanz, Dumpf, Hall und Cutoff-Glaettung am Player-Prefab, nicht versteckt in Modifier-Assets.
Entscheidung/Fakt: `VoiceHearingTuning` liegt an `EarshotProximityVoice`. Der lokale Spieler schreibt die Werte zur Laufzeit ins Profil und die Standard-Module. Ein Voice-Profile-Asset bleibt optional (Advanced).
Auswirkung: Keine zweite Pflichtkomponente. Paket im Spiel nach Push updaten, Prefab Inspector neu ansehen.

## [2026-09-06] Hoertest-Szene gehoert nach Assets
Kontext: Unity-Fehler "It is not allowed to open a scene in a read-only package" und Warnung zu immutable packages, als die Szene unter `Packages/com.earshot.voice/Scenes` erzeugt wurde.
Entscheidung/Fakt: Git-URL-Pakete sind schreibgeschuetzt. Erzeugte Szenen liegen im Spiel unter `Assets/`.
Auswirkung: `HearingTestSceneBuilder` speichert nach `Assets/EarshotHearingTest.unity`.

## [2026-09-06] Alles laeuft ueber EarshotProximityVoice
Kontext: Nutzer will keinen Spielcode fuer Connect/Bind. Leitprinzip: eine Komponente.
Entscheidung/Fakt: Die Komponente verbindet selbst, liest Besitz per Reflection (kein Netcode in der asmdef) und ordnet Stimmen zu. Zwei Spieler ohne synchronisierte UGS-ID nutzen den bestehenden 1:1-Fallback in `VoiceRuntime`.
Auswirkung: Kein Pflicht-Aufruf mehr in Lobby-/Spawn-Code. Paket im Spiel nach Push updaten.

## [2026-09-06] Spiel-Team holt com.earshot.voice per Git-URL (Weg A)
Kontext: Mehrere Leute arbeiten am Spiel über Git. Add-package-from-disk zeigt auf einen lokalen Pfad — Mitspieler hätten das Paket nicht, Änderungen im Package-Cache wären verloren.
Entscheidung/Fakt: Dieses Repo bleibt die Quelle. Das Spiel trägt in `Packages/manifest.json` die Git-URL mit `?path=/DevProject/Packages/com.earshot.voice` ein. Mitspieler brauchen nur das Spiel-Repo. Nach jeder Paket-Änderung hier: commit + push, im Spiel Package Manager Update, neuen `packages-lock.json`-Hash mitcommitten.
Auswirkung: Install-Anleitung in `docs/altes-earshot-entfernen.md` Abschnitt 8 und README; Disk-Pfad nur noch zum lokalen Alleine-Test vor dem ersten Push.

## [2026-09-06] Multiplayer fliegt aus diesem Repo, Spiel räumt der Nutzer selbst
Kontext: Nutzer will das alte Earshot zuerst selbst aus dem eigenen Unity-Spiel entfernen und danach nur `com.earshot.voice` einfügen. DevProject soll das Voice-Testprojekt bleiben, ohne `com.earshot.coop`.
Entscheidung/Fakt: `com.earshot.coop` und die Netcode-/MPPM-Abhängigkeiten sind aus diesem Repo gelöscht. Der geplante `NetcodeVoicePlayer`-Adapter entfällt. Phase 2 ist die Nutzer-Anleitung `docs/altes-earshot-entfernen.md` (erst altes Paket restlos weg, dann neues einbinden — keine Übergangsphase mit beiden Paketen).
Auswirkung: Kein Multiplayer-Code mehr unter `DevProject/Packages/`. Install-URL zeigt auf `com.earshot.voice`.

## [2026-09-06] ProximityChatExport ist die neueste Voice-Iteration
Kontext: Zeitstempel- und Inhaltsvergleich Export-Ordner vs. DevProject (21.08. 23:32–23:34 vs. 23:22; `ProxVoice.cs` zuletzt am 06.09. bearbeitet), bestätigt durch Nutzer
Entscheidung/Fakt: `ProximityChatExport/` enthält die zuletzt bearbeitete Standalone-Fassung der Voice-Schicht ohne Netcode-Abhängigkeit (statische `ProxVoice`-Fassade, `ProxVoiceRoster`/`ProxVoicePlayer`, erweitertes Session-Logging). `DevProject/Packages/com.earshot.coop/Runtime/Voice/` ist der ältere Stand und bleibt Referenz für die Test-Werkzeuge (`VoiceTestSpeaker`, `VoiceSessionRecorder`, `MppmDuoTester`).
Auswirkung: Phase 1 der Voice-Abkopplung baut auf dem Export-Stand auf (u. a. `Earshot.Proximity` → `Earshot.Voice` umbenennen, `ProxVoiceRoster` → generisches `IProximityVoicePlayer`-Register), nicht auf dem DevProject-Voice-Ordner.

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

