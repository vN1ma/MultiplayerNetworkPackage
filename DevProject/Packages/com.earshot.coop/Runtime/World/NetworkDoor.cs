using Earshot.Voice;
using Unity.Netcode;
using UnityEngine;

namespace Earshot
{
    /// <summary>
    /// Eine Tuer, deren Stellung alle Spieler teilen und die den Schall mitbewegt.
    /// <para>
    /// Solange sie aufschwingt, wird <see cref="VoicePortal.Openness"/> auf denselben
    /// Fortschritt gesetzt. Die Stimme dahinter wird also genau dann durchlaessiger,
    /// wenn die Tuer sichtbar aufgeht - nicht erst, wenn die Animation fertig ist.
    /// </para>
    /// </summary>
    [AddComponentMenu("Earshot/Network Door")]
    [RequireComponent(typeof(VoicePortal))]
    [RequireComponent(typeof(NetworkObject))]
    public class NetworkDoor : NetworkInteractable
    {
        [SerializeField]
        [Tooltip("Das Scharnier, das gedreht wird. Leer lassen dreht dieses Objekt selbst.")]
        private Transform hinge;

        [SerializeField]
        [Tooltip("Winkel in Grad, um den das Scharnier beim Oeffnen um die lokale Hochachse dreht.")]
        private float openAngle = 90f;

        [SerializeField, Min(0.01f)]
        [Tooltip("Dauer der Bewegung in Sekunden. Bestimmt, wie lange die Stimme braucht, um sich dem neuen Zustand anzunaehern.")]
        private float openSeconds = 0.6f;

        private readonly NetworkVariable<bool> isOpen = new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private VoicePortal portal;
        private Quaternion closedLocalRotation;
        private float visualProgress;
        private bool capturedClosedPose;

        /// <summary>Wahr, wenn die Tuer auf dem Server als offen gilt.</summary>
        public bool IsOpen => isOpen.Value;

        public override void OnNetworkSpawn()
        {
            EnsureClosedPose();
            portal = GetComponentInChildren<VoicePortal>();

            visualProgress = isOpen.Value ? 1f : 0f;
            ApplyVisual(visualProgress);
        }

        public override void OnInteract(ulong clientId)
        {
            Toggle();
        }

        /// <summary>Oeffnet die Tuer. Wirkung nur auf dem Server, Sichtbarkeit bei allen.</summary>
        public void Open()
        {
            RequestSetOpen(true);
        }

        /// <summary>Schliesst die Tuer.</summary>
        public void Close()
        {
            RequestSetOpen(false);
        }

        /// <summary>Wechselt zwischen offen und geschlossen.</summary>
        public void Toggle()
        {
            RequestSetOpen(!isOpen.Value);
        }

        private void RequestSetOpen(bool value)
        {
            if (!IsSpawned) return;

            if (IsServer)
            {
                isOpen.Value = value;
                return;
            }

            SubmitSetOpenRpc(value);
        }

        [Rpc(SendTo.Server)]
        private void SubmitSetOpenRpc(bool value, RpcParams rpcParams = default)
        {
            isOpen.Value = value;
        }

        private void Update()
        {
            EnsureClosedPose();
            if (portal == null) portal = GetComponentInChildren<VoicePortal>();

            float target = isOpen.Value ? 1f : 0f;
            if (!Mathf.Approximately(visualProgress, target))
            {
                float speed = 1f / Mathf.Max(0.01f, openSeconds);
                visualProgress = Mathf.MoveTowards(visualProgress, target, speed * Time.deltaTime);
            }

            ApplyVisual(visualProgress);
        }

        private void ApplyVisual(float progress)
        {
            var pivot = hinge != null ? hinge : transform;
            pivot.localRotation = closedLocalRotation * Quaternion.Euler(0f, openAngle * progress, 0f);

            if (portal != null)
            {
                portal.Openness = progress;
            }
        }

        private void EnsureClosedPose()
        {
            if (capturedClosedPose) return;

            var pivot = hinge != null ? hinge : transform;
            closedLocalRotation = pivot.localRotation;
            capturedClosedPose = true;
        }

        private void OnValidate()
        {
            openSeconds = Mathf.Max(0.01f, openSeconds);
        }
    }
}
