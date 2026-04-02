using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Polished main menu overlay. Shows when the network isn't active.
/// Sits on a Canvas in SampleScene (sort order 30) so it covers everything.
/// </summary>
public class MainMenuUI : MonoBehaviour
{
    [Header("Panels")]
    [SerializeField] private GameObject _menuPanel;
    [SerializeField] private GameObject _joinPanel;
    [SerializeField] private GameObject _settingsPanel;

    [Header("Main Buttons")]
    [SerializeField] private Button _hostButton;
    [SerializeField] private Button _joinButton;
    [SerializeField] private Button _settingsButton;
    [SerializeField] private Button _quitButton;

    [Header("Join Panel")]
    [SerializeField] private InputField _ipInput;
    [SerializeField] private Button _connectButton;
    [SerializeField] private Button _backButton;

    [Header("Settings Panel")]
    [SerializeField] private Button _settingsCloseButton;

    private NetworkSetup _networkSetup;
    private bool _hidden;

    private void Awake()
    {
        // Disable the old OnGUI HUD
        var netHud = FindFirstObjectByType<NetworkHUD>();
        if (netHud != null) netHud.enabled = false;

        // Cache NetworkSetup
        if (NetworkManager.Singleton != null)
            _networkSetup = NetworkManager.Singleton.GetComponent<NetworkSetup>();

        // Wire buttons
        _hostButton?.onClick.AddListener(OnHost);
        _joinButton?.onClick.AddListener(OnJoin);
        _settingsButton?.onClick.AddListener(OnSettings);
        _quitButton?.onClick.AddListener(OnQuit);
        _connectButton?.onClick.AddListener(OnConnect);
        _backButton?.onClick.AddListener(OnBack);
        _settingsCloseButton?.onClick.AddListener(OnSettingsClose);

        // Start with join/settings panels hidden
        if (_joinPanel != null) _joinPanel.SetActive(false);
        if (_settingsPanel != null) _settingsPanel.SetActive(false);

        ShowMenu();
    }

    private void Update()
    {
        // Auto-hide if network started externally (e.g. --host flag)
        if (!_hidden && NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            HideMenu();
    }

    private void ShowMenu()
    {
        _hidden = false;
        if (_menuPanel != null) _menuPanel.SetActive(true);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void HideMenu()
    {
        _hidden = true;
        if (_menuPanel != null) _menuPanel.SetActive(false);
        if (_joinPanel != null) _joinPanel.SetActive(false);
        if (_settingsPanel != null) _settingsPanel.SetActive(false);
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    // ── Button Handlers ─────────────────────────────────────────────────

    private void OnHost()
    {
        if (_networkSetup == null && NetworkManager.Singleton != null)
            _networkSetup = NetworkManager.Singleton.GetComponent<NetworkSetup>();

        _networkSetup?.StartHost();
        HideMenu();
    }

    private void OnJoin()
    {
        if (_joinPanel != null) _joinPanel.SetActive(true);
    }

    private void OnConnect()
    {
        if (_networkSetup == null && NetworkManager.Singleton != null)
            _networkSetup = NetworkManager.Singleton.GetComponent<NetworkSetup>();

        string ip = _ipInput != null ? _ipInput.text : "127.0.0.1";
        if (string.IsNullOrWhiteSpace(ip)) ip = "127.0.0.1";

        _networkSetup?.StartClient(ip);
        HideMenu();
    }

    private void OnBack()
    {
        if (_joinPanel != null) _joinPanel.SetActive(false);
    }

    private void OnSettings()
    {
        if (_settingsPanel != null) _settingsPanel.SetActive(true);
    }

    private void OnSettingsClose()
    {
        if (_settingsPanel != null) _settingsPanel.SetActive(false);
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
