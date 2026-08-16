using System.Threading.Tasks;
using Unity.Services.Multiplayer;

namespace Earshot
{
    /// <summary>
    /// Verbindet die Spieler ueber Unity Relay. Der Datenverkehr laeuft ueber Unitys Server,
    /// wodurch niemand Ports freigeben oder eine feste IP kennen muss - der Grund, warum das
    /// bei Freunden ueber verschiedene Internetanschluesse einfach funktioniert.
    /// <para>
    /// Kostenlos bis 50 durchschnittliche gleichzeitige Nutzer im Monat, was fuer
    /// Koop-Runden unter Freunden weit jenseits des Erreichbaren liegt.
    /// </para>
    /// </summary>
    public class RelayTransportProvider : ITransportProvider
    {
        public string DisplayName => "Unity Relay";

        public Task<bool> PrepareAsync()
        {
            // Relay braucht keine Vorbereitung: Die Zuteilung passiert beim Erstellen
            // der Sitzung automatisch.
            return Task.FromResult(true);
        }

        public void ConfigureCreate(SessionOptions options, CoopSettings settings)
        {
            string region = settings.RelayRegion;

            if (string.IsNullOrEmpty(region))
            {
                // Ohne feste Region misst Unity die Laufzeiten und nimmt die schnellste.
                options.WithRelayNetwork();
            }
            else
            {
                CoopLog.Info($"Relay-Region ist fest auf '{region}' gesetzt.");
                options.WithRelayNetwork(region);
            }
        }
    }
}
