using System;
using UnityEngine;

namespace Earshot
{
    /// <summary>
    /// Sucht vor dem lokalen Spieler nach etwas Benutzbarem.
    /// <para>
    /// Diese Klasse zeichnet selbst keine Oberflaeche. Sie sagt nur, worauf der Blick
    /// gerade liegt; eine eigene UI kann das Prompt anzeigen, und die Benutzen-Taste
    /// hier loest die Wirkung aus.
    /// </para>
    /// </summary>
    [AddComponentMenu("Earshot/Interactor")]
    public class Interactor : MonoBehaviour
    {
        [SerializeField, Min(0.1f)]
        [Tooltip("Reichweite in Metern. Typisch fuer eine Tuer direkt vor einem: etwa 3.")]
        private float range = 3f;

        [SerializeField]
        [Tooltip("Welche Layer als benutzbar gelten. Leer lassen trifft alles.")]
        private LayerMask hitMask = ~0;

        [SerializeField]
        [Tooltip("Taste zum Benutzen. Ohne klassischen Input Manager bleibt nur das Focused-Ereignis.")]
        private KeyCode useKey = KeyCode.E;

        /// <summary>
        /// Das aktuell anvisierte Objekt, oder null. Wird nur gesendet, wenn sich der
        /// Treffer aendert - nicht in jedem Bild.
        /// </summary>
        public event Action<NetworkInteractable> Focused;

        /// <summary>Das zuletzt gemeldete Ziel. Null, wenn nichts in Reichweite ist.</summary>
        public NetworkInteractable Current { get; private set; }

        private void Update()
        {
            var player = GetComponentInParent<CoopPlayer>();
            if (player == null || !player.IsSpawned || !player.IsLocalPlayer)
            {
                SetFocus(null);
                return;
            }

            var target = Probe();
            SetFocus(target);

#if ENABLE_LEGACY_INPUT_MANAGER
            if (useKey != KeyCode.None && Input.GetKeyDown(useKey) && Current != null)
            {
                Current.RequestInteract();
            }
#endif
        }

        private void OnDisable()
        {
            SetFocus(null);
        }

        private NetworkInteractable Probe()
        {
            GetRay(out Vector3 origin, out Vector3 direction);

            if (!Physics.Raycast(
                    origin,
                    direction,
                    out RaycastHit hit,
                    range,
                    hitMask,
                    QueryTriggerInteraction.Collide))
            {
                return null;
            }

            return hit.collider.GetComponentInParent<NetworkInteractable>();
        }

        private void GetRay(out Vector3 origin, out Vector3 direction)
        {
            // Die Kamera ist der Blick des Spielers. Fehlt sie, faellt der Strahl in
            // Blickrichtung dieses Objekts - etwa wenn der Interactor auf der Kamera sitzt.
            var camera = Camera.main;
            if (camera != null)
            {
                origin = camera.transform.position;
                direction = camera.transform.forward;
                return;
            }

            origin = transform.position;
            direction = transform.forward;
        }

        private void SetFocus(NetworkInteractable target)
        {
            if (Current == target) return;
            Current = target;
            Focused?.Invoke(target);
        }
    }
}
