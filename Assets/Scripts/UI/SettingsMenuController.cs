using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Drives the SettingsPanel UI in the MainMenu scene.
/// Attach to the SettingsPanel GameObject and wire all fields in the Inspector.
/// MainMenuController.OnSettings() calls InitUI() before showing the panel.
/// </summary>
public class SettingsMenuController : MonoBehaviour
{
    [Header("Sensitivity")]
    [SerializeField] private Slider _sensitivitySlider;
    [SerializeField] private Text   _sensitivityValue;

    [Header("Volume")]
    [SerializeField] private Slider _volumeSlider;
    [SerializeField] private Text   _volumeValue;

    [Header("Display")]
    [SerializeField] private Toggle   _fullscreenToggle;
    [SerializeField] private Dropdown _resolutionDropdown;

    [Header("Buttons")]
    [SerializeField] private Button _applyButton;
    [SerializeField] private Button _resetButton;
    [SerializeField] private Button _backButton;

    private Resolution[] _resolutions;

    private void Awake()
    {
        if (_sensitivitySlider != null) _sensitivitySlider.onValueChanged.AddListener(OnSensitivityChanged);
        if (_volumeSlider      != null) _volumeSlider.onValueChanged.AddListener(OnVolumeChanged);
        if (_applyButton       != null) _applyButton.onClick.AddListener(OnApply);
        if (_resetButton       != null) _resetButton.onClick.AddListener(OnResetDefaults);
        if (_backButton        != null) _backButton.onClick.AddListener(OnBack);
    }

    /// <summary>
    /// Called by MainMenuController.OnSettings() before SetActive(true).
    /// Refreshes all controls from saved PlayerPrefs every time the panel opens.
    /// </summary>
    public void InitUI()
    {
        // ── Resolution Dropdown ──────────────────────────────────────────────
        _resolutions = SettingsManager.GetDeduplicatedResolutions();
        if (_resolutionDropdown != null)
        {
            _resolutionDropdown.ClearOptions();
            var options = new List<Dropdown.OptionData>();
            foreach (var r in _resolutions)
                options.Add(new Dropdown.OptionData($"{r.width} x {r.height}"));
            _resolutionDropdown.AddOptions(options);

            int saved = SettingsManager.GetResolutionIndex();
            int index = (saved >= 0 && saved < _resolutions.Length)
                ? saved
                : SettingsManager.GetCurrentResolutionIndex();
            _resolutionDropdown.value = index;
            _resolutionDropdown.RefreshShownValue();
        }

        // ── Sensitivity Slider ───────────────────────────────────────────────
        if (_sensitivitySlider != null)
        {
            _sensitivitySlider.minValue = 0.05f;
            _sensitivitySlider.maxValue = 0.5f;
            _sensitivitySlider.value    = SettingsManager.GetSensitivity();
        }
        UpdateSensitivityLabel(SettingsManager.GetSensitivity());

        // ── Volume Slider ────────────────────────────────────────────────────
        if (_volumeSlider != null)
        {
            _volumeSlider.minValue = 0f;
            _volumeSlider.maxValue = 1f;
            _volumeSlider.value    = SettingsManager.GetVolume();
        }
        UpdateVolumeLabel(SettingsManager.GetVolume());

        // ── Fullscreen Toggle ────────────────────────────────────────────────
        if (_fullscreenToggle != null)
            _fullscreenToggle.isOn = SettingsManager.GetFullscreen();
    }

    // ── Live callbacks ────────────────────────────────────────────────────────

    private void OnSensitivityChanged(float value) => UpdateSensitivityLabel(value);

    private void OnVolumeChanged(float value)
    {
        UpdateVolumeLabel(value);
        SettingsManager.ApplyVolume(value);
    }

    private void UpdateSensitivityLabel(float value)
    {
        if (_sensitivityValue != null)
            _sensitivityValue.text = value.ToString("F2");
    }

    private void UpdateVolumeLabel(float value)
    {
        if (_volumeValue != null)
            _volumeValue.text = Mathf.RoundToInt(value * 100f) + "%";
    }

    // ── Button handlers ───────────────────────────────────────────────────────

    private void OnApply()
    {
        if (_sensitivitySlider  != null) SettingsManager.SaveSensitivity(_sensitivitySlider.value);
        if (_volumeSlider       != null) SettingsManager.SaveVolume(_volumeSlider.value);
        if (_fullscreenToggle   != null) SettingsManager.SaveFullscreen(_fullscreenToggle.isOn);
        if (_resolutionDropdown != null) SettingsManager.SaveResolutionIndex(_resolutionDropdown.value);

        SettingsManager.SaveAll();

        if (_volumeSlider       != null) SettingsManager.ApplyVolume(_volumeSlider.value);
        if (_fullscreenToggle   != null) SettingsManager.ApplyFullscreen(_fullscreenToggle.isOn);
        if (_resolutionDropdown != null) SettingsManager.ApplyResolution(_resolutionDropdown.value);
        // Sensitivity takes effect next time PlayerCamera / SpectatorCamera Awake() runs.
    }

    private void OnResetDefaults()
    {
        if (_sensitivitySlider  != null) _sensitivitySlider.value = SettingsManager.DEFAULT_SENSITIVITY;
        if (_volumeSlider       != null) _volumeSlider.value      = SettingsManager.DEFAULT_VOLUME;
        if (_fullscreenToggle   != null) _fullscreenToggle.isOn   = SettingsManager.DEFAULT_FULLSCREEN;
        if (_resolutionDropdown != null)
        {
            int idx = SettingsManager.GetCurrentResolutionIndex();
            _resolutionDropdown.value = idx;
            _resolutionDropdown.RefreshShownValue();
        }
        // Labels update via the slider onValueChanged callbacks above
    }

    private void OnBack()
    {
        gameObject.SetActive(false);
    }
}
