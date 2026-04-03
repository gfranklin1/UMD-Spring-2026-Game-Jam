using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerCamera : MonoBehaviour
{
    [SerializeField] private float sensitivity = 0.15f;
    [SerializeField] private float pitchClamp  = 80f;

    private Transform        _playerBody;
    private float            _pitch;
    private InputAction      _lookAction;
    private PlayerController _playerController;
    private bool             _cursorLocked = true;

    private void Awake()
    {
        _playerBody       = transform.parent;
        _playerController = GetComponentInParent<PlayerController>(includeInactive: true);

        var pi = GetComponentInParent<PlayerInput>(includeInactive: true);
        if (pi != null) _lookAction = pi.actions["Look"];

        SetCursorLocked(true);
        sensitivity = SettingsManager.GetSensitivity();
    }

    private void SetCursorLocked(bool locked)
    {
        _cursorLocked    = locked;
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible   = !locked;
    }

    private void Update()
    {
        // Freeze look and free cursor while game over screen is showing
        bool gameOver = QuotaManager.Instance != null && QuotaManager.Instance.IsGameOver;
        if (gameOver)
        {
            if (_cursorLocked) SetCursorLocked(false);
            return;
        }

        bool uiOpen = _playerController != null && _playerController.OpenUI;

        // Sync cursor state to chest UI state
        if (uiOpen && _cursorLocked)
            SetCursorLocked(false);
        else if (!uiOpen && !_cursorLocked)
            SetCursorLocked(true);

        // Escape releases cursor while no chest is open
        if (!uiOpen && _cursorLocked &&
            Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            SetCursorLocked(false);

        // No look input while cursor is free or chest is open
        if (_lookAction == null || !_cursorLocked) return;

        var delta = _lookAction.ReadValue<Vector2>() * sensitivity;

        _pitch -= delta.y;
        _pitch  = Mathf.Clamp(_pitch, -pitchClamp, pitchClamp);
        transform.localRotation = Quaternion.Euler(_pitch, 0f, 0f);

        if (_playerBody != null)
            _playerBody.Rotate(Vector3.up, delta.x);
    }
}
