using UnityEngine;

public class BuyUI : MonoBehaviour
{
    [SerializeField] private PlayerController _player;
    [SerializeField] private PlayerInventory _inventory;
    [SerializeField] private GameObject _panel;

    void Start()
    {
        _panel.SetActive(false);

        // Only wire up on the local player's HUD — non-owner instances are disabled by PlayerHUD
        bool networked = Unity.Netcode.NetworkManager.Singleton != null
                      && Unity.Netcode.NetworkManager.Singleton.IsListening;
        if (networked && _player != null && !_player.IsOwner)
        {
            enabled = false;
            return;
        }

        _player.OnOpenBuyScreen += OnOpen;
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

    void Buy(GameObject item)
    {
        return;
    }
}
