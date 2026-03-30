using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Scene singleton that tracks the shared gold pool for all players.
/// Server-authoritative — clients read only.
/// </summary>
public class GoldTracker : NetworkBehaviour
{
    public static GoldTracker Instance { get; private set; }

    private NetworkVariable<int> _totalGold = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public int TotalGold => _totalGold.Value;

    public event System.Action OnGoldChanged;

    private void Awake()
    {
        if (Instance == null) Instance = this;
    }

    public override void OnNetworkSpawn()
    {
        Instance = this;
        _totalGold.OnValueChanged += (_, __) => OnGoldChanged?.Invoke();
        OnGoldChanged?.Invoke();  // fire once on spawn so HUDs initialize
    }

    public override void OnNetworkDespawn()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>Server-authoritative: any client can request adding gold.</summary>
    [ServerRpc(RequireOwnership = false)]
    public void AddGoldServerRpc(int amount)
    {
        _totalGold.Value += amount;
    }

    /// <summary>Called directly on server (e.g. from StorageChest.SellAllServerRpc).</summary>
    public void AddGoldDirect(int amount)
    {
        if (!IsServer) return;
        _totalGold.Value += amount;
    }

    /// <summary>Server-only: reset gold to zero at the start of a new quota cycle.</summary>
    public void ResetGold()
    {
        if (!IsServer) return;
        _totalGold.Value = 0;
    }
}
