using System;
using System.Threading.Tasks;
using Unity.Services.Multiplayer;

namespace Earshot
{
    /// <summary>
    /// Kapselt Erstellen, Beitreten und Verlassen einer Sitzung. Die Multiplayer-Services-SDK
    /// startet den NetworkManager dabei selbst - wir rufen also bewusst kein StartHost oder
    /// StartClient auf, sonst gaebe es zwei konkurrierende Verbindungsversuche.
    /// </summary>
    internal class SessionController
    {
        private readonly ITransportProvider transport;

        public SessionController(ITransportProvider transport)
        {
            this.transport = transport ?? new RelayTransportProvider();
        }

        /// <summary>Die laufende Sitzung, oder null.</summary>
        public ISession Current { get; private set; }

        /// <summary>Der Code, den Mitspieler zum Beitreten brauchen. Leer, wenn keine Sitzung laeuft.</summary>
        public string JoinCode => Current?.Code ?? string.Empty;

        public bool HasSession => Current != null;

        /// <summary>
        /// Erstellt eine Sitzung und macht diese Instanz zum Host.
        /// </summary>
        public async Task<ISession> CreateAsync(CoopSettings settings)
        {
            if (!await transport.PrepareAsync())
            {
                throw new InvalidOperationException(
                    $"Transport '{transport.DisplayName}' ist nicht einsatzbereit.");
            }

            var options = new SessionOptions
            {
                MaxPlayers = settings.MaxPlayers
            };

            transport.ConfigureCreate(options, settings);

            CoopLog.Info($"Sitzung wird erstellt ueber {transport.DisplayName}, " +
                         $"Platz fuer {settings.MaxPlayers} Spieler.");

            Current = await MultiplayerService.Instance.CreateSessionAsync(options);

            CoopLog.Info($"Sitzung laeuft. Join-Code: {Current.Code}");
            return Current;
        }

        /// <summary>
        /// Tritt einer bestehenden Sitzung per Code bei. Der Verbindungsweg kommt aus der
        /// Sitzung selbst, muss hier also nicht angegeben werden.
        /// </summary>
        public async Task<ISession> JoinByCodeAsync(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                throw new ArgumentException("Join-Code ist leer.", nameof(code));
            }

            string normalized = NormalizeCode(code);
            CoopLog.Info($"Sitzung '{normalized}' wird betreten.");

            Current = await MultiplayerService.Instance.JoinSessionByCodeAsync(normalized);

            CoopLog.Info("Sitzung betreten.");
            return Current;
        }

        /// <summary>
        /// Verlaesst die Sitzung. Fehler werden protokolliert, aber nicht weitergereicht:
        /// Wer aussteigen will, soll nicht daran scheitern, dass die Gegenseite schon weg ist.
        /// </summary>
        public async Task LeaveAsync()
        {
            if (Current == null) return;

            var session = Current;
            Current = null;

            try
            {
                await session.LeaveAsync();
                CoopLog.Info("Sitzung verlassen.");
            }
            catch (Exception ex)
            {
                CoopLog.Warn($"Beim Verlassen der Sitzung trat ein Fehler auf: {ex.Message}");
            }
        }

        /// <summary>
        /// Macht aus getippten Codes etwas Verwendbares. Join-Codes sind Grossbuchstaben;
        /// Leerzeichen und Kleinschreibung sind die haeufigsten Tippfehler beim Vorlesen
        /// ueber Discord.
        /// </summary>
        internal static string NormalizeCode(string code)
        {
            return code.Trim().Replace(" ", string.Empty).ToUpperInvariant();
        }
    }
}
