using UnityEngine;

[RequireComponent(typeof(Collider))]
public class DivingSuitRack : MonoBehaviour, IInteractable
{
    private bool _suitAvailable = true;

    public string GetPromptText() => _suitAvailable ? "[Hold E] Equip Suit" : "Suit in use";

    public void OnInteractStart(PlayerController player) { }

    public void OnInteractHold(PlayerController player)
    {
        if (!_suitAvailable) return;
        _suitAvailable = false;
        player.EquipSuit(this);
    }

    public void OnInteractCancel(PlayerController player) { }

    public void ReturnSuit() => _suitAvailable = true;
}
