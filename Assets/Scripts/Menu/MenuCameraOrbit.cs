using UnityEngine;

/// <summary>
/// Slowly orbits the camera around a target (the ship) for the main menu background.
/// Mimics Minecraft's rotating panorama — smooth, atmospheric, no player input.
/// </summary>
public class MenuCameraOrbit : MonoBehaviour
{
    [Header("Target")]
    [Tooltip("The point the camera orbits around. Set to the ship's center transform.")]
    [SerializeField] private Transform target;

    [Header("Orbit")]
    [SerializeField] private float orbitSpeed          = 6f;   // degrees/second
    [SerializeField] private float orbitRadius         = 35f;
    [SerializeField] private float orbitHeight         = 15f;
    [SerializeField] private float targetAimYOffset    = 12.5f;

    [Header("Pitch Drift")]
    [SerializeField] private float pitchDriftAmplitude = 4f;   // degrees
    [SerializeField] private float pitchDriftPeriod    = 12f;  // seconds per cycle

    private float _yaw   = -30f;
    private float _timer = 0f;

    private void Update()
    {
        _yaw   += orbitSpeed * Time.deltaTime;
        _timer += Time.deltaTime;

        float pitch = pitchDriftAmplitude * Mathf.Sin(_timer * 2f * Mathf.PI / pitchDriftPeriod);
        float rad   = _yaw * Mathf.Deg2Rad;

        Vector3 pivot  = target != null ? target.position + Vector3.up * targetAimYOffset : Vector3.up * (2f + targetAimYOffset);
        Vector3 offset = new Vector3(
            Mathf.Sin(rad) * orbitRadius,
            orbitHeight,
            Mathf.Cos(rad) * orbitRadius
        );

        transform.position = pivot + offset;
        transform.rotation = Quaternion.LookRotation(pivot - transform.position)
                           * Quaternion.Euler(pitch, 0f, 0f);
    }
}
