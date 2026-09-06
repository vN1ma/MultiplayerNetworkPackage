using System;
using System.Collections;
using System.Reflection;
using System.Threading.Tasks;

namespace Earshot.Voice
{
    /// <summary>
    /// Gemeinsamer Kanalname: Inspector-Feld, sonst Unity-Lobby, sonst Settings, sonst
    /// <see cref="DefaultChannel"/>.
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

            string lobbyId = await TryGetUnityLobbyIdAsync();
            if (!string.IsNullOrWhiteSpace(lobbyId)) return lobbyId.Trim();

            return Resolve(null);
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
