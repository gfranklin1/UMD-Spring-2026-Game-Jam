using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Owner-only pause menu. Escape toggles the menu open/closed.
/// While open: cursor unlocked, PlayerInput + PlayerCamera disabled.
/// Reuses the SettingsPanel prefab for in-game settings.
/// </summary>
public class PauseMenu : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private Canvas   _pauseCanvas;
    [SerializeField] private GameObject _pausePanel;
    [SerializeField] private Button   _resumeButton;
    [SerializeField] private Button   _settingsButton;
    [SerializeField] private Button   _mainMenuButton;
    [SerializeField] private Transform _settingsContainer;

    [Header("Settings Prefab")]
    [SerializeField] private GameObject _settingsPanelPrefab;

    private PlayerController _playerController;
    private PlayerCamera     _playerCamera;
    private PlayerInput      _playerInput;
    private NetworkBehaviour _netBehaviour;
    private GameObject       _settingsInstance;
    private bool             _isPaused;

    private void Awake()
    {
        _playerController = GetComponentInParent<PlayerController>(true);
        _playerCamera     = GetComponentInChildren<PlayerCamera>(true);
        _playerInput      = GetComponentInParent<PlayerInput>(true);
        _netBehaviour     = GetComponentInParent<NetworkBehaviour>(true);

        _resumeButton?.onClick.AddListener(Resume);
        _settingsButton?.onClick.AddListener(OpenSettings);
        _mainMenuButton?.onClick.AddListener(OnMainMenu);

        if (_pauseCanvas != null) _pauseCanvas.enabled = false;
        if (_pausePanel  != null) _pausePanel.SetActive(false);
    }

    private void Update()
    {
        // Owner-only
        if (_netBehaviour != null && !_netBehaviour.IsOwner) return;

        // Don't allow pause during game over
        bool gameOver = QuotaManager.Instance != null && QuotaManager.Instance.IsGameOver;
        if (gameOver)
        {
            if (_isPaused) Resume();
            return;
        }

        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            if (_isPaused) Resume();
            else           Pause();
        }
    }

    public bool IsPaused => _isPaused;

    private void Pause()
    {
        _isPaused = true;

        if (_pauseCanvas != null) _pauseCanvas.enabled = true;
        if (_pausePanel  != null) _pausePanel.SetActive(true);

        // Unlock cursor and show it
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible   = true;

        // Disable player input and camera look
        if (_playerInput  != null) _playerInput.enabled  = false;
        if (_playerCamera != null) _playerCamera.enabled = false;
    }

    private void Resume()
    {
        _isPaused = false;

        // Destroy settings instance if open
        if (_settingsInstance != null)
        {
            Destroy(_settingsInstance);
            _settingsInstance = null;
        }

        if (_pausePanel  != null) _pausePanel.SetActive(false);
        if (_pauseCanvas != null) _pauseCanvas.enabled = false;

        // Re-lock cursor
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible   = false;

        // Re-enable player input and camera look
        if (_playerInput  != null) _playerInput.enabled  = true;
        if (_playerCamera != null) _playerCamera.enabled = true;
    }

    private void OpenSettings()
    {
        if (_settingsPanelPrefab == null || _settingsContainer == null) return;

        if (_settingsInstance == null)
        {
            _settingsInstance = Instantiate(_settingsPanelPrefab, _settingsContainer);

            // Ensure it fills the container
            var rt = _settingsInstance.GetComponent<RectTransform>();
            if (rt != null)
            {
                rt.anchorMin     = Vector2.zero;
                rt.anchorMax     = Vector2.one;
                rt.offsetMin     = Vector2.zero;
                rt.offsetMax     = Vector2.zero;
                rt.localScale    = Vector3.one;
                rt.localPosition = Vector3.zero;
            }
        }

        _settingsInstance.SetActive(true);
        var controller = _settingsInstance.GetComponent<SettingsMenuController>();
        controller?.InitUI();

        // Hide pause buttons while settings are open
        if (_pausePanel != null) _pausePanel.SetActive(false);
    }

    private void LateUpdate()
    {
        // Show pause panel again when settings is closed via its own Back button
        if (_isPaused && _settingsInstance != null && !_settingsInstance.activeSelf)
        {
            if (_pausePanel != null) _pausePanel.SetActive(true);
            Destroy(_settingsInstance);
            _settingsInstance = null;
        }
    }

    private void OnMainMenu()
    {
        Resume(); // Clean up pause state first
        GameManager.Instance?.LeaveGame();
    }
}
