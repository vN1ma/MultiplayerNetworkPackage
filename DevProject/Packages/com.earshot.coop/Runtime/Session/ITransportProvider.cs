using System.Threading.Tasks;
using Unity.Services.Multiplayer;

namespace Earshot
{
    /// <summary>
    /// Legt fest, wie Spieler technisch zueinander finden.
    /// <para>
    /// Diese Schnittstelle existiert, damit spaeter Steam Sockets neben Unity Relay treten
    /// kann, ohne dass Sitzungslogik, Spieler-Handling oder Voice davon etwas mitbekommen.
    /// Nur der Erzeuger einer Sitzung legt den Verbindungsweg fest - wer beitritt, uebernimmt
    /// ihn automatisch aus der Sitzung.
    /// </para>
    /// </summary>
    public interface ITransportProvider
    {
        /// <summary>Anzeigename fuer Log-Ausgaben und den Setup-Wizard.</summary>
        string DisplayName { get; }

        /// <summary>
        /// Einmalige Vorbereitung, bevor eine Sitzung entsteht. Relay braucht nichts,
        /// ein spaeterer Steam-Transport wuerde hier die Steam-API hochfahren.
        /// Gibt false zurueck, wenn der Transport nicht einsatzbereit ist.
        /// </summary>
        Task<bool> PrepareAsync();

        /// <summary>
        /// Traegt den Verbindungsweg in die Optionen einer neu erstellten Sitzung ein.
        /// </summary>
        void ConfigureCreate(SessionOptions options, CoopSettings settings);
    }
}
