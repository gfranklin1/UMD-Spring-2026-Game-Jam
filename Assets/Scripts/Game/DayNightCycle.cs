using UnityEngine;

/// <summary>
/// Drives visual day/night cycle by reading QuotaManager.TimeOfDay01 each frame.
/// Rotates the sun, drives moon opposite the sun, and blends the skybox material.
/// Not networked — all clients read the same synced time value independently.
/// </summary>
public class DayNightCycle : MonoBehaviour
{
    [SerializeField] private Light directionalLight;

    [Header("Sun")]
    [SerializeField] private float sunYAngle = 170f;
    [SerializeField] private AnimationCurve lightIntensityCurve = AnimationCurve.Linear(0f, 0.3f, 1f, 0.3f);

    [Header("Moon")]
    [SerializeField] private Light moonLight;
    [SerializeField] private AnimationCurve moonIntensityCurve = AnimationCurve.Linear(0f, 0.1f, 1f, 0.1f);

    [Header("Skybox")]
    [SerializeField] private Material blendedSkybox;
    [SerializeField] private AnimationCurve skyboxBlendCurve;

    private void Reset()
    {
        directionalLight = GetComponent<Light>();

        // Sun: bright at noon, dark at midnight
        lightIntensityCurve = new AnimationCurve(
            new Keyframe(0f,    0.4f),
            new Keyframe(0.25f, 1.0f),
            new Keyframe(0.5f,  0.4f),
            new Keyframe(0.75f, 0.05f),
            new Keyframe(1.0f,  0.4f)
        );

        // Moon: faint at dawn/dusk, zero at noon, peak at midnight
        moonIntensityCurve = new AnimationCurve(
            new Keyframe(0f,    0.1f),
            new Keyframe(0.25f, 0.0f),
            new Keyframe(0.5f,  0.1f),
            new Keyframe(0.75f, 0.3f),
            new Keyframe(1.0f,  0.1f)
        );

        // Skybox blend: 0=day, 1=night. Full day at noon, full night at midnight.
        skyboxBlendCurve = new AnimationCurve(
            new Keyframe(0f,    0.5f),   // dawn — mid-blend
            new Keyframe(0.25f, 0.0f),   // noon — full day
            new Keyframe(0.5f,  0.5f),   // dusk — mid-blend
            new Keyframe(0.75f, 1.0f),   // midnight — full night
            new Keyframe(1.0f,  0.5f)    // wrap to dawn
        );
    }

    private void Awake()
    {
        if (lightIntensityCurve == null || lightIntensityCurve.length == 0 ||
            moonIntensityCurve  == null || moonIntensityCurve.length  == 0 ||
            skyboxBlendCurve    == null || skyboxBlendCurve.length    == 0)
            Reset();

        if (blendedSkybox != null)
            RenderSettings.skybox = blendedSkybox;
    }

    private void Update()
    {
        if (QuotaManager.Instance == null) return;
        ApplyVisuals(QuotaManager.Instance.TimeOfDay01);
    }

    private void ApplyVisuals(float t)
    {
        if (directionalLight != null)
        {
            float xAngle = t * 360f - 90f;
            directionalLight.transform.rotation = Quaternion.Euler(xAngle, sunYAngle, 0f);
            directionalLight.intensity = lightIntensityCurve.Evaluate(t);
        }

        if (moonLight != null)
        {
            float moonX = t * 360f - 90f + 180f;
            moonLight.transform.rotation = Quaternion.Euler(moonX, sunYAngle, 0f);
            moonLight.intensity = moonIntensityCurve.Evaluate(t);
        }

        if (blendedSkybox != null)
        {
            blendedSkybox.SetFloat("_Blend", skyboxBlendCurve.Evaluate(t));
            DynamicGI.UpdateEnvironment();
        }
    }
}
