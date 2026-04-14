using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public enum WaterColorMode { Tropical, DeepOcean, Murky }

/// <summary>
/// Controls underwater visuals for the local player.
///
/// Distance fog: drives UnderwaterFogFeature via Shader.SetGlobal* each frame.
///   - Fog density grows with depth → objects farther away fade to water colour.
///   - _UnderwaterFogWeight mirrors _volume.weight for a smooth surface transition.
///
/// Post-processing volume (still used for close-up effects):
///   - Subtle colour tint (not a flat override — much lighter than before)
///   - Vignette, chromatic aberration
///   - postExposure scales with depth → sky fades to black at fullDarknessDepth
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(PlayerController))]
public class UnderwaterEffect : MonoBehaviour
{
    [Header("Water Color Mode")]
    [SerializeField] private WaterColorMode colorMode     = WaterColorMode.Tropical;
    [SerializeField] private Material       oceanMaterial;

    [Header("Transition")]
    [SerializeField] private float transitionSpeed = 3f;

    [Header("Distance Fog")]
    [SerializeField] private float minFogDensity  = 0.02f;   // at water surface (~50 m range)
    [SerializeField] private float maxFogDensity  = 0.20f;   // at fullDarknessDepth (~5 m range)
    [SerializeField] private float fogStartOffset = 1f;      // metres before fog begins

    [Header("Post-Processing (close-up)")]
    [SerializeField] private float  hueShift          = -10f;
    [SerializeField] private float  saturation         = 15f;
    [SerializeField] private float  vignetteIntensity  = 0.32f;
    [SerializeField] private float  vignetteSmoothness = 0.80f;
    [SerializeField] private float  chromaticIntensity = 0.20f;

    [Header("Depth Scaling")]
    [SerializeField] private float fullDarknessDepth = 25f;   // metres for max fog + blackout

    // ─── Shader global IDs ────────────────────────────────────────────────────
    private static readonly int s_FogColor    = Shader.PropertyToID("_UnderwaterFogColor");
    private static readonly int s_FogDensity  = Shader.PropertyToID("_UnderwaterFogDensity");
    private static readonly int s_FogOffset   = Shader.PropertyToID("_UnderwaterFogOffset");
    private static readonly int s_FogWeight   = Shader.PropertyToID("_UnderwaterFogWeight");
    private static readonly int s_WaterSurfY  = Shader.PropertyToID("_WaterSurfaceY");

    // ─── Runtime ──────────────────────────────────────────────────────────────
    private PlayerController    _player;
    private CharacterController _cc;
    private OceanWaves          _oceanWaves;
    private Volume              _volume;
    private float               _targetWeight;

    private ColorAdjustments    _colorAdjustments;
    private Vignette            _vignette;
    private ChromaticAberration _chromaticAberration;

    private void Awake()
    {
        _player = GetComponent<PlayerController>();
        _cc     = GetComponent<CharacterController>();
        BuildVolume();
    }

    private void Start()
    {
        _oceanWaves = FindAnyObjectByType<OceanWaves>();
    }

    private void Update()
    {
        // Only drive visuals for the local player
        bool isLocal = NetworkManager.Singleton == null
                    || !NetworkManager.Singleton.IsListening
                    || _player.IsOwner;
        if (!isLocal)
        {
            Shader.SetGlobalFloat(s_FogDensity, 0f);
            return;
        }

        // ── Determine underwater state and depth ──────────────────────────────
        bool  headUnder;
        float depth = 0f;

        if (_player.IsDead && _oceanWaves != null)
        {
            var   cam = _player.CameraRoot;
            float wh  = cam != null ? _oceanWaves.GetWaveHeight(cam.position) : 0f;
            headUnder = cam != null && cam.position.y < wh;
            if (headUnder && cam != null) depth = Mathf.Max(0f, wh - cam.position.y);
        }
        else
        {
            headUnder = _player.IsHeadUnderwater;
            if (headUnder && _oceanWaves != null)
            {
                float wh    = _oceanWaves.GetWaveHeight(transform.position);
                float headY = transform.position.y + (_cc != null ? _cc.height * 0.5f : 0f);
                depth = Mathf.Max(0f, wh - headY);
            }
        }

        // ── Smooth volume weight (handles surface-crossing transition) ─────────
        _targetWeight  = headUnder ? 1f : 0f;
        _volume.weight = Mathf.MoveTowards(_volume.weight, _targetWeight, transitionSpeed * Time.deltaTime);

        // ── Depth ratio ───────────────────────────────────────────────────────
        float depthT = Mathf.Clamp01(depth / fullDarknessDepth);

        // ── Drive distance fog via global shader properties ───────────────────
        float waterSurfaceY = _oceanWaves != null
            ? _oceanWaves.GetWaveHeight(transform.position)
            : 0f;
        // Density scales with depth when underwater; above water uses minFogDensity (for looking-in fog)
        float fogDensity = headUnder
            ? Mathf.Lerp(minFogDensity, maxFogDensity, depthT)
            : minFogDensity;
        var (fogCol, _, _, _, _) = GetPresetValues(colorMode);
        Shader.SetGlobalColor(s_FogColor,   fogCol);
        Shader.SetGlobalFloat(s_FogOffset,  fogStartOffset);
        Shader.SetGlobalFloat(s_FogDensity, fogDensity);
        Shader.SetGlobalFloat(s_FogWeight,  _volume.weight);
        Shader.SetGlobalFloat(s_WaterSurfY, waterSurfaceY);

        // ── Post-processing: depth-scaled close-up effects ────────────────────
        if (_colorAdjustments != null)
        {
            var (_, baseExposure, _, _, _) = GetPresetValues(colorMode);
            _colorAdjustments.postExposure.Override(
                Mathf.Lerp(baseExposure, -3.5f, depthT) * _volume.weight);
        }
    }

