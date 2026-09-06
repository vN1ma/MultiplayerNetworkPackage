# Earshot Voice

Eigenstaendiger Proximity Voice Chat fuer Unity 6. Dieses Paket kennt kein Netcode und
keinen bestimmten Multiplayer-Dienst - es kennt nur `IProximityVoicePlayer`. Wer sein
eigenes Spiel anbindet, tut das entweder ueber die mitgelieferte
`EarshotProximityVoice`-Komponente direkt (Zero-Config) oder ueber einen kleinen Adapter,
der `IProximityVoicePlayer` fuer das eigene Netzwerk-Framework implementiert (Advanced).

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

1. `EarshotProximityVoice` auf das Player-Prefab ziehen. Das ist der einzige Pflichtschritt.
2. `EarshotVoiceSettings`-Asset unter `Assets/Resources/EarshotVoiceSettings.asset` anlegen
   (Create > Earshot Voice > Settings) und ein `VoiceProfile` zuweisen.
3. `EarshotVoice.ConnectAsync(channelName, displayName)` aufrufen, sobald alle Spieler im
   selben Match sind (z.B. beim Sitzungsstart). `channelName` muss bei allen identisch sein.

Ohne eigenes Multiplayer-Framework reicht das. Mit einem: einen Adapter schreiben, der
`IProximityVoicePlayer` implementiert (oder `EarshotProximityVoice.Bind(...)` aufruft),
sobald die Spieler-Identitaet bekannt ist.

Optional, nicht auf den Player: `Earshot Voice/Connect In Scene` verbindet eine Testszene
ohne eigenes Sitzungs-Skript. `Earshot Voice/Mute Hotkey` schaltet das Mikrofon um.
