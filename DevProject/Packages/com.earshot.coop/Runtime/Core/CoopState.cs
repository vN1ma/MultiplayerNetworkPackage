namespace Earshot
{
    /// <summary>
    /// Verbindungszustand der aktuellen Sitzung.
    /// </summary>
    public enum CoopState
    {
        /// <summary>Keine Sitzung. Startzustand.</summary>
        Offline = 0,

        /// <summary>Dienste werden initialisiert, Sitzung wird erstellt oder betreten.</summary>
        Connecting = 1,

        /// <summary>Sitzung laeuft, diese Instanz ist der Host.</summary>
        Hosting = 2,

        /// <summary>Sitzung laeuft, diese Instanz ist ein Client.</summary>
        Connected = 3,

        /// <summary>Sitzung wird gerade beendet.</summary>
        Disconnecting = 4
    }
}
