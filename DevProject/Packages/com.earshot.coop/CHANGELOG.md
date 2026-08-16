# Changelog

Alle nennenswerten Aenderungen an Earshot werden hier festgehalten.
Das Format orientiert sich an [Keep a Changelog](https://keepachangelog.com/de/1.1.0/),
die Versionierung an [Semantic Versioning](https://semver.org/lang/de/).

## [Unreleased]

### Hinzugefuegt

- Lokaler Testraum ohne Unity-Account: `Coop.HostLocalAsync` / `JoinLocalAsync`,
  kuenstliche Teststimme hinter der Tuer, Play startet die Sitzung von selbst

### Geplant fuer 1.1.0

- Raum-Portal-Graph fuer realistische Schallausbreitung: Schall findet den Weg durch
  verbundene Raeume statt nur die direkte Sichtlinie zu pruefen. Behebt, dass jemand um
  eine Flurecke wie hinter einer massiven Wand klingt.
- Mehrfach-Raycasts fuer teilweise Verdeckung
- Richtungskorrektur: Stimme kommt aus Richtung der offenen Tuer
- Hall-Presets pro Raumtyp

## [0.1.0] - 2026-08-14

### Hinzugefuegt

- Sitzung: Host / Join per Code ueber Unity Relay, `Coop` als oeffentliche Fassade
- Spieler: `CoopPlayer`, `PlayerRegistry`, `PlayerSpawner`
- Stimme: Vivox als flacher 2D-Kanal, Audio Taps, `VoicePipeline` mit Distanz, Occlusion,
  Portal und Zone
- `CoopVoice.ListenerOverride` fuer Zuschauerkameras nach dem Tod
- Welt: `NetworkInteractable`, `NetworkDoor`, `Interactor`
- Editor: `Tools > Earshot > Setup` (zweistufig, ohne Ueberschreiben) und
  `Tools > Earshot > Pruefen`
- Debug: `CoopQuickMenu`, `VoiceDebugOverlay`
- Samples: Quick Start (zwei Raeume, eine Tuer) und Radio-Modifier
- EditMode-Tests fuer die Pipeline-Mathematik
