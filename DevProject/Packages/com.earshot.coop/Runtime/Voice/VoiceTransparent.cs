using UnityEngine;

namespace Earshot.Voice
{
    /// <summary>
    /// Collider mit dieser Komponente zaehlen nicht als Wand fuer Stimme.
    /// Fuer grosse Koerper wie den Playtest-Planeten, deren Sehne sonst jede
    /// Unterhaltung auf der Oberflaeche als "hinter einer Mauer" wertet.
    /// </summary>
    [AddComponentMenu("Earshot/Voice Transparent")]
    [DisallowMultipleComponent]
    public sealed class VoiceTransparent : MonoBehaviour
    {
    }
}
