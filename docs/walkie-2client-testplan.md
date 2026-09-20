# Walkie 2-Client-Lokaltest (remote-hunt v16.9)

> Zweck: Den Remote-Funk-Pfad erstmals unter voller Log-Sicht auf BEIDEN Seiten testen — auf einem
> Rechner, ohne Freund. Hintergrund: Debug-Historie Abschnitt 19 (Freund-Session 20260920-005940
> bewies: Gasts-Sendung kam nie im Funkkanal an; bis v16.9 waren alle drei Failure-Punkte der
> TX-Kette stumm).

## Voraussetzungen

1. **Package-Stand:** `com.earshot.voice` mit `revision='remote-hunt-v16.9'` (Log-Zeilen von Taps/
   FLOW/RX tragen den Revisions-String — ältere Zeilen ohne ihn sind ein altes Package).
2. **Zwei getrennte Prozesse** (NICHT Multiplayer Play Mode / Virtual Players — ein Prozess
   unterstützt nur EINEN VivoxService):
   - **A (Host):** Unity-Editor, Szene `TRIALITY_HOTEL`, Host starten.
   - **B (Client):** Windows-Build des Projekts (Build Settings → Build), danach im Build als
     Client per localhost joinen.
3. **Getrennte Vivox-Identitäten sind automatisch gegeben:** `EarshotVoice.LocalPlayerId` kommt aus
   Unity Authentication (anonym, pro Prozess eigener Account) — Code-Beweis `EarshotVoice.cs`.
4. **Mikrofon:** Editor nutzt das Default-Mikro; für den Build reicht dasselbe (Funkkanal-Echo des
   eigenen Mikros erwartet man nicht — Vivox spiegelt eigene Sendungen nicht zurück). Alternative:
   im Build ein zweites/Virtuell-Mikro wählen, um Verwechslungen auszuschließen.
5. Beide Logs laufen automatisch: `HOTEL_GAME/EarshotLogs/voice-<timestamp>.txt` pro Prozess.

## Szenarien-Checkliste

Pro Szenario: 10 s sprechen, dann 5 s Stille. Danach beide Logs prüfen.

