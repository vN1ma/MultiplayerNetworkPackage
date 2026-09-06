using UnityEngine;

namespace Earshot.Voice
{
    /// <summary>
    /// Testtuer: Blatt blockiert den Weg, <see cref="VoicePortal.Openness"/> steuert den Klang.
    /// </summary>
    [AddComponentMenu("Earshot Voice/Hearing Test Door")]
    public sealed class HearingTestDoor : MonoBehaviour
    {
        [SerializeField] private VoicePortal portal;
        [SerializeField] private Transform leaf;
        [SerializeField] private Collider leafCollider;

        public bool IsOpen => portal != null && portal.IsOpen;
        public string Label => IsOpen ? "Tuer offen" : "Tuer zu";

        public void Bind(VoicePortal voicePortal, Transform doorLeaf, Collider collider)
        {
            portal = voicePortal;
            leaf = doorLeaf;
            leafCollider = collider;
            Apply();
        }

        public void Toggle()
        {
            if (portal == null) return;
            portal.IsOpen = !portal.IsOpen;
            Apply();
        }

        private void Apply()
        {
            bool open = IsOpen;
            if (leaf != null) leaf.gameObject.SetActive(!open);
            if (leafCollider != null) leafCollider.enabled = !open;
        }
    }
}
