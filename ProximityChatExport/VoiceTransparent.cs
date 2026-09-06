using UnityEngine;

namespace Earshot.Proximity
{
    /// <summary>
    /// Collider mit dieser Komponente zaehlen nicht als Wand fuer Stimme.
    /// Fuer grosse Koerper wie den Playtest-Planeten, deren Sehne sonst jede
    /// Unterhaltung auf der Oberflaeche als "hinter einer Mauer" wertet.
    /// </summary>
    [AddComponentMenu("Earshot Proximity/Voice Transparent")]
    [DisallowMultipleComponent]
    public sealed class VoiceTransparent : MonoBehaviour
    {
    }
}
