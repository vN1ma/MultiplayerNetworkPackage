using UnityEngine;

namespace Earshot.Voice
{
    /// <summary>
    /// Optionale Hilfe fuer Szenen ohne eigenes Sitzungs-Skript: verbindet beim Aktivieren
    /// und trennt beim Deaktivieren. Nicht Teil des Standardwegs — wer selbst
    /// <see cref="EarshotVoice.ConnectAsync"/> aufruft, braucht diese Komponente nicht.
    /// <para>
    /// Auf ein Objekt in der Spielszenen legen, nicht ins Hauptmenue. Der Kanalname
    /// muss bei allen Mitspielern identisch sein.
    /// </para>
    /// </summary>
    [AddComponentMenu("Earshot Voice/Connect In Scene (optional)")]
    public class EarshotVoiceConnectInScene : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Gleicher Text bei jedem Mitspieler. Beliebig, z.B. die Lobby-Id.")]
        private string channelName = "proximity-test";

        [SerializeField]
        [Tooltip("Nur fuer Logs und den Vivox-Anzeigenamen.")]
        private string displayName = "Player";

        private void OnEnable()
        {
            if (!Application.isPlaying) return;

            if (string.IsNullOrWhiteSpace(channelName))
            {
                EarshotVoiceLog.Warn("Connect In Scene: Channel Name ist leer. Nichts verbunden.");
                return;
            }

            _ = EarshotVoice.ConnectAsync(channelName.Trim(), displayName);
        }

        private void OnDisable()
        {
            if (!Application.isPlaying) return;

            _ = EarshotVoice.DisconnectAsync();
        }
    }
}
