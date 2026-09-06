using UnityEngine;

namespace Earshot.Voice
{
    /// <summary>
    /// Einfache Ego-Steuerung fuer den Hoertest. Kein Multiplayer.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class HearingTestWalker : MonoBehaviour
    {
        [SerializeField] private float walkSpeed = 5.2f;
        [SerializeField] private float mouseSensitivity = 2.1f;
        [SerializeField] private float gravity = -22f;
        [SerializeField] private Transform lookPivot;

        private CharacterController controller;
        private float pitch;
        private float verticalVelocity;

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            if (lookPivot == null)
            {
                var camera = GetComponentInChildren<Camera>();
                if (camera != null) lookPivot = camera.transform;
            }
        }

        private void Update()
        {
            if (Cursor.lockState != CursorLockMode.Locked && Input.GetMouseButtonDown(0))
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }

            if (Cursor.lockState == CursorLockMode.Locked && lookPivot != null)
            {
                float yaw = Input.GetAxisRaw("Mouse X") * mouseSensitivity;
                pitch = Mathf.Clamp(pitch - Input.GetAxisRaw("Mouse Y") * mouseSensitivity, -80f, 80f);
                transform.Rotate(0f, yaw, 0f);
                lookPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
            }

            Vector3 input = new Vector3(Input.GetAxisRaw("Horizontal"), 0f, Input.GetAxisRaw("Vertical"));
            if (input.sqrMagnitude > 1f) input.Normalize();

            Vector3 move = transform.TransformDirection(input) * walkSpeed;
            if (controller.isGrounded && verticalVelocity < 0f) verticalVelocity = -2f;
            verticalVelocity += gravity * Time.deltaTime;
            move.y = verticalVelocity;
            controller.Move(move * Time.deltaTime);
        }
    }
}
