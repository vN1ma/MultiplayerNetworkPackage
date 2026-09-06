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

Hoertest: Menue `Earshot Voice / Create Hearing Test Scene` oder die Szene
`Scenes/HearingTest.unity` oeffnen und Play. WASD, Maus, E Ton, F Tuer.

Optional, nicht auf den Player: `Earshot Voice/Mute Hotkey`. Raeume und Tueren:
`VoiceZone` / `VoicePortal` (Treppe = Kind Stair + Lauflaenge). Menue
`Earshot Voice/Authoring` backt den Graph und zeigt Warnungen. Ohne Zonen gilt
weiter die Sichtlinie.
