using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Game over overlay. Scene-root canvas (not on player prefab) so all clients see it.
/// Polls for QuotaManager, activates when game over triggers.
/// </summary>
public class GameOverUI : MonoBehaviour
{
    [SerializeField] private GameObject _panel;
    [SerializeField] private Text _titleText;
    [SerializeField] private Text _statsText;
    [SerializeField] private Button _restartButton;
    [SerializeField] private Button _mainMenuButton;
    [SerializeField] private Button _quitButton;

    private bool _subscribed;

    private void Start()
    {
        if (_panel != null) _panel.SetActive(false);
        _restartButton?.onClick.AddListener(OnRestart);
        _mainMenuButton?.onClick.AddListener(OnMainMenu);
        _quitButton?.onClick.AddListener(OnQuit);
    }

    private void Update()
    {
        if (_subscribed) return;
        if (QuotaManager.Instance == null) return;

        _subscribed = true;
        QuotaManager.Instance.OnGameOverTriggered += ShowGameOver;
        QuotaManager.Instance.OnGameReset         += HideGameOver;

        // In case we missed the event (late join after game over)
        if (QuotaManager.Instance.IsGameOver)
            ShowGameOver();
    }

    private void OnDestroy()
    {
        if (QuotaManager.Instance != null)
        {
            QuotaManager.Instance.OnGameOverTriggered -= ShowGameOver;
            QuotaManager.Instance.OnGameReset         -= HideGameOver;
        }
    }

    private void ShowGameOver()
    {
        if (_panel == null) return;
        _panel.SetActive(true);

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible   = true;

        // Only host can restart — non-hosts just see the stats
        bool isHost = NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost;
        if (_restartButton != null) _restartButton.gameObject.SetActive(isHost);

        var qm = QuotaManager.Instance;
        if (qm == null) return;

        bool quotaFail = qm.GameOverReason == 0;
        if (_titleText != null)
            _titleText.text = quotaFail ? "QUOTA FAILED" : "CREW LOST";

        int daysSurvived = (qm.CurrentCycle * qm.DaysPerCycle) + qm.CurrentDay;
        int currentGold  = GoldTracker.Instance?.TotalGold ?? 0;

        if (_statsText != null)
        {
            _statsText.text =
                $"Quota Target: {qm.CurrentQuotaTarget}g\n" +
                $"Gold Collected: {currentGold}g\n" +
                $"Days Survived: {daysSurvived}\n" +
                $"Total Gold Earned: {qm.TotalGoldEarned}g";
        }
    }

    private void HideGameOver()
    {
        if (_panel != null) _panel.SetActive(false);
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible   = false;
    }

    private void OnRestart()
    {
        QuotaManager.Instance?.ResetGame();
    }

    private void OnMainMenu()
    {
        GameManager.Instance?.RestartGame();
    }

    private void OnQuit()
    {
        Application.Quit();
    }
}
