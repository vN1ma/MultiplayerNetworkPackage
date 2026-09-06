using System.Reflection;
using UnityEngine;

namespace Earshot.Voice
{
    /// <summary>
    /// Liest "bin ich der lokale Spieler?" von Netcode/Mirror/Photon/FishNet,
    /// ohne diese Assemblies zu referenzieren.
    /// </summary>
    public static class NetworkOwnershipProbe
    {
        public enum Status
        {
            None,
            NotReady,
            Ready
        }

        public readonly struct Result
        {
            public Result(Status status, bool isLocal, bool waitingForSpawn = false)
            {
                Status = status;
                IsLocal = isLocal;
                WaitingForSpawn = waitingForSpawn;
            }

            public Status Status { get; }
            public bool IsLocal { get; }
            public bool WaitingForSpawn { get; }
        }

        private static readonly string[] OwnerNames =
        {
            "IsOwner",
            "IsLocalPlayer",
            "isLocalPlayer",
            "IsMine",
            "isOwned"
        };

        public static Result Read(GameObject gameObject)
        {
            if (gameObject == null) return new Result(Status.None, false);

            var components = gameObject.GetComponentsInParent<Component>(true);
            bool sawNetwork = false;
            bool waitingToSpawn = false;

            for (int i = 0; i < components.Length; i++)
            {
                var component = components[i];
                if (component == null || component is Transform) continue;
                if (component is EarshotProximityVoice) continue;
                if (!IsNetworkish(component)) continue;

                sawNetwork = true;
                bool hasSpawned = TryReadBool(component, "IsSpawned", out bool spawned);
                if (hasSpawned && !spawned)
                {
                    waitingToSpawn = true;
                    continue;
                }

                if (!TryReadOwner(component, out bool isLocal)) continue;

                if (isLocal) return new Result(Status.Ready, true);
                if (hasSpawned && spawned) return new Result(Status.Ready, false);

                // Owner noch falsch, Spawn-Flag fehlt (Mirror/Photon vor der Zuweisung).
                waitingToSpawn = false;
                sawNetwork = true;
                return new Result(Status.NotReady, false, waitingForSpawn: false);
            }

            if (waitingToSpawn) return new Result(Status.NotReady, false, waitingForSpawn: true);
            if (sawNetwork) return new Result(Status.NotReady, false);
            return new Result(Status.None, false);
        }

        public static bool TryReadSyncedPlayerId(GameObject gameObject, out string playerId)
        {
            playerId = null;
            if (gameObject == null) return false;

            var components = gameObject.GetComponentsInParent<Component>(true);
            for (int i = 0; i < components.Length; i++)
            {
                var component = components[i];
                if (component == null || component is Transform) continue;
                if (component is EarshotProximityVoice) continue;
                if (!IsNetworkish(component) && !LooksLikePlayerState(component)) continue;

                if (TryReadStringish(component, "PlayerId", out playerId)) return true;
                if (TryReadStringish(component, "UgsPlayerId", out playerId)) return true;
                if (TryReadStringish(component, "AuthPlayerId", out playerId)) return true;
                if (TryReadStringish(component, "UnityPlayerId", out playerId)) return true;
                if (TryReadStringish(component, "AuthenticationPlayerId", out playerId)) return true;
            }

            return false;
        }

        internal static bool IsNetworkish(Component component)
        {
            if (component == null) return false;
            var type = component.GetType();
            string name = type.Name;
            string full = type.FullName ?? string.Empty;

            return ContainsIgnoreCase(name, "Network")
                || ContainsIgnoreCase(name, "PhotonView")
                || ContainsIgnoreCase(full, "Unity.Netcode")
                || ContainsIgnoreCase(full, "Mirror")
                || ContainsIgnoreCase(full, "Photon")
                || ContainsIgnoreCase(full, "FishNet");
        }

        private static bool LooksLikePlayerState(Component component)
        {
            return ContainsIgnoreCase(component.GetType().Name, "Player");
        }

        private static bool TryReadOwner(object target, out bool isLocal)
        {
            for (int i = 0; i < OwnerNames.Length; i++)
            {
                if (TryReadBool(target, OwnerNames[i], out isLocal)) return true;
            }

            isLocal = false;
            return false;
        }

        private static bool TryReadBool(object target, string name, out bool value)
        {
            value = false;
            var type = target.GetType();
            const BindingFlags flags =
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase;

            var property = type.GetProperty(name, flags);
            if (property != null && property.PropertyType == typeof(bool) && property.CanRead)
            {
                value = (bool)property.GetValue(target);
                return true;
            }

            var field = type.GetField(name, flags);
            if (field != null && field.FieldType == typeof(bool))
            {
                value = (bool)field.GetValue(target);
                return true;
            }

            return false;
        }

        private static bool TryReadStringish(object target, string name, out string value)
        {
            value = null;
            var type = target.GetType();
            const BindingFlags flags =
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase;

            object raw = null;
            var property = type.GetProperty(name, flags);
            if (property != null && property.CanRead) raw = property.GetValue(target);
            else
            {
                var field = type.GetField(name, flags);
                if (field != null) raw = field.GetValue(target);
            }

            if (raw == null) return false;
            if (raw is string text)
            {
                if (string.IsNullOrWhiteSpace(text)) return false;
                value = text.Trim();
                return true;
            }

            var valueProperty = raw.GetType().GetProperty("Value", flags);
            if (valueProperty == null) return false;

            object inner = valueProperty.GetValue(raw);
            if (inner == null) return false;

            string asText = inner.ToString();
            if (string.IsNullOrWhiteSpace(asText)) return false;
            value = asText.Trim();
            return true;
        }

        private static bool ContainsIgnoreCase(string haystack, string needle)
        {
            return haystack.IndexOf(needle, System.StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