| # | Szenario | Aktion | Erwartung (Ohr) | Erwartung (Log, Empfänger) |
|---|---|---|---|---|
| 1 | Proximity-Basis | Beide ohne Walkie in der Hand, 5 m Abstand, normal reden | Normale Mund-Stimme, 3D | Prox-Taps liefern (`outPeak>0`), keine Funk-Zeilen |
| 2 | Kernfall: A funkt | A hebt Walkie, hält LMB (PTT), spricht; B hält sein Walkie, hört nur | B hört A am eigenen Walkie mit Funk-Effekt (3D, näher = lauter) | `TAP Funk an: <A>`, `WALKIE OUTPUT AN … mode=REMOTE … reason=AUDIBLE`, `funkRx>0` (Flanken-Zeile `WALKIE VIVOX RX (Wechsel)`) |
| 3 | Abwechselnd | A und B sprechen zeitversetzt je 5 s in die Walkies | Jeder hört den anderen | Wechselnde Winner; `OUTPUT AN mode=REMOTE` auf beiden |
| 4 | Gleichzeitig (Design-Check) | Beide halten gleichzeitig PTT und sprechen | STILL (korrekt nach Design: Half-Duplex + „Funk ersetzt Mund") | `NO_REMOTE_WINNER` während eigenenem PTT — erwartungsgemäß, KEIN Bug |
| 5 | Distanz | Szenario 2, aber B >8 m von SEINEM Walkie weggeht (Walkie ablegen) | Funkton wird leiser bis stumm (>8 m) | `reason=OUT_OF_RANGE`, `target→0` |
| 6 | „Sidetone für alle" | Szenario 2, aber B stellt sich OHNE Walkie neben A (≤3 m) | B hört As Stimme aus As Walkie-Gerät (3D, gedämpft), As Mund-Stimme gedämpft (`MouthVolumeScale`) | `OUTPUT AN` an As-Gerät auf Bs Client; Mund-Dämpfung im Prox-Emitter |

## Auswertung: Fällt Szenario 2 aus

Das **Sender-Log (A)** zeigt jetzt garantiert die Bruchstelle — genau eine dieser Zeilen fehlt dann
auf dem Weg von `WALKIE PTT an` bis zur hörbaren Sendung:

| Fehlende / auftretende Zeile im Sender-Log | Diagnose | Nächster Schritt |
|---|---|---|
| `WALKIE PTT BLOCKIERT … poweredOn/canTransmit/isLocallyOwned` | PTT-Guard-Race: Gerät aus, nicht als „getragen” markiert oder Ownership nicht beim lokalen Client | HOTEL_GAME-Seite prüfen: `WalkiePlayerController.SetHeldWalkie`/`ApplyLocalCanTransmit`, `WalkieWorldItem.SyncWalkieLocalOwnership`, `RequestPickupRpc`→`ChangeOwnership` |
| `FUNK sendet BLOCKIERT: Vivox nicht verbunden` | TX-Wechsel lief in eine Lücke vor/nach Vivox-Login/Channel-Join | Timing in `SetRadioTransmittingAsync`/`WalkieRadioSync` prüfen (ggf. Retry) |
| `FUNK sendet FEHLGESCHLAGEN …` | `SetChannelTransmissionModeAsync` wirft | Exception-Meldung analysieren; Vivox-Kanalname/State prüfen |
| `FUNK sendet auf 'default' … vivoxTx=[…]` OHNE Funkkanal in der Liste | Stilles Scheitern des Moduswechsels | Vivox-SDK-Verhalten graben (TransmittingChannels vs. Echo der eigenen TX) |
| `FUNK sendet … vivoxTx=[earshot-radio-…]` korrekt, aber Empfänger-Log bleibt stumm (`funkRx=0`) | Sendung ist IM Kanal, Empfänger bekommt nichts | Empfangsseite: Vivox-Kanal-Teilnehmer-Properties (AudioOnly, Transmission), ggf. Tap-Rebuild bei Sprechbeginn |

## Nach dem Test

1. Beide `EarshotLogs/voice-*.txt` sichern (Kopie mit Szenario-Nummern im Dateinamen).
2. Befund in `docs/walkie-talkie-debug-history.md` (neuer Abschnitt oder Nachtrag zu 19) festhalten.
3. Root-Cause-Fix erst DANN umsetzen (nicht auf Verdacht — Lektion Abschnitt 6).
4. Danach: Freund-Ferntest (Internet) mit derselben Checkliste.

## Nachtest-Runde v17 (radio-tx-probe)

Der erste Lauf (2026-09-20) ergab: Sendekette komplett unschuldig, TX-Wechsel von Vivox
quittiert — aber kein Audio erreicht je den Funkkanal. Der zweite Lauf mit v17 beantwortet
die letzte offene Frage (wo stirbt das Audio?):

1. **Szenario 2 normal wiederholen** (Single-Modus). Danach im **Empfänger**-Log prüfen:
   - `funkSprecher=[…:E=0.xx/S=True]` bei `funkRx=0` → Audio ist im Kanal, unsere Tap-Schicht
     ist schuld (Empfängerseite graben).
   - `funkSprecher=[…:E=0.00/S=False]` → Sendung kommt nie im Kanal an (Vivox-Speisung).
2. **Sender: F6 drücken** (seit v17.1 immer aktiv — auch im Windows-Build, kein Inspector nötig;
   Log: `WALKIE DIAGNOSE F6: Funk-Sendemodus ALL`) und Szenario 2 wiederholen:
   - Funk-Audio kommt an (`funkRx>0`, `OUTPUT AN mode=REMOTE`) → Single-Wechsel auf den zweiten
     Kanal ist die Vivox-Seite des Bugs; ALL ist der Fix-Kandidat (und testet „Proximity parallel").
   - Immer noch nichts → nächstes Level (native Vivox-Logs).
3. Logs wieder in `EarshotLogs` sichern und BOTH mitschicken.

## Nachtest-Runde v17.1 (dritter Lauf ausgewertet, vierter Lauf geplant)

**Dritter Lauf (2026-09-20, `voice-20260920-093314-809`/`093348-865`) — beide offenen Fragen beantwortet:**

- `funkSprecher=[…:E=0,00/S=False]` durchgehend während Sender-PTT mit lebendem Mikro → Sendung
  kommt nie im Funkkanal an (wie zweiter Lauf).
- **F6/ALL auf beiden Instanzen getestet** (nur der Sender braucht es): `vivoxTx=[prox | radio]`
  korrekt gesetzt, Sender `micPeak=0,34` — und **trotzdem nichts im Funkkanal** (`E=0,00`).
  → Single-Verdacht WIDERLEGT: Das Mikro erreicht den Funkkanal unabhängig vom TX-Modus nie.
- **Beweis-Lücke des Laufs:** Spieler standen ~46 m auseinander (außerhalb der 25-m-Proximity-Weite)
  → der Prox-Kanal als zweite Spur war tot; wir konnten nicht prüfen, ob das Mikro ÜBERHAUPT noch
  sendet, während PTT gehalten wird.
- **Echo-Beobachtung:** lautes Selbst-Hören beim Proximity-Reden — bei 46 m muss der räumliche Tap
  stumm sein; Verdacht: Vivox-native Ausgabe auf dem virtuellen Kabel (AUDIO-DEVICES-Warnzeile).
  v17/v17.1 ändern KEINEN Audio-Code. Verifikation: Windows-Lautstärkemixer beim Auftreten prüfen.

**Vierter Lauf — ALL-PTT in Proximity-Weite (5–10 m Abstand, entscheidend):**

1. **Normal reden** (ohne PTT) → Mund-Stimme in der anderen Instanz räumlich und leise hörbar
   (Baseline, dass Empfang generell funktioniert).
2. **Sender: F6 drücken** (Log: `WALKIE DIAGNOSE F6: Funk-Sendemodus ALL`), PTT halten + reden:
   - **Mund-Stimme bleibt hörbar** → TX-Pipeline arbeitet; nur der FUNKKANAL ist kaputt
     (Medien-/URI-/Tap-Ebene) → Kanal-URIs und Funk-Tap-Registrierung vergleichen.
   - **Mund-Stimme verstummt komplett** → der TX-Wechsel tötet die Mikro-Übertragung insgesamt
     (Vivox-Core/sessiongroup-Bug) → native Vivox-Logs aktivieren.
3. **Sender: nochmal F6** (zurück auf SINGLE), PTT + reden → Mund-Stimme muss jetzt verstummen
   (Kontrollprobe: zeigt, dass der Moduswechsel überhaupt greift).
4. Parallel: Tritt das laute Echo auf → Windows-Lautstärkemixer prüfen (welche App schlägt aus?).
5. Logs beider Instanzen in `EarshotLogs` sichern.

*Angelegt 2026-09-20 (v16.9), Nachtest-Runden v17/v17.1 ergänzt. Bei Änderung der Szenarien: hier pflegen, nicht nur im Chat.*
