using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// First-person mouse look. Reads Mouse.current.delta directly — no PlayerInput needed.
///
/// Yaw  (left/right) rotates the parent Player GameObject.
/// Pitch (up/down)   rotates this CameraRoot locally, clamped ±pitchClamp°.
///
/// Disabled on remote player instances via PlayerController.OnNetworkSpawn().
/// </summary>
public class PlayerCamera : MonoBehaviour
{
    [SerializeField] private float sensitivity = 0.15f;
    [SerializeField] private float pitchClamp  = 80f;

    private Transform _playerBody;
    private float     _pitch;

    private void Awake()
    {
        _playerBody = transform.parent;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible   = false;
    }

    private void Update()
    {
        // Escape releases cursor
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible   = true;
        }

        var mouse = Mouse.current;
        if (mouse == null) return;

        var delta = mouse.delta.ReadValue() * sensitivity;

        // Pitch — rotate CameraRoot locally
        _pitch -= delta.y;
        _pitch  = Mathf.Clamp(_pitch, -pitchClamp, pitchClamp);
        transform.localRotation = Quaternion.Euler(_pitch, 0f, 0f);

        // Yaw — rotate the whole player body
        if (_playerBody != null)
            _playerBody.Rotate(Vector3.up, delta.x);
    }
}
