using UnityEngine;

/// <summary>
/// Cosmetic wave-following for the ship. Samples OceanWaves at 4 hull points
/// to set the ship's Y position and pitch/roll each frame.
/// Runs on all clients (deterministic — no network sync needed).
/// </summary>
[DefaultExecutionOrder(-10)]
public class ShipBuoyancy : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private OceanWaves oceanWaves;

    [Header("Hull Sample Points (local space)")]
    [SerializeField] private Vector3 bowOffset       = new(0, 1, 13);
    [SerializeField] private Vector3 sternOffset     = new(0, 1, -13);
    [SerializeField] private Vector3 portOffset      = new(-4, 1, -4);
    [SerializeField] private Vector3 starboardOffset = new(4, 1, -4);

    [Header("Tilt")]
    [SerializeField] private float tiltScale    = 0.3f;
    [SerializeField] private float maxTiltAngle = 5f;

    [Header("Smoothing")]
    [SerializeField] private float heightSmoothTime = 0.35f;
    [SerializeField] private float tiltSmoothSpeed  = 2f;

    private ShipMovement _shipMovement;
    private float _heightVelocity;
    private float _currentY;
    private float _smoothedPitch;
    private float _smoothedRoll;
    private bool _initialized;

    private void Start()
    {
        if (oceanWaves == null)
            oceanWaves = FindFirstObjectByType<OceanWaves>();
        _shipMovement = GetComponent<ShipMovement>();
        _currentY = transform.position.y;
        _initialized = true;
    }

    private void Update()
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
        // On non-authority clients, ShipMovement.Update() is skipped so CurrentYaw is frozen
        // at its spawn value. Use the transform's yaw directly — NetworkTransform has already
        // written the interpolated server yaw to the transform before this Update() runs.
        bool isAuthority = Unity.Netcode.NetworkManager.Singleton == null
                        || !Unity.Netcode.NetworkManager.Singleton.IsListening
                        || (_shipMovement != null && _shipMovement.IsServer);
        float yaw = (isAuthority && _shipMovement != null) ? _shipMovement.CurrentYaw : transform.eulerAngles.y;
        Quaternion yawRot = Quaternion.Euler(0, yaw, 0);
        Vector3 shipForward = yawRot * Vector3.forward;
        Vector3 shipRight   = yawRot * Vector3.right;

        float forwardSlope = (bowY - sternY) * tiltScale;
        float rightSlope   = (starboardY - portY) * tiltScale;
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

        // Clamp pitch/roll to maxTiltAngle
        targetRotation = ClampTilt(targetRotation, yaw);

        // Smooth only pitch/roll — yaw is applied exactly so platform tracking stays accurate
        // on non-authority clients (where yaw comes from NetworkTransform interpolation).
        Vector3 targetEuler = targetRotation.eulerAngles;
        float targetPitch = targetEuler.x > 180f ? targetEuler.x - 360f : targetEuler.x;
        float targetRoll  = targetEuler.z > 180f ? targetEuler.z - 360f : targetEuler.z;
        float t = tiltSmoothSpeed * Time.deltaTime;
        _smoothedPitch = Mathf.Lerp(_smoothedPitch, targetPitch, t);
        _smoothedRoll  = Mathf.Lerp(_smoothedRoll,  targetRoll,  t);

        // Apply position: authority writes Y (NT syncs it to clients); clients keep NT-delivered Y
        if (isAuthority)
        {
            Vector3 pos = transform.position;
            pos.y = _currentY;
            transform.position = pos;
        }
        // Pitch/roll are cosmetic and computed locally on every client
        transform.rotation = Quaternion.Euler(_smoothedPitch, yaw, _smoothedRoll);
    }

    private Quaternion ClampTilt(Quaternion rot, float yaw)
    {
        Vector3 euler = rot.eulerAngles;
        float pitch = euler.x > 180f ? euler.x - 360f : euler.x;
        float roll  = euler.z > 180f ? euler.z - 360f : euler.z;
        pitch = Mathf.Clamp(pitch, -maxTiltAngle, maxTiltAngle);
        roll  = Mathf.Clamp(roll,  -maxTiltAngle, maxTiltAngle);
        return Quaternion.Euler(pitch, yaw, roll);
    }
}
