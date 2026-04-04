using UnityEngine;

/// <summary>
/// Static store for settings PlayerPrefs keys and apply methods.
/// Mirrors the pattern of NetworkLauncher — pure static, no MonoBehaviour,
/// no DontDestroyOnLoad needed.
/// </summary>
public static class SettingsManager
{
    // ── PlayerPrefs Keys ──────────────────────────────────────────────────────

    public const string KEY_MOUSE_SENSITIVITY = "MouseSensitivity";
    public const string KEY_MASTER_VOLUME     = "MasterVolume";
    public const string KEY_FULLSCREEN        = "Fullscreen";
    public const string KEY_RESOLUTION_INDEX  = "ResolutionIndex";
    public const string KEY_NIGHT_SKYBOX      = "NightSkybox";

    // ── Defaults ──────────────────────────────────────────────────────────────

    public const float DEFAULT_SENSITIVITY      = 0.15f;
    public const float DEFAULT_VOLUME           = 1f;
    public const bool  DEFAULT_FULLSCREEN       = true;
    public const int   DEFAULT_RESOLUTION_INDEX = -1;
    public const int   DEFAULT_NIGHT_SKYBOX     = 0; // 0 = Cold Night

    // ── Getters ───────────────────────────────────────────────────────────────

    public static float GetSensitivity()      => PlayerPrefs.GetFloat(KEY_MOUSE_SENSITIVITY, DEFAULT_SENSITIVITY);
    public static float GetVolume()           => PlayerPrefs.GetFloat(KEY_MASTER_VOLUME, DEFAULT_VOLUME);
    public static bool  GetFullscreen()       => PlayerPrefs.GetInt(KEY_FULLSCREEN, DEFAULT_FULLSCREEN ? 1 : 0) == 1;
    public static int   GetResolutionIndex()  => PlayerPrefs.GetInt(KEY_RESOLUTION_INDEX, DEFAULT_RESOLUTION_INDEX);
    public static int   GetNightSkyboxIndex() => PlayerPrefs.GetInt(KEY_NIGHT_SKYBOX, DEFAULT_NIGHT_SKYBOX);

    // ── Individual Apply ──────────────────────────────────────────────────────

    public static void ApplyVolume(float volume)
    {
        AudioListener.volume = Mathf.Clamp01(volume);
    }

    public static void ApplyFullscreen(bool fullscreen)
    {
        Screen.fullScreen = fullscreen;
    }

    public static void ApplyResolution(int deduplicatedIndex)
    {
        var resolutions = GetDeduplicatedResolutions();
        if (deduplicatedIndex < 0 || deduplicatedIndex >= resolutions.Length) return;
        var r = resolutions[deduplicatedIndex];
        Screen.SetResolution(r.width, r.height, Screen.fullScreen);
    }

    // ── Apply All (call on startup) ───────────────────────────────────────────

    /// <summary>
    /// Reads all settings from PlayerPrefs and applies them.
    /// Called from MainMenuController.Awake() on every menu load.
    /// </summary>
    public static void ApplyAll()
    {
        ApplyVolume(GetVolume());
        ApplyFullscreen(GetFullscreen());
        ApplyResolution(GetResolutionIndex());
        // Sensitivity is applied per-camera in Awake — no global apply needed.
    }

    // ── Save Helpers ──────────────────────────────────────────────────────────

    public static void SaveSensitivity(float value)      => PlayerPrefs.SetFloat(KEY_MOUSE_SENSITIVITY, Mathf.Clamp(value, 0.05f, 0.5f));
    public static void SaveVolume(float value)           => PlayerPrefs.SetFloat(KEY_MASTER_VOLUME, Mathf.Clamp01(value));
    public static void SaveFullscreen(bool value)        => PlayerPrefs.SetInt(KEY_FULLSCREEN, value ? 1 : 0);
    public static void SaveResolutionIndex(int index)    => PlayerPrefs.SetInt(KEY_RESOLUTION_INDEX, index);
    public static void SaveNightSkyboxIndex(int value)   => PlayerPrefs.SetInt(KEY_NIGHT_SKYBOX, Mathf.Clamp(value, 0, 1));
    public static void SaveAll()                         => PlayerPrefs.Save();

    // ── Resolution Utilities ──────────────────────────────────────────────────

    /// <summary>
    /// Returns Screen.resolutions deduplicated by width x height.
    /// Windows returns multiple entries per resolution (one per refresh rate);
    /// we keep the last (highest refresh) for each width+height pair.
    /// </summary>
    public static Resolution[] GetDeduplicatedResolutions()
    {
        var all    = Screen.resolutions;
        var seen   = new System.Collections.Generic.HashSet<long>();
        var result = new System.Collections.Generic.List<Resolution>();

        // Iterate in reverse so we keep the highest refresh rate per size
        for (int i = all.Length - 1; i >= 0; i--)
        {
            long key = ((long)all[i].width << 32) | (uint)all[i].height;
            if (seen.Add(key))
                result.Add(all[i]);
        }

        result.Reverse(); // restore ascending order by size
        return result.ToArray();
    }

    /// <summary>
    /// Returns the index in the deduplicated list matching the current screen size.
    /// Falls back to the last entry (typically highest resolution) if no match.
    /// </summary>
    public static int GetCurrentResolutionIndex()
    {
        var resolutions = GetDeduplicatedResolutions();
        int w = Screen.width;
        int h = Screen.height;
        for (int i = 0; i < resolutions.Length; i++)
        {
            if (resolutions[i].width == w && resolutions[i].height == h)
                return i;
        }
        return Mathf.Max(0, resolutions.Length - 1);
    }
}
