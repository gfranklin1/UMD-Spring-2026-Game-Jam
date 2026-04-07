using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Main menu UI for the standalone MainMenu scene.
/// No NetworkManager dependency — uses NetworkLauncher to carry host/join
/// intent into the gameplay scene where NetworkSetup picks it up in Start().
/// </summary>
public class MainMenuController : MonoBehaviour
{
    [Header("Panels")]
    [SerializeField] private GameObject _menuPanel;
    [SerializeField] private GameObject _joinPanel;
    [SerializeField] private GameObject _settingsPanel;

    [Header("Player Name")]
    [SerializeField] private InputField _nameInput;

    [Header("Main Buttons")]
    [SerializeField] private Button _hostButton;
    [SerializeField] private Button _joinButton;
    [SerializeField] private Button _settingsButton;
    [SerializeField] private Button _controlsButton;
    [SerializeField] private Button _quitButton;

    [Header("Join Panel")]
    [SerializeField] private InputField _ipInput;
    [SerializeField] private Button     _connectButton;
    [SerializeField] private Button     _backButton;

    [Header("Settings Panel")]
    [SerializeField] private Button                _settingsCloseButton;
    [SerializeField] private SettingsMenuController _settingsMenuController;

    [Header("Controls Panel")]
    [SerializeField] private GameObject _controlsPanel;

    private void Awake()
    {
        SettingsManager.ApplyAll();

        // onClick events are wired as persistent listeners in the scene.
        // No AddListener needed here — avoids double-firing.
        // Exception: controls button is wired here since it's added after initial setup.
        _controlsButton?.onClick.AddListener(OnControls);

        if (_joinPanel     != null) _joinPanel.SetActive(false);
        if (_settingsPanel != null) _settingsPanel.SetActive(false);
        if (_controlsPanel != null) _controlsPanel.SetActive(false);
        if (_menuPanel     != null) _menuPanel.SetActive(true);

        if (_nameInput != null)
            _nameInput.text = PlayerPrefs.HasKey("PlayerName")
                ? PlayerPrefs.GetString("PlayerName", "")
                : NetworkLauncher.PlayerName;

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible   = true;
    }

    // ── Button Handlers ──────────────────────────────────────────────────────

    private void OnHost()
    {
        SaveAndApplyName();
        NetworkLauncher.SetHost();
        SceneManager.LoadScene(1);
    }

    private void OnJoin()
    {
        if (_joinPanel != null) _joinPanel.SetActive(true);
    }

    private void OnConnect()
    {
        SaveAndApplyName();
        string ip = _ipInput != null ? _ipInput.text : "127.0.0.1";
        NetworkLauncher.SetClient(ip);
        SceneManager.LoadScene(1);
    }

    private void OnBack()
    {
        if (_joinPanel != null) _joinPanel.SetActive(false);
    }

    private void OnSettings()
    {
        _settingsMenuController?.InitUI();
        if (_settingsPanel != null) _settingsPanel.SetActive(true);
    }

    private void OnSettingsClose()
    {
        if (_settingsPanel != null) _settingsPanel.SetActive(false);
    }

    private void OnControls()
    {
        if (_controlsPanel != null) _controlsPanel.SetActive(true);
    }

    private void OnControlsClose()
    {
        if (_controlsPanel != null) _controlsPanel.SetActive(false);
    }

    private void SaveAndApplyName()
    {
        string n = _nameInput != null ? _nameInput.text : "";
        PlayerPrefs.SetString("PlayerName", n);
        NetworkLauncher.SetPlayerName(n);
    }

    private void OnQuit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
