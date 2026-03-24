using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerCamera : MonoBehaviour
{
    [SerializeField] private float sensitivity = 0.15f;
    [SerializeField] private float pitchClamp  = 80f;

    private Transform _playerBody;
    private float     _pitch;
    private InputAction _lookAction;

    private void Awake()
    {
        _playerBody = transform.parent;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible   = false;

        // PlayerInput lives on the root; grab Look from it
        var pi = GetComponentInParent<PlayerInput>(includeInactive: true);
        if (pi != null) _lookAction = pi.actions["Look"];
    }

    private void Update()
    {
        // Escape releases cursor
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible   = true;
        }

        if (_lookAction == null) return;

        var delta = _lookAction.ReadValue<Vector2>() * sensitivity;

        // Pitch — rotate CameraRoot locally
        _pitch -= delta.y;
        _pitch  = Mathf.Clamp(_pitch, -pitchClamp, pitchClamp);
        transform.localRotation = Quaternion.Euler(_pitch, 0f, 0f);

        // Yaw — rotate the whole player body
        if (_playerBody != null)
            _playerBody.Rotate(Vector3.up, delta.x);
    }
}
