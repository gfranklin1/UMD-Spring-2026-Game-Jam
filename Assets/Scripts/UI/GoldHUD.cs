using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Displays the global shared gold pool (from GoldTracker) in the upper-right corner.
/// Subscribes to GoldTracker.OnGoldChanged — safe to be on any player's HUD.
/// </summary>
public class GoldHUD : MonoBehaviour
{
    [SerializeField] private Text _goldText;

    private void Start()
    {
        // GoldTracker may not exist yet if this runs before network spawn
        TrySubscribe();
        Refresh();
    }

    private void OnEnable()  => TrySubscribe();
    private void OnDisable() => TryUnsubscribe();

    private void TrySubscribe()
    {
        if (GoldTracker.Instance != null)
            GoldTracker.Instance.OnGoldChanged += Refresh;
    }

    private void TryUnsubscribe()
    {
        if (GoldTracker.Instance != null)
            GoldTracker.Instance.OnGoldChanged -= Refresh;
    }

    // Poll each frame until GoldTracker spawns, then subscribe
    private void Update()
    {
        if (GoldTracker.Instance != null)
        {
            TrySubscribe();
            Refresh();
            enabled = false; // stop polling once connected
        }
    }

    private void Refresh()
    {
        if (_goldText != null)
            _goldText.text = $"Gold: {GoldTracker.Instance?.TotalGold ?? 0}";
    }
}
