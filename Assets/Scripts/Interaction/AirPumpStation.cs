using Unity.Netcode;
using UnityEngine;

/// <summary>
/// A topside station where a player operates an air pump for the diver.
/// Link this to the DivingSuitRack the diver uses — it pumps air to whoever
/// currently has that suit equipped.
///
/// Multiplayer: pumping is routed through ServerRpc → ClientRpc so the oxygen
/// update always lands on the diver's owning client.
/// Single-player: calls SetPumpActive / AddPumpedAir directly (no NetworkManager).
/// </summary>
[RequireComponent(typeof(Collider))]
public class AirPumpStation : MonoBehaviour, IInteractable
{
    [SerializeField] private string stationName = "Air Pump";
    [SerializeField] private DivingSuitRack linkedRack;
    [SerializeField] private float pumpRate = 2f;   // oxygen units/second delivered to diver

    private PlayerController _operator;
    private bool _wasPumping;

    // ── IInteractable ─────────────────────────────────────────────────────────

    public string GetPromptText() => _operator == null ? $"[E] Use {stationName}" : "In use";

    public void OnInteractStart(PlayerController player)
    {
        if (_operator != null) return;
        _operator = player;
        player.LockToStation(this);
    }

    public void OnInteractHold(PlayerController player) { }
    public void OnInteractCancel(PlayerController player) { }

    public void Release(PlayerController player)
    {
        if (_operator == player) _operator = null;
    }

    // ── Called by PlayerController ────────────────────────────────────────────

    /// <summary>Called each frame by the operator's PlayerController while they are at this station.</summary>
    public void PumpOperatorTick(PlayerController op, float dt)
    {
        var diver = linkedRack != null ? linkedRack.OccupyingPlayer : null;
        if (diver == null) return;

        bool networked = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        if (networked)
        {
            if (!_wasPumping) { diver.SetPumpActiveServerRpc(true); _wasPumping = true; }
            diver.AddPumpedAirServerRpc(pumpRate * dt);
        }
        else
        {
            if (!_wasPumping) { diver.SetPumpActive(true); _wasPumping = true; }
            diver.AddPumpedAir(pumpRate * dt);
        }
    }

    /// <summary>Called by PlayerController.ReleaseFromStation when the operator leaves.</summary>
    public void OnOperatorLeft(PlayerController op)
    {
        if (!_wasPumping) return;
        _wasPumping = false;

        var diver = linkedRack != null ? linkedRack.OccupyingPlayer : null;
        if (diver == null) return;

        bool networked = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        if (networked)
            diver.SetPumpActiveServerRpc(false);
        else
            diver.SetPumpActive(false);
    }
}
