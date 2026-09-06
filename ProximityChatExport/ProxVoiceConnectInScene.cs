using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using UnityEngine;

namespace Earshot.Proximity
{
    /// <summary>
    /// Tritt dem Sprachkanal bei, sobald dieses Objekt aktiv wird, und geht beim
    /// Deaktivieren wieder raus. Auf ein Objekt in der SPIELSZENE legen (nicht ins
    /// Hauptmenue). Channel Name muss bei allen Spielern identisch sein.
    /// </summary>
    [AddComponentMenu("Earshot Proximity/Connect In Scene")]
    public class ProxVoiceConnectInScene : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Gleicher Text bei jedem Mitspieler. Beliebig, z.B. test oder die Lobby-Id.")]
        private string channelName = "proximity-test";

        [SerializeField]
        [Tooltip("Nur fuer Logs und Vivox-Anzeigename.")]
        private string displayName = "Player";

        private async void OnEnable()
        {
            if (string.IsNullOrWhiteSpace(channelName))
            {
                ProxLog.Warn("ProxVoiceConnectInScene: Channel Name ist leer. Nichts verbunden.");
                return;
            }

            // RelaySessionUI im Hotel loggt in Start() ein. Kurz warten, sonst
            // starten zwei anonyme Logins gleichzeitig.
            await WaitUntilSignedInOrTimeoutAsync();
            if (!isActiveAndEnabled) return;

            await ProxVoice.ConnectAsync(channelName.Trim(), displayName);
        }

        private async void OnDisable()
        {
            await ProxVoice.DisconnectAsync();
        }

        private static async Task WaitUntilSignedInOrTimeoutAsync()
        {
            await Task.Delay(250);

            float elapsed = 0f;
            while (elapsed < 12f)
            {
                try
                {
                    if (UnityServices.State == ServicesInitializationState.Initialized &&
                        AuthenticationService.Instance != null &&
                        AuthenticationService.Instance.IsSignedIn)
                    {
                        return;
                    }
                }
                catch
                {
                    // Services noch nicht bereit.
                }

                await Task.Delay(100);
                elapsed += 0.1f;
            }
        }
    }
}
