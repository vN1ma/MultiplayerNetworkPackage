using UnityEngine;

namespace Earshot
{
    /// <summary>
    /// Kamera und Ohr, solange noch kein eigener Spieler da ist. Sonst waere der Bildschirm
    /// schwarz, bis jemand hostet oder beitritt.
    /// </summary>
    [AddComponentMenu("Earshot/Lobby Camera")]
    public class LobbyCamera : MonoBehaviour
    {
        private Camera cam;
        private AudioListener listener;

        private void Awake()
        {
            cam = GetComponent<Camera>();
            listener = GetComponent<AudioListener>();
        }

        private void Update()
        {
            bool showLobby = Coop.LocalPlayer == null;

            if (cam != null && cam.enabled != showLobby) cam.enabled = showLobby;
            if (listener != null && listener.enabled != showLobby) listener.enabled = showLobby;
        }
    }
}
