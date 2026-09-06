using UnityEngine;

namespace Earshot
{
    /// <summary>
    /// Kamera und Ohr, solange noch kein eigener Spieler da ist. Sonst waere der Bildschirm
    /// schwarz, bis jemand hostet oder beitritt.
    /// </summary>
    [AddComponentMenu("Earshot/Lobby Camera")]
    [DefaultExecutionOrder(-200)]
    public class LobbyCamera : MonoBehaviour
    {
        private Camera cam;
        private AudioListener listener;

        private void Awake()
        {
            cam = GetComponent<Camera>();
            listener = GetComponent<AudioListener>();
            Apply();
        }

        private void OnEnable()
        {
            // LateUpdate allein liess ein Frame lang zwei aktive AudioListener zu
            // (der eigene Avatar-Kopf ist schon da, die Lobby-Kamera hat aber noch
            // nicht reagiert). Die Events schalten die Lobby sofort im selben
            // Aufruf ab, in dem der Spieler eintrifft - kein Wettlauf mehr.
            Coop.PlayerJoined += OnRosterChanged;
            Coop.PlayerLeft += OnRosterChanged;
            Coop.StateChanged += OnCoopStateChanged;
        }

        private void OnDisable()
        {
            Coop.PlayerJoined -= OnRosterChanged;
            Coop.PlayerLeft -= OnRosterChanged;
            Coop.StateChanged -= OnCoopStateChanged;
        }

        private void OnRosterChanged(CoopPlayer player) => Apply();

        private void OnCoopStateChanged(CoopState state) => Apply();

        private void LateUpdate()
        {
            // Sicherheitsnetz fuer alle Faelle, die kein Event ausloesen.
            Apply();
        }

        private void Apply()
        {
            bool showLobby = Coop.LocalPlayer == null;

            if (cam != null && cam.enabled != showLobby) cam.enabled = showLobby;
            if (listener != null && listener.enabled != showLobby) listener.enabled = showLobby;
        }
    }
}
