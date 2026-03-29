using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Two-state crosshair:
///   • Default  — small white dot (·)
///   • Loot     — gold open circle (○) to signal "you can pick this up"
///
/// Only active for the owning player (same ownership guard as PlayerHUD).
/// </summary>
public class CrosshairHUD : MonoBehaviour
{
    [SerializeField] private PlayerController _player;
    [SerializeField] private Text             _dotText;   // always-on center dot
    [SerializeField] private Text             _ringText;  // open circle — shown when loot is targeted

    private void Start()
    {
        bool networked = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        if (networked && _player != null && !_player.IsOwner)
        {
            enabled = false;
            if (_dotText  != null) _dotText.gameObject.SetActive(false);
            if (_ringText != null) _ringText.gameObject.SetActive(false);
            return;
        }

        if (_ringText != null) _ringText.gameObject.SetActive(false);
    }

    private void Update()
    {
        if (_player == null) return;

        bool hasLoot = _player.NearestLoot != null;

        if (_dotText  != null) _dotText.gameObject.SetActive(!hasLoot);
        if (_ringText != null) _ringText.gameObject.SetActive(hasLoot);
    }
}
