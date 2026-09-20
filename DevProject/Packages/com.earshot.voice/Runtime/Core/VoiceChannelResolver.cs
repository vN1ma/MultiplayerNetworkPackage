using System;
using System.Collections;
using System.Reflection;
using System.Threading.Tasks;

namespace Earshot.Voice
{
    /// <summary>
    /// Gemeinsamer Kanalname: Inspector-Feld, sonst aktive UGS-Session, sonst Unity-Lobby,
    /// sonst Settings, sonst <see cref="DefaultChannel"/>.
    /// <para>
    /// Die aktive Session geht VOR der Lobby: der Lobby-Cache
    /// (GetJoinedLobbiesAsync) ist direkt nach Create/Leave oder nach Abstuerzen
    /// veraltet (verwaiste Mitgliedschaften), sodass Host und Joiner in
    /// unterschiedliche Kanaele geraten konnten.
    /// </para>
    /// </summary>
    public static class VoiceChannelResolver
    {
        public const string DefaultChannel = "earshot";

        public static string Resolve(string componentOverride)
        {
            if (!string.IsNullOrWhiteSpace(componentOverride)) return componentOverride.Trim();

            string fromSettings = EarshotVoiceSettings.Instance.ChannelName;
            if (!string.IsNullOrWhiteSpace(fromSettings)) return fromSettings.Trim();

            return DefaultChannel;
        }

        public static async Task<string> ResolveAsync(string componentOverride)
        {
            if (!string.IsNullOrWhiteSpace(componentOverride)) return componentOverride.Trim();

            string sessionId = TryGetActiveSessionId();
            if (!string.IsNullOrWhiteSpace(sessionId))
            {
                EarshotVoiceLog.Info(
                    "Sprachkanal aus aktiver Session: " + sessionId
                );
                return sessionId.Trim();
            }

            string lobbyId = await TryGetUnityLobbyIdAsync();
            if (!string.IsNullOrWhiteSpace(lobbyId))
            {
                EarshotVoiceLog.Info("Sprachkanal aus Lobby: " + lobbyId);
                return lobbyId.Trim();
            }

            return Resolve(null);
        }

        /// <summary>
        /// Session-Id aus Unity.Services.Multiplayer (MultiplayerService.Instance.Sessions),
        /// per Reflection, damit das Paket ohne Hard-Dependency bleibt. Der Projekttreiber
        /// (z.B. RelaySessionUI) fuelle die aktive Session per Create/Join, bevor der
        /// Spieler spawnt und Voice verbindet.
        /// </summary>
        private static string TryGetActiveSessionId()
        {
            try
            {
                Type serviceType = FindType(
                    "Unity.Services.Multiplayer.MultiplayerService"
                );
                if (serviceType == null) return null;

                object instance = serviceType.GetProperty("Instance")?.GetValue(null);
                if (instance == null) return null;

                // 'Sessions' ist in WrappedMultiplayerService explizit als
                // IMultiplayerService.Sessions implementiert. GetProperty
                // ("Sessions") auf dem konkreten Typ findet explizite
                // Interface-Implementierungen nicht und lieferte deshalb
                // immer null (Logs 20.09.: durchgaengig 'Sprachkanal aus
                // Lobby' trotz aktiver Session). Zugriff deshalb ueber den
                // Interface-Typ.
                Type serviceInterface = FindType(
                    "Unity.Services.Multiplayer.IMultiplayerService"
                );
                object sessions = serviceInterface != null
                    ? serviceInterface.GetProperty("Sessions")?.GetValue(instance)
                    : instance.GetType().GetProperty("Sessions")?.GetValue(instance);
                if (sessions is not System.Collections.IEnumerable enumerable)
                {
                    return null;
                }

                foreach (object entry in enumerable)
                {
                    object session = entry?.GetType()
                        .GetProperty("Value")
                        ?.GetValue(entry);
                    string id = session?.GetType()
                        .GetProperty("Id")
                        ?.GetValue(session) as string;
                    if (!string.IsNullOrWhiteSpace(id)) return id;
                }
            }
            catch (Exception ex)
            {
                EarshotVoiceLog.Info(
                    "Aktive Session nicht lesbar, Lobby-Kanal gilt. " + ex.Message
                );
            }

            return null;
        }

        private static async Task<string> TryGetUnityLobbyIdAsync()
        {
            try
            {
                Type serviceType = FindType("Unity.Services.Lobbies.LobbyService");
                if (serviceType == null) return null;

                object instance = serviceType.GetProperty("Instance")?.GetValue(null);
                if (instance == null) return null;

                var method = instance.GetType().GetMethod("GetJoinedLobbiesAsync", Type.EmptyTypes);
                if (method == null) return null;

                object taskObject = method.Invoke(instance, null);
                if (taskObject is not Task task) return null;

                await task.ConfigureAwait(true);

                object result = task.GetType().GetProperty("Result")?.GetValue(task);
                if (result is IList list && list.Count > 0)
                {
                    object first = list[0];
                    return first?.ToString();
                }
            }
            catch (Exception ex)
            {
                EarshotVoiceLog.Info("Lobby-Kanal nicht lesbar, Standardkanal gilt. " + ex.Message);
            }

            return null;
        }

        private static Type FindType(string fullName)
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Type type = assemblies[i].GetType(fullName, throwOnError: false);
                if (type != null) return type;
            }

            return null;
        }
    }
}
