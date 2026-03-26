using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(PlayerController))]
public class PlayerHUD : NetworkBehaviour
{
    [SerializeField] private GameObject _hudCanvas;
    [SerializeField] private RectTransform _healthFillRT;
    [SerializeField] private RectTransform _oxygenFillRT;

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
    }
}
