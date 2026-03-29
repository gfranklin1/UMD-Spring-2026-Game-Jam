using UnityEngine;

[RequireComponent(typeof(Collider))]
public class InteractableStation : MonoBehaviour, IInteractable
{
    [SerializeField] string stationName = "Station";
    private PlayerController _occupant;

    public string GetPromptText() => _occupant == null ? $"[E] Use {stationName}" : "In use";
    public float  HoldDuration   => 0f;

    public void OnInteractStart(PlayerController player)
    {
        if (_occupant != null) return;
        _occupant = player;
        player.LockToStation(this);
    }

    public void OnInteractHold(PlayerController player) { }

    public void OnInteractCancel(PlayerController player) { }

    public void Release(PlayerController player)
    {
        if (_occupant == player) _occupant = null;
    }
}
