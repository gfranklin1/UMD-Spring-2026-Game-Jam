using UnityEngine;

public class BuyUI : MonoBehaviour
{
    [SerializeField] private PlayerController _player;
    [SerializeField] private PlayerInventory _inventory;
    [SerializeField] private GameObject _panel;
    [SerializeField] private GameObject _noMoney;


    //public UnityEvent<IUpgrade> TriggerBuy;
    void Start()
    {
        _panel.SetActive(false);

        if (_player == null) { enabled = false; return; }

        // Only wire up on the local player's HUD — non-owner instances are disabled by PlayerHUD
        bool networked = Unity.Netcode.NetworkManager.Singleton != null
                      && Unity.Netcode.NetworkManager.Singleton.IsListening;
        if (networked && !_player.IsOwner) { enabled = false; return; }

        _player.OnOpenBuyScreen  += OnOpen;
        _player.OnCloseBuyScreen += OnClose;
    }

    void OnOpen()
    {
        _panel.SetActive(true);
    }

    void OnClose()
    {
        _panel?.SetActive(false);
    }
}
