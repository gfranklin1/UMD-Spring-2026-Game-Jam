using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Ship's wheel interaction station. Locks the player in and captures A/D
/// for steering without releasing on movement input (unlike generic stations).
/// Wind drives forward speed automatically while the helm is occupied.
/// </summary>
[RequireComponent(typeof(Collider))]
public class HelmStation : MonoBehaviour, IInteractable
{
    [SerializeField] private string stationName = "Ship's Wheel";
    [SerializeField] private ShipMovement shipMovement;

    private PlayerController _operator;
    private float _lastSentSteering;

    public string GetPromptText(PlayerController viewer)
    {
        return _operator == null ? $"[E] Use {stationName}" : "In use";
    }

    public float HoldDurationFor(PlayerController viewer) => 0f;

    public void OnInteractStart(PlayerController player)
    {
        if (_operator != null) return;
        _operator = player;
        player.LockToStation(this);

        if (shipMovement != null)
        {
            bool networked = Unity.Netcode.NetworkManager.Singleton != null
                          && Unity.Netcode.NetworkManager.Singleton.IsListening;
            if (networked) shipMovement.SetHelmOccupiedServerRpc(true);
            else           shipMovement.SetHelmOccupiedLocal(true);
        }
    }

    public void OnInteractHold(PlayerController player) { }
    public void OnInteractCancel(PlayerController player) { }

    public void Release(PlayerController player)
    {
        if (_operator != player) return;
        _operator = null;
        _lastSentSteering = 0f;

        if (shipMovement != null)
        {
            bool networked = Unity.Netcode.NetworkManager.Singleton != null
                          && Unity.Netcode.NetworkManager.Singleton.IsListening;
            if (networked)
            {
                shipMovement.SetSteeringInputServerRpc(0f);
                shipMovement.SetHelmOccupiedServerRpc(false);
            }
            else
            {
                shipMovement.SetSteeringInputLocal(0f);
                shipMovement.SetHelmOccupiedLocal(false);
            }
        }
    }

    /// <summary>
    /// Called by PlayerController.HandleAtStationState() each frame while
    /// the player is locked to this station. Reads A/D for steering.
    /// </summary>
    public void HandleInput(InputAction moveAction)
    {
        if (shipMovement == null) return;

        var move = moveAction?.ReadValue<Vector2>() ?? Vector2.zero;
        float steering = move.x; // A = -1, D = +1

        // Only send when input changes meaningfully
        if (Mathf.Abs(steering - _lastSentSteering) > 0.05f)
        {
            _lastSentSteering = steering;
            bool networked = Unity.Netcode.NetworkManager.Singleton != null
                          && Unity.Netcode.NetworkManager.Singleton.IsListening;
            if (networked) shipMovement.SetSteeringInputServerRpc(steering);
            else           shipMovement.SetSteeringInputLocal(steering);
        }
    }
}
