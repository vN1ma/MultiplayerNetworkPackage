using Unity.Netcode;
using UnityEngine;

namespace Earshot
{
    /// <summary>
    /// Basis fuer Dinge in der Welt, die ein Spieler benutzen kann: Tueren, Schalter, Hebel.
    /// <para>
    /// Die eigentliche Wirkung laeuft auf dem Server. Clients schicken nur die Bitte;
    /// dadurch sehen alle dieselbe Zustandsaenderung und niemand kann den Zustand
    /// an dem Host vorbei umbiegen.
    /// </para>
    /// </summary>
    public abstract class NetworkInteractable : NetworkBehaviour
    {
        [SerializeField]
        [Tooltip("Text fuer eine eigene Benutzeroberflaeche, etwa 'Tuer oeffnen'. Earshot zeichnet selbst kein Prompt.")]
        private string prompt = "Benutzen";

        /// <summary>Text, den eine UI neben dem Fadenkreuz zeigen kann.</summary>
        public string Prompt => prompt;

        /// <summary>
        /// Fordert die Benutzung an. Darf von jedem Client aufgerufen werden; ausgefuehrt
        /// wird sie auf dem Server.
        /// </summary>
        public void RequestInteract()
        {
            if (!IsSpawned) return;
            SubmitInteractRpc();
        }

        [Rpc(SendTo.Server)]
        private void SubmitInteractRpc(RpcParams rpcParams = default)
        {
            OnInteract(rpcParams.Receive.SenderClientId);
        }

        /// <summary>
        /// Die Wirkung der Benutzung. Laeuft nur auf dem Server.
        /// </summary>
        public abstract void OnInteract(ulong clientId);
    }
}
