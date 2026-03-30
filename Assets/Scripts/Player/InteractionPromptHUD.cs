using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Screen-space interaction prompt that shows context-aware hints for the nearest interactable.
/// Instant interactions show plain text; hold interactions show a Fortnite-style radial fill ring.
/// Only active for the owning player.
/// </summary>
public class InteractionPromptHUD : MonoBehaviour
{
    [SerializeField] private PlayerController _player;
    [SerializeField] private CanvasGroup      _canvasGroup;   // on the panel root — drives fade
    [SerializeField] private Text             _promptText;    // prompt label
    [SerializeField] private GameObject       _radialRing;    // shown for hold interactions only
    [SerializeField] private Image            _radialFill;    // fillAmount 0→1 during hold
    [SerializeField] private float            _fadeSpeed = 6f;

    private void Start()
    {
        bool networked = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        if (networked && _player != null && !_player.IsOwner)
        {
            enabled = false;
            if (_canvasGroup != null) _canvasGroup.alpha = 0f;
            return;
        }

        if (_canvasGroup != null) _canvasGroup.alpha = 0f;
        if (_radialRing  != null) _radialRing.SetActive(false);
    }

    private void Update()
    {
        if (_canvasGroup == null) return;

        var nearest = _player != null ? _player.NearestInteractable : null;
        float targetAlpha = nearest != null ? 1f : 0f;
        _canvasGroup.alpha = Mathf.MoveTowards(_canvasGroup.alpha, targetAlpha, _fadeSpeed * Time.deltaTime);

        if (nearest == null) return;

        // Update prompt text
        if (_promptText != null)
            _promptText.text = nearest.GetPromptText(_player);

        // Show/hide ring and update fill
        bool isHold = nearest.HoldDurationFor(_player) > 0f;
        if (_radialRing != null) _radialRing.SetActive(isHold);

        if (isHold && _radialFill != null)
        {
            float progress = _player.InteractHoldProgress;
            _radialFill.fillAmount = progress < 0f ? 0f : progress;
        }
    }
}
