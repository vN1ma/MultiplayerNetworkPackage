# Earshot Voice

Eigenstaendiger Proximity Voice Chat fuer Unity 6. Eine Komponente auf den Player:
`EarshotProximityVoice`. Kein Netcode in diesem Paket, kein zweiter Pflichtschritt.

Vivox liefert die Sprachuebertragung als flacher 2D-Kanal, alle raeumlichen Effekte
(Entfernung, Verdeckung durch Waende, Tueren/Portale, Raeume/Zonen) rechnet dieses Paket
lokal in der `VoicePipeline`.

## Installation

Wenn das Spiel noch `com.earshot.coop` hat: zuerst
`docs/altes-earshot-entfernen.md` abarbeiten, nicht beide Pakete parallel.

Package Manager -> "Add package from git URL":

```
https://github.com/vN1ma/MultiplayerNetworkPackage.git?path=/DevProject/Packages/com.earshot.voice
```

Mitspieler brauchen dieses Repo nicht lokal. Nach Aenderungen hier pushen, im Spiel
das Paket updaten und `packages-lock.json` mitcommitten.

## Schnellstart

`EarshotProximityVoice` auf das Player-Prefab ziehen. Das ist der einzige Pflichtschritt.

Die Komponente tritt dem Sprachkanal selbst bei, erkennt lokal/remote an Netcode/Mirror/Photon
und haengt Stimmen an die Avatare. Vivox und Authentication muessen im Cloud-Projekt aktiv sein.

Hoertest: Menue `Earshot Voice / Create Hearing Test Scene`. Die Szene landet
unter `Assets/EarshotHearingTest.unity` (nicht im Paket — Git-Pakete sind
schreibgeschuetzt). Dann Play. WASD, Maus, E Ton, F Tuer.

Optional, nicht auf den Player: `Earshot Voice/Mute Hotkey`. Raeume und Tueren:
`VoiceZone` / `VoicePortal` (Treppe = Kind Stair + Lauflaenge). Menue
`Earshot Voice/Authoring` backt den Graph und zeigt Warnungen. Ohne Zonen gilt
weiter die Sichtlinie.

## Walkie-Talkie (optional)

`EarshotWalkieTalkie` an ein Welt-Objekt. Funkkanal, An/Aus, PTT und Empfang am
Geraet (Half-Duplex, Delay) liegen im Paket. Optik und Tasten (E/G/Q/LMB) baust du
im Spiel — Anleitung: Repo-Root `docs/walkie-talkie-game-integration.md`.
