using UnityEngine;

namespace Earshot.Proximity
{
    /// <summary>
    /// Optionale Stummschalt-Taste. Auf ein beliebiges persistentes Objekt legen.
    /// </summary>
    [AddComponentMenu("Earshot Proximity/Mute Hotkey")]
    public class ProxVoiceMuteHotkey : MonoBehaviour
    {
        [SerializeField] private KeyCode muteKey = KeyCode.M;

        private void Update()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(muteKey)) ProxVoice.ToggleMicrophone();
#endif
        }
    }
}
