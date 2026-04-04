using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(PlayerController))]
public class SpectatorCamera : MonoBehaviour
{
    [Header("Orbit")]
    [SerializeField] private float orbitRadius   = 4f;
    [SerializeField] private float sensitivity   = 0.15f;
    [SerializeField] private float pitchClamp    = 70f;
    [SerializeField] private float heightOffset  = 1.5f;

    private PlayerController  _owner;
    private Transform         _cameraRoot;
    private Transform         _originalParent;
    private Vector3           _originalLocalPosition;
    private Quaternion        _originalLocalRotation;
    private PlayerController  _currentTarget;
    private SpectatorHUD      _hud;
    private float             _yaw;
    private float             _pitch = 20f;

    private void Awake()
    {
        _owner                 = GetComponent<PlayerController>();
        _cameraRoot            = _owner.CameraRoot;
        _originalParent        = _cameraRoot != null ? _cameraRoot.parent : null;
        _originalLocalPosition = _cameraRoot != null ? _cameraRoot.localPosition : Vector3.zero;
        _originalLocalRotation = _cameraRoot != null ? _cameraRoot.localRotation : Quaternion.identity;
        _hud                   = GetComponent<SpectatorHUD>();
        enabled                = false;
        sensitivity = SettingsManager.GetSensitivity();
    }

    public void Activate()
    {
        if (_cameraRoot != null)
            _cameraRoot.SetParent(null, worldPositionStays: true);

        _currentTarget = FindNextAliveTarget(null, true);
        _hud?.SetTarget(_currentTarget);

        // Initialise yaw from current camera facing so there's no snap
        if (_cameraRoot != null)
            _yaw = _cameraRoot.eulerAngles.y;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible   = false;
        enabled          = true;
    }

    public void Deactivate()
    {
        if (_cameraRoot != null && _originalParent != null)
        {
            _cameraRoot.SetParent(_originalParent, false);
            _cameraRoot.localPosition = _originalLocalPosition;
            _cameraRoot.localRotation = _originalLocalRotation;
        }
        _currentTarget = null;
        enabled        = false;
    }

    private void LateUpdate()
    {
        var mouse = Mouse.current;
        var kb    = Keyboard.current;

        // Orbit with mouse
        if (mouse != null)
        {
            var delta = mouse.delta.ReadValue() * sensitivity;
            _yaw   += delta.x;
            _pitch  = Mathf.Clamp(_pitch - delta.y, -pitchClamp, pitchClamp);
        }

        // Cycle: D / Left click → next,  A / Right click → previous
        if (kb != null)
        {
            if (kb.dKey.wasPressedThisFrame) Cycle(true);
            if (kb.aKey.wasPressedThisFrame) Cycle(false);
        }
        if (mouse != null)
        {
            if (mouse.leftButton.wasPressedThisFrame)  Cycle(true);
            if (mouse.rightButton.wasPressedThisFrame) Cycle(false);
        }

        // Auto-advance if current target just died
        if (_currentTarget == null || _currentTarget.IsDead)
            Cycle(true);

        // Position camera on sphere around target, always facing it
        if (_currentTarget != null && _cameraRoot != null)
        {
            Vector3 pivot = _currentTarget.transform.position + Vector3.up * heightOffset;
            Vector3 dir   = Quaternion.Euler(_pitch, _yaw, 0f) * Vector3.back * orbitRadius;
            _cameraRoot.position = pivot + dir;
            _cameraRoot.rotation = Quaternion.LookRotation(pivot - _cameraRoot.position);
        }
    }

    private void Cycle(bool forward)
    {
        _currentTarget = FindNextAliveTarget(_currentTarget, forward);
        _hud?.SetTarget(_currentTarget);
    }

    private PlayerController FindNextAliveTarget(PlayerController current, bool forward)
    {
        var all   = FindObjectsByType<PlayerController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        var alive = new System.Collections.Generic.List<PlayerController>();
        foreach (var pc in all)
        {
            if (pc == _owner) continue;
            if (!pc.IsDead)   alive.Add(pc);
        }

        if (alive.Count == 0) return null;

        int idx = alive.IndexOf(current);
        if (idx < 0) return alive[0];

        int next = forward
            ? (idx + 1) % alive.Count
            : (idx - 1 + alive.Count) % alive.Count;

        return alive[next];
    }
}
