using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(PlayerController))]
public class PlayerHUD : NetworkBehaviour
{
    [SerializeField] private GameObject    _hudCanvas;
    [SerializeField] private RectTransform _healthFillRT;
    [SerializeField] private RectTransform _oxygenFillRT;
    [SerializeField] private Text          _bootPromptText;    // "[Hold Q] Kick off boots"
    [SerializeField] private RectTransform _bootKickFillRT;    // progress bar while holding Q

    private PlayerController _player;

    private void Start()
    {
        // Single-player / non-networked: activate canvas immediately
        bool networked = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        if (!networked)
        {
            _player = GetComponent<PlayerController>();
            if (_hudCanvas != null) _hudCanvas.SetActive(true);
        }
    }

    public override void OnNetworkSpawn()
    {
        if (!IsOwner)
        {
            if (_hudCanvas != null) _hudCanvas.SetActive(false);
            enabled = false;
            return;
        }
        _player = GetComponent<PlayerController>();
        if (_hudCanvas != null) _hudCanvas.SetActive(true);
    }

    public override void OnNetworkDespawn()
    {
        if (_hudCanvas != null)
            Destroy(_hudCanvas);
    }

    private void Update()
    {
        if (_healthFillRT == null) return;
        float hp = Mathf.Clamp01(_player.Health / _player.MaxHealth);
        float ox = Mathf.Clamp01(_player.Oxygen / _player.OxygenCapacity);
        _healthFillRT.anchorMax = new Vector2(hp, 1f);
        _oxygenFillRT.anchorMax = new Vector2(ox, 1f);

        // Boot kick-off prompt and progress
        float kickProgress = _player.BootKickProgress;  // -1 = not available, 0–1 = in progress
        bool showBoot = kickProgress >= 0f;
        if (_bootPromptText != null) _bootPromptText.text    = showBoot ? "[Hold Q] Kick off boots" : "";
        if (_bootKickFillRT  != null) _bootKickFillRT.gameObject.SetActive(showBoot && kickProgress > 0f);
        if (_bootKickFillRT  != null && showBoot)
            _bootKickFillRT.anchorMax = new Vector2(Mathf.Clamp01(kickProgress), 1f);
    }
}
