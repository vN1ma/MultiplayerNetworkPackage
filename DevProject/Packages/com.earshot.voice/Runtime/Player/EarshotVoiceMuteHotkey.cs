using UnityEngine;

namespace Earshot.Voice
{
    /// <summary>
    /// Optionale Stummschalt-Taste. Nicht Teil des Standardwegs — nur noetig, wenn
    /// das eigene Spiel noch keine Mute-Taste hat. Auf ein beliebiges Objekt in der
    /// Szene legen, nicht auf den Player.
    /// </summary>
    [AddComponentMenu("Earshot Voice/Mute Hotkey (optional)")]
    public class EarshotVoiceMuteHotkey : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Taste zum Umschalten des Mikrofons.")]
        private KeyCode muteKey = KeyCode.M;

        private void Update()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(muteKey)) EarshotVoice.ToggleMicrophone();
#endif
        }
    }
}