    private void OnValidate() => ApplyColorMode();

    private void BuildVolume()
    {
        // Destroy any stale volume left from a previous edit-mode reload ([ExecuteAlways])
        var existing = transform.Find("UnderwaterVolume");
        if (existing != null) DestroyImmediate(existing.gameObject);

        var go = new GameObject("UnderwaterVolume");
        go.hideFlags = HideFlags.DontSave; // never serialized into the prefab asset
        go.transform.SetParent(transform);

        _volume          = go.AddComponent<Volume>();
        _volume.isGlobal = true;
        _volume.priority = 10;
        _volume.weight   = 0f;

        var profile = ScriptableObject.CreateInstance<VolumeProfile>();

        // Subtle colour adjustments — NOT a flat tint, just a mild shift
        _colorAdjustments          = profile.Add<ColorAdjustments>(true);
        _colorAdjustments.active   = true;
        _colorAdjustments.hueShift.Override(hueShift);
        _colorAdjustments.saturation.Override(saturation);
        // postExposure set in Update; no colorFilter override (avoids uniform tint)

        _vignette            = profile.Add<Vignette>(true);
        _vignette.active     = true;
        _vignette.intensity.Override(vignetteIntensity);
        _vignette.smoothness.Override(vignetteSmoothness);
        _vignette.rounded.Override(true);

        _chromaticAberration           = profile.Add<ChromaticAberration>(true);
        _chromaticAberration.active    = true;
        _chromaticAberration.intensity.Override(chromaticIntensity);

        _volume.profile = profile;

        ApplyColorMode();
    }

    private void ApplyColorMode()
    {
        if (_vignette == null) return;

        var (_, exposure, vigCol, baseColor, deepColor) = GetPresetValues(colorMode);

        _vignette.color.Override(vigCol);
        if (_colorAdjustments != null)
            _colorAdjustments.postExposure.Override(exposure);

        // Also push fog colour globally right away (editor preview)
        var (fogCol, _, _, _, _) = GetPresetValues(colorMode);
        Shader.SetGlobalColor(s_FogColor, fogCol);

        if (oceanMaterial != null)
        {
            oceanMaterial.SetColor("_BaseColor", baseColor);
            oceanMaterial.SetColor("_DeepColor",  deepColor);
        }
    }

    /// <summary>
    /// Returns (fogColor, baseExposure, vignetteColor, oceanBaseColor, oceanDeepColor).
    /// fogColor doubles as the water-tint the distance fog blends objects toward.
    /// </summary>
    private static (Color fogColor, float exposure, Color vigCol, Color baseColor, Color deepColor)
        GetPresetValues(WaterColorMode mode) => mode switch
    {
        WaterColorMode.DeepOcean => (
            new Color(0.02f, 0.08f, 0.28f),   // dark blue fog
            -1.2f,
            new Color(0f, 0.02f, 0.15f),
            new Color(0.02f, 0.06f, 0.25f, 0.95f),
            new Color(0.005f, 0.02f, 0.10f, 1.0f)),
        WaterColorMode.Murky => (
            new Color(0.06f, 0.18f, 0.08f),   // murky green fog
            -0.8f,
            new Color(0f, 0.12f, 0.05f),
            new Color(0.05f, 0.22f, 0.10f, 0.95f),
            new Color(0.02f, 0.10f, 0.04f, 1.0f)),
        _ => (                                // Tropical
            new Color(0.03f, 0.14f, 0.38f),   // mid-blue fog
            -0.6f,
            new Color(0f, 0.15f, 0.40f),
            new Color(0.04f, 0.20f, 0.42f, 0.92f),
            new Color(0.01f, 0.08f, 0.20f, 1.0f)),
    };

    private void OnDestroy()
    {
        // Clear fog when this player is destroyed (avoids fog sticking on screen)
        Shader.SetGlobalFloat(s_FogDensity, 0f);

        if (_volume != null && _volume.profile != null)
            Destroy(_volume.profile);
    }
}
