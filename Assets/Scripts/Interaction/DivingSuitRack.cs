using UnityEngine;

//[RequireComponent(typeof(Collider))]
public class DivingSuitRack : MonoBehaviour, IInteractable
{
    private bool _suitAvailable = true;

    /// The player who currently has this suit equipped (null when available).
    public PlayerController OccupyingPlayer { get; private set; }

    public string GetPromptText() => _suitAvailable ? "[Hold E] Equip Suit" : "Suit in use";

    public void OnInteractStart(PlayerController player) { }

    public void OnInteractHold(PlayerController player)
    {
        if (!_suitAvailable) return;
        _suitAvailable = false;
        OccupyingPlayer = player;
        player.EquipSuit(this);
    }

    public void OnInteractCancel(PlayerController player) { }

    public void Release(PlayerController player) { }

    public void ReturnSuit()
    {
        _suitAvailable = true;
        OccupyingPlayer = null;
    }
}
