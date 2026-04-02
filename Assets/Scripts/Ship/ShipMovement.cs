using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Server-authoritative ship locomotion. Handles wind-driven forward thrust,
/// yaw steering, water drag, and anchor braking.
/// Only the server writes position/rotation; NetworkTransform syncs to clients.
/// NetworkTransform should sync XZ position + Y rotation only (buoyancy handles the rest).
/// </summary>
public class ShipMovement : NetworkBehaviour
{
    [Header("Wind")]
    [SerializeField] private float minWindSpeed   = 3f;
    [SerializeField] private float maxWindSpeed    = 8f;
    [SerializeField] private float windChangeSpeed = 0.15f; // Perlin noise time scale (lower = slower gusts)

    [Header("Steering")]
    [SerializeField] private float maxTurnSpeed = 30f; // degrees/s at full speed & full rudder

    [Header("Drag")]
    [SerializeField] private float waterDrag  = 0.3f;  // speed lost fraction per second when coasting
    [SerializeField] private float anchorDrag = 8f;     // massive drag when anchored

    private float _currentSpeed;
    private float _currentYaw;
    private float _steeringInput; // -1 to 1
    private bool  _helmOccupied;
    private bool  _isAnchored;
    private float _windSeed;

    /// <summary>Current yaw in degrees. Read by ShipBuoyancy to preserve yaw.</summary>
    public float CurrentYaw => _currentYaw;

    /// <summary>Current forward speed. Can be read by HUD or other systems.</summary>
    public float CurrentSpeed => _currentSpeed;

    public override void OnNetworkSpawn()
    {
        _currentYaw = transform.eulerAngles.y;
        _windSeed = Random.Range(0f, 1000f);
    }

    private void Start()
    {
        _currentYaw = transform.eulerAngles.y;
        if (_windSeed == 0f) _windSeed = Random.Range(0f, 1000f);
    }

    private void Update()
    {
        bool isAuthority = IsServer || (!NetworkManager.Singleton?.IsListening ?? true);
        if (!isAuthority) return;

        float dt = Time.deltaTime;

        // Wind-driven thrust: Perlin noise produces smoothly varying speed
        float windT = Mathf.PerlinNoise(_windSeed, Time.time * windChangeSpeed);
        float windSpeed = Mathf.Lerp(minWindSpeed, maxWindSpeed, windT);

        if (_helmOccupied && !_isAnchored)
        {
            // Accelerate toward wind speed
            _currentSpeed = Mathf.MoveTowards(_currentSpeed, windSpeed, windSpeed * dt);
        }

        // Drag
        float drag = _isAnchored ? anchorDrag : waterDrag;
        _currentSpeed -= _currentSpeed * drag * dt;
        if (_currentSpeed < 0.01f) _currentSpeed = 0f;

        // Steering: turn rate scales with speed ratio so you can't spin while stopped
        float speedRatio = maxWindSpeed > 0f ? Mathf.Clamp01(_currentSpeed / maxWindSpeed) : 0f;
        _currentYaw += _steeringInput * maxTurnSpeed * speedRatio * dt;

        // Apply movement (XZ only — buoyancy handles Y in LateUpdate)
        Vector3 forward = Quaternion.Euler(0, _currentYaw, 0) * Vector3.forward;
        Vector3 pos = transform.position;
        pos += forward * _currentSpeed * dt;
        transform.position = pos;
        transform.rotation = Quaternion.Euler(0, _currentYaw, 0);
    }

    // ── Server API (called by HelmStation and AnchorSystem) ──────

    [ServerRpc(RequireOwnership = false)]
    public void SetSteeringInputServerRpc(float steering)
    {
        _steeringInput = Mathf.Clamp(steering, -1f, 1f);
    }

    [ServerRpc(RequireOwnership = false)]
    public void SetHelmOccupiedServerRpc(bool occupied)
    {
        _helmOccupied = occupied;
        if (!occupied) _steeringInput = 0f;
    }

    /// <summary>Called by AnchorSystem (server-only).</summary>
    public void SetAnchored(bool anchored)
    {
        _isAnchored = anchored;
        if (anchored)
        {
            _currentSpeed = 0f;
            _steeringInput = 0f;
        }
    }

    // ── Offline support ──────────────────────────────────────────

    public void SetSteeringInputLocal(float steering)
    {
        _steeringInput = Mathf.Clamp(steering, -1f, 1f);
    }

    public void SetHelmOccupiedLocal(bool occupied)
    {
        _helmOccupied = occupied;
        if (!occupied) _steeringInput = 0f;
    }
}
