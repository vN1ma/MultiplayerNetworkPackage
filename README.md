# Earshot Voice

Eigenstaendiger **Proximity Voice Chat** fuer **Unity 6**. Kein Netcode, kein Relay,
kein Host/Join — nur Stimme.

Vivox uebertraegt Audio als flachen 2D-Kanal. **Entfernung, Waende, Tueren und Raeume**
rechnet Earshot lokal.

Paket-ID: `com.earshot.voice` · Namespace: `Earshot.Voice` · Unity: **6000.0 oder neuer**

Dieses Repo ist das **DevProject**: Unity-Testprojekt plus das Paket unter
`DevProject/Packages/com.earshot.voice/`.

---

## Altes Earshot (`com.earshot.coop`) zuerst entfernen

Wenn dein Spiel noch das kombinierte Multiplayer+Voice-Paket hat: **nicht** das neue
Paket daneben installieren. Erst restlos entfernen, dann neu einbinden.

Schritt-fuer-Schritt: **[docs/altes-earshot-entfernen.md](docs/altes-earshot-entfernen.md)**

---

## Neues Paket einbinden (Team)

Nur wenn die Anleitung oben durch ist (oder das Spiel nie `com.earshot.coop` hatte).

Git muss auf jedem Rechner installiert sein. Im **Spielprojekt**:

`Window > Package Manager` → `+` → **Add package from git URL**:

```
https://github.com/vN1ma/MultiplayerNetworkPackage.git?path=/DevProject/Packages/com.earshot.voice
```

Mitspieler brauchen dieses Repo nicht lokal. Nach einer Aenderung hier: pushen,
im Spiel das Paket updaten und `packages-lock.json` mitcommitten.

Add package from disk nur zum Alleine-Test, bevor der Stand auf GitHub liegt.

### Pflicht

1. `EarshotProximityVoice` auf jeden spielbaren Charakter (lokal und remote).
2. `EarshotVoice.ConnectAsync(kanalname, anzeigename)` aufrufen, sobald alle im
   selben Match sind. `kanalname` muss bei allen identisch sein.

```csharp
using Earshot.Voice;

await EarshotVoice.ConnectAsync("match-42", "Anna");
EarshotVoice.ToggleMicrophone();
```

Optional: `Create > Earshot Voice > Settings` als
`Assets/Resources/EarshotVoiceSettings.asset`, plus ein Voice Profile.

Vivox und Unity Authentication muessen im Cloud-Projekt des Spiels aktiv sein
(`Edit > Project Settings > Services`).

---

## Dieses Repo in Unity oeffnen

`DevProject` in Unity **6000.x** oeffnen. Beim ersten Start erzeugt Unity
`packages-lock.json` und `Library/` neu.

---

## Weiteres

- Plan: [docs/earshot-voice-plan.md](docs/earshot-voice-plan.md)
- Fortschritt: [docs/PROGRESS.md](docs/PROGRESS.md)
- Altes Paket entfernen: [docs/altes-earshot-entfernen.md](docs/altes-earshot-entfernen.md)

MIT — [LICENSE.md](LICENSE.md)
