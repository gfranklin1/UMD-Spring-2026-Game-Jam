using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Blends a URP post-processing volume in/out based on whether the local player is underwater.
/// Creates the volume and profile at runtime — no scene setup required.
/// </summary>
[RequireComponent(typeof(PlayerController))]
public class UnderwaterEffect : MonoBehaviour
{
    [Header("Transition")]
    [SerializeField] private float transitionSpeed = 3f;

    [Header("Color Adjustments")]
    [SerializeField] private Color  colorFilter    = new Color(0.15f, 0.55f, 1f);
    [SerializeField] private float  postExposure   = -0.6f;   // darken slightly
    [SerializeField] private float  hueShift       = -10f;    // subtle blue-green push
    [SerializeField] private float  saturation      = 20f;    // pop the blues

    [Header("Vignette")]
    [SerializeField] private float  vignetteIntensity  = 0.35f;
    [SerializeField] private float  vignetteSmoothness = 0.85f;
    [SerializeField] private Color  vignetteColor      = new Color(0f, 0.15f, 0.4f);

    [Header("Depth Fog")]
    [SerializeField] private float  fogFocusDistance = 4f;    // meters before blur starts
    [SerializeField] private float  fogAperture      = 2.5f;  // lower = more blur
    [SerializeField] private float  fogFocalLength   = 50f;

    [Header("Chromatic Aberration")]
    [SerializeField] private float  chromaticIntensity = 0.25f;

    // ─── Runtime ──────────────────────────────────────────────────────────────
    private PlayerController _player;
    private Volume           _volume;
    private float            _targetWeight;

    private void Awake()
    {
        _player = GetComponent<PlayerController>();
        BuildVolume();
    }

    private void Update()
    {
        // Only drive visuals for the local player; treat as local when not in a network session
        bool isLocal = NetworkManager.Singleton == null
                    || !NetworkManager.Singleton.IsListening
                    || _player.IsOwner;
        if (!isLocal) return;

        _targetWeight = _player.IsUnderwater ? 1f : 0f;
        _volume.weight = Mathf.MoveTowards(_volume.weight, _targetWeight, transitionSpeed * Time.deltaTime);
    }

    private void BuildVolume()
    {
        var go = new GameObject("UnderwaterVolume");
        go.transform.SetParent(transform);

        _volume = go.AddComponent<Volume>();
        _volume.isGlobal = true;
        _volume.priority = 10;   // higher than the scene's global volume (priority 0)
        _volume.weight   = 0f;

        var profile = ScriptableObject.CreateInstance<VolumeProfile>();

        // ── Color Adjustments ──────────────────────────────────────────────
        var ca = profile.Add<ColorAdjustments>(true);
        ca.active = true;
        ca.colorFilter.Override(colorFilter);
        ca.postExposure.Override(postExposure);
        ca.hueShift.Override(hueShift);
        ca.saturation.Override(saturation);

        // ── Vignette ───────────────────────────────────────────────────────
        var vg = profile.Add<Vignette>(true);
        vg.active    = true;
        vg.color.Override(vignetteColor);
        vg.intensity.Override(vignetteIntensity);
        vg.smoothness.Override(vignetteSmoothness);
        vg.rounded.Override(true);

        // ── Depth of Field (simulates underwater fog/limited visibility) ───
        var dof = profile.Add<DepthOfField>(true);
        dof.active = true;
        dof.mode.Override(DepthOfFieldMode.Gaussian);
        dof.gaussianStart.Override(fogFocusDistance);
        dof.gaussianEnd.Override(fogFocusDistance * 3f);
        dof.gaussianMaxRadius.Override(fogAperture);

        // ── Chromatic Aberration ───────────────────────────────────────────
        var chr = profile.Add<ChromaticAberration>(true);
        chr.active    = true;
        chr.intensity.Override(chromaticIntensity);

        _volume.profile = profile;
    }

    private void OnDestroy()
    {
        if (_volume != null && _volume.profile != null)
            Destroy(_volume.profile);
    }
}
