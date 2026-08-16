using Unity.Netcode;
using UnityEngine;

namespace Earshot
{
    /// <summary>
    /// Schlichte Ego-Steuerung fuer den Testraum. Nur der lokale Spieler laeuft und guckt;
    /// die anderen sehen denselben Avatar ueber NetworkTransform.
    /// <para>
    /// Das ist Absicht kein fertiges Movement-System fuer dein Spiel. Es existiert, damit
    /// ihr sofort durch Tueren laufen und den Proximity Chat hoeren koennt.
    /// </para>
    /// </summary>
    [AddComponentMenu("Earshot/Demo First Person")]
    [RequireComponent(typeof(CharacterController))]
    public class DemoFirstPerson : NetworkBehaviour
    {
        [SerializeField]
        [Tooltip("Laufgeschwindigkeit in Metern pro Sekunde.")]
        private float walkSpeed = 5f;

        [SerializeField]
        [Tooltip("Mausempfindlichkeit.")]
        private float mouseSensitivity = 2f;

        [SerializeField]
        [Tooltip("Fallbeschleunigung. Negativ, weil nach unten.")]
        private float gravity = -25f;

        private CharacterController controller;
        private Transform head;
        private Camera headCamera;
        private AudioListener headListener;
        private float pitch;
        private float verticalVelocity;

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            headCamera = GetComponentInChildren<Camera>();
            headListener = GetComponentInChildren<AudioListener>();
            if (headCamera != null) head = headCamera.transform;
        }

        public override void OnNetworkSpawn()
        {
            bool local = IsOwner;

            if (headCamera != null)
            {
                headCamera.enabled = local;
                if (local) headCamera.tag = "MainCamera";
            }

            if (headListener != null) headListener.enabled = local;
            enabled = local;

            ApplyBodyColor();

            if (local)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        private void ApplyBodyColor()
        {
            var body = transform.Find("Body");
            if (body == null) return;

            var renderer = body.GetComponent<Renderer>();
            if (renderer == null) return;

            var material = renderer.material;
            material.color = ColorFor(OwnerClientId);
        }

        private static Color ColorFor(ulong clientId)
        {
            switch (clientId % 4)
            {
                case 0: return new Color(0.25f, 0.55f, 0.95f);
                case 1: return new Color(0.95f, 0.45f, 0.12f);
                case 2: return new Color(0.25f, 0.8f, 0.4f);
                default: return new Color(0.85f, 0.3f, 0.75f);
            }
        }

        public override void OnNetworkDespawn()
        {
            if (!IsOwner) return;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void Update()
        {
            if (!IsOwner) return;

#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.Escape) && FindAnyObjectByType<CoopPauseMenu>() == null)
            {
                bool locked = Cursor.lockState == CursorLockMode.Locked;
                Cursor.lockState = locked ? CursorLockMode.None : CursorLockMode.Locked;
                Cursor.visible = locked;
            }

            if (CoopPauseMenu.IsOpen) return;

            if (Cursor.lockState == CursorLockMode.Locked)
            {
                float mouseX = Input.GetAxisRaw("Mouse X") * mouseSensitivity;
                float mouseY = Input.GetAxisRaw("Mouse Y") * mouseSensitivity;
                transform.Rotate(0f, mouseX, 0f);

                if (head != null)
                {
                    pitch = Mathf.Clamp(pitch - mouseY, -89f, 89f);
                    head.localRotation = Quaternion.Euler(pitch, 0f, 0f);
                }
            }

            Vector3 input = new Vector3(Input.GetAxisRaw("Horizontal"), 0f, Input.GetAxisRaw("Vertical"));
            if (input.sqrMagnitude > 1f) input.Normalize();

            Vector3 move = transform.TransformDirection(input) * walkSpeed;

            if (controller != null && controller.isGrounded && verticalVelocity < 0f)
            {
                verticalVelocity = -2f;
            }

            verticalVelocity += gravity * Time.deltaTime;
            move.y = verticalVelocity;

            if (controller != null)
            {
                controller.Move(move * Time.deltaTime);
            }
            else
            {
                transform.position += move * Time.deltaTime;
            }
#endif
        }
    }
}
