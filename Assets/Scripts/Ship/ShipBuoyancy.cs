using UnityEngine;

/// <summary>
/// Cosmetic wave-following for the ship. Samples OceanWaves at 4 hull points
/// to set the ship's Y position and pitch/roll each frame.
/// Runs on all clients (deterministic — no network sync needed).
/// </summary>
public class ShipBuoyancy : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private OceanWaves oceanWaves;

    [Header("Hull Sample Points (local space)")]
    [SerializeField] private Vector3 bowOffset       = new(0, 0, 10);
    [SerializeField] private Vector3 sternOffset     = new(0, 0, -10);
    [SerializeField] private Vector3 portOffset      = new(-5, 0, 0);
    [SerializeField] private Vector3 starboardOffset = new(5, 0, 0);

    [Header("Smoothing")]
    [SerializeField] private float heightSmoothTime = 0.35f;
    [SerializeField] private float tiltSmoothSpeed  = 2f;

    private ShipMovement _shipMovement;
    private float _heightVelocity;
    private float _currentY;
    private Quaternion _currentTiltRotation = Quaternion.identity;
    private bool _initialized;

    private void Start()
    {
        if (oceanWaves == null)
            oceanWaves = FindObjectOfType<OceanWaves>();
        _shipMovement = GetComponent<ShipMovement>();
        _currentY = transform.position.y;
        _initialized = true;
    }

    private void LateUpdate()
    {
        if (oceanWaves == null || !_initialized) return;

        // Sample wave height at 4 hull points
        float bowY       = oceanWaves.GetWaveHeight(transform.TransformPoint(bowOffset));
        float sternY     = oceanWaves.GetWaveHeight(transform.TransformPoint(sternOffset));
        float portY      = oceanWaves.GetWaveHeight(transform.TransformPoint(portOffset));
        float starboardY = oceanWaves.GetWaveHeight(transform.TransformPoint(starboardOffset));

        // Average height for Y position
        float targetY = (bowY + sternY + portY + starboardY) * 0.25f;
        _currentY = Mathf.SmoothDamp(_currentY, targetY, ref _heightVelocity, heightSmoothTime);

        // Compute surface normal from height differences
        float yaw = _shipMovement != null ? _shipMovement.CurrentYaw : transform.eulerAngles.y;
        Quaternion yawRot = Quaternion.Euler(0, yaw, 0);
        Vector3 shipForward = yawRot * Vector3.forward;
        Vector3 shipRight   = yawRot * Vector3.right;

        float forwardSlope = bowY - sternY;
        float rightSlope   = starboardY - portY;
        float forwardDist  = (bowOffset - sternOffset).magnitude;
        float rightDist    = (starboardOffset - portOffset).magnitude;

        Vector3 forwardTangent = shipForward * forwardDist + Vector3.up * forwardSlope;
        Vector3 rightTangent   = shipRight * rightDist + Vector3.up * rightSlope;
        Vector3 surfaceNormal  = Vector3.Cross(forwardTangent, rightTangent).normalized;

        // Ensure normal points up (not down)
        if (surfaceNormal.y < 0) surfaceNormal = -surfaceNormal;

        // Build target rotation: preserve yaw, apply pitch/roll from wave normal
        Vector3 adjustedForward = Vector3.ProjectOnPlane(shipForward, surfaceNormal).normalized;
        Quaternion targetRotation = Quaternion.LookRotation(adjustedForward, surfaceNormal);
        _currentTiltRotation = Quaternion.Slerp(_currentTiltRotation, targetRotation, tiltSmoothSpeed * Time.deltaTime);

        // Apply position (keep XZ from ShipMovement, override Y)
        Vector3 pos = transform.position;
        pos.y = _currentY;
        transform.position = pos;
        transform.rotation = _currentTiltRotation;
    }
}
