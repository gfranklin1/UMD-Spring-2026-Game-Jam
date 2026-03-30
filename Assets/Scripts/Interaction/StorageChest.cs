using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// A permanent shared storage chest on the boat.
/// Any player can open it, deposit loot, and sell everything for shared gold.
/// Server-authoritative slot list synced to all clients via NetworkList.
/// </summary>
[RequireComponent(typeof(Collider))]
public class StorageChest : NetworkBehaviour, IInteractable
{
    [SerializeField] private int          maxSlots = 12;
    [SerializeField] private LootRegistry _registry;
    [SerializeField] private Animator     _animator;

    // Server-authoritative slot list (itemId per slot, empty string = empty)
    private NetworkList<FixedString64Bytes> _slots;

    // Cached gold total kept in sync for all clients (drives HUD without opening chest)
    private NetworkVariable<int> _chestGold = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    // Tracks how many players currently have the chest UI open (server-authoritative)
    private NetworkVariable<int> _openCount = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    // Offline fallback mirrors
    private string[] _localSlots;
    private int      _localOpenCount;

    public int  ChestGold => _chestGold.Value;
    public int  MaxSlots  => maxSlots;

    /// <summary>Fires on every client when slot contents or gold total changes.</summary>
    public event System.Action OnSlotsChanged;

    private bool IsNetworked => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;

    private void Awake()
    {
        _slots      = new NetworkList<FixedString64Bytes>();
        _localSlots = new string[maxSlots];
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            // Populate empty slots once
            for (int i = 0; i < maxSlots; i++)
                _slots.Add(new FixedString64Bytes(""));
        }

        _slots.OnListChanged      += _ => { RebuildGoldCache(); OnSlotsChanged?.Invoke(); };
        _chestGold.OnValueChanged += (_, __)   => OnSlotsChanged?.Invoke();
        _openCount.OnValueChanged += (prev, next) =>
        {
            if (next > 0 && prev == 0) SetAnimatorOpen(true);
            else if (next == 0 && prev > 0) SetAnimatorOpen(false);
        };
    }

    // ── IInteractable ─────────────────────────────────────────────────────────

    public float HoldDurationFor(PlayerController viewer) => 0f;

    public string GetPromptText(PlayerController viewer)
    {
        int count = ItemCount();
        return count > 0
            ? $"[E] Open Chest  ({ChestGold}g stored)"
            : "[E] Open Chest";
    }

    public void OnInteractStart(PlayerController player)
    {
        if (IsNetworked) ChangeOpenCountServerRpc(1);
        else { _localOpenCount++; SetAnimatorOpen(_localOpenCount > 0); }
        player.OpenChest(this);
    }

    public void OnInteractHold(PlayerController player)   { }
    public void OnInteractCancel(PlayerController player) { }
    public void Release(PlayerController player)          { }

    /// <summary>Called by ChestUI when the player closes the chest panel.</summary>
    public void NotifyChestClosed()
    {
        if (IsNetworked) ChangeOpenCountServerRpc(-1);
        else { _localOpenCount = Mathf.Max(0, _localOpenCount - 1); SetAnimatorOpen(_localOpenCount > 0); }
    }

    [ServerRpc(RequireOwnership = false)]
    private void ChangeOpenCountServerRpc(int delta)
    {
        _openCount.Value = Mathf.Max(0, _openCount.Value + delta);
    }

    private void SetAnimatorOpen(bool open)
    {
        if (_animator != null)
            _animator.SetTrigger(open ? "Open" : "Close");
    }

    // ── Deposit ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Called by ChestUI when the player clicks "Deposit Loot".
    /// itemIdsCsv is a comma-separated list of itemId strings (e.g. "Shell,Coin,Artifact").
    /// NGO can serialize FixedString512Bytes natively.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void DepositItemsServerRpc(FixedString512Bytes itemIdsCsv, ServerRpcParams rpc = default)
    {
        string[] itemIds = itemIdsCsv.ToString().Split(',');
        int deposited = 0;
        foreach (var id in itemIds)
        {
            if (string.IsNullOrEmpty(id)) continue;
            bool placed = false;
            for (int i = 0; i < _slots.Count; i++)
            {
                if (_slots[i].IsEmpty)
                {
                    _slots[i] = new FixedString64Bytes(id);
                    deposited++;
                    placed = true;
                    break;
                }
            }
            if (!placed) break; // chest full
        }
        RebuildGoldCache();

        // Tell depositing client how many items were accepted (so it can clear inventory)
        var cp = new ClientRpcParams
            { Send = new ClientRpcSendParams { TargetClientIds = new[] { rpc.Receive.SenderClientId } } };
        ConfirmDepositClientRpc(deposited, cp);
    }

    [ClientRpc]
    private void ConfirmDepositClientRpc(int depositedCount, ClientRpcParams cp = default)
    {
        // ChestUI subscribes to OnSlotsChanged and handles inventory clearing.
        // We fire the event here so ChestUI can clear the player's inventory slots.
        OnDepositConfirmed?.Invoke(depositedCount);
    }

    /// <summary>Fires on the depositing client with how many items were accepted.</summary>
    public event System.Action<int> OnDepositConfirmed;

    /// <summary>
    /// Deposit a single item by itemId. Simpler than the CSV bulk path.
    /// Returns true if there was room in the chest.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void DepositOneServerRpc(FixedString64Bytes itemId, ServerRpcParams rpc = default)
    {
        string id = itemId.ToString();
        if (string.IsNullOrEmpty(id)) return;
        bool placed = false;
        for (int i = 0; i < _slots.Count; i++)
        {
            if (_slots[i].IsEmpty)
            {
                _slots[i] = new FixedString64Bytes(id);
                placed = true;
                break;
            }
        }
        RebuildGoldCache();
        if (placed)
        {
            var cp = new ClientRpcParams
                { Send = new ClientRpcSendParams { TargetClientIds = new[] { rpc.Receive.SenderClientId } } };
            ConfirmDepositClientRpc(1, cp);
        }
    }

    // ── Sell All ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Called by ChestUI when the player clicks "Close & Sell".
    /// Clears all slots and adds gold to the global GoldTracker.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void SellAllServerRpc()
    {
        int total = 0;
        for (int i = 0; i < _slots.Count; i++)
        {
            string id = _slots[i].ToString();
            if (!string.IsNullOrEmpty(id))
            {
                var item = _registry?.Find(id);
                if (item != null) total += item.goldValue;
                _slots[i] = new FixedString64Bytes("");
            }
        }
        _chestGold.Value = 0;

        if (total > 0)
            GoldTracker.Instance?.AddGoldDirect(total);
    }

    // ── Offline Deposit / Sell (no network) ───────────────────────────────────

    /// <summary>Offline-only deposit path (called directly from ChestUI).</summary>
    public int DepositLocal(string[] itemIds)
    {
        int deposited = 0;
        foreach (var id in itemIds)
        {
            if (string.IsNullOrEmpty(id)) continue;
            for (int i = 0; i < maxSlots; i++)
            {
                if (string.IsNullOrEmpty(_localSlots[i]))
                {
                    _localSlots[i] = id;
                    deposited++;
                    break;
                }
            }
        }
        RebuildLocalGold();
        OnSlotsChanged?.Invoke();
        return deposited;
    }

    /// <summary>Offline-only sell path.</summary>
    public void SellAllLocal()
    {
        int total = 0;
        for (int i = 0; i < maxSlots; i++)
        {
            if (!string.IsNullOrEmpty(_localSlots[i]))
            {
                var item = _registry?.Find(_localSlots[i]);
                if (item != null) total += item.goldValue;
                _localSlots[i] = null;
            }
        }
        OnSlotsChanged?.Invoke();
        // Gold tracker offline: just call AddGoldDirect if it exists
        GoldTracker.Instance?.AddGoldDirect(total);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Returns the ItemData for each slot (null = empty). Safe to call on any client.</summary>
    public ItemData[] GetItems()
    {
        var result = new ItemData[maxSlots];
        if (IsNetworked)
        {
            for (int i = 0; i < _slots.Count && i < maxSlots; i++)
            {
                string id = _slots[i].ToString();
                if (!string.IsNullOrEmpty(id))
                    result[i] = _registry?.Find(id);
            }
        }
        else
        {
            for (int i = 0; i < maxSlots; i++)
                if (!string.IsNullOrEmpty(_localSlots[i]))
                    result[i] = _registry?.Find(_localSlots[i]);
        }
        return result;
    }

    public int ItemCount()
    {
        if (IsNetworked)
        {
            int n = 0;
            foreach (var s in _slots) if (!s.IsEmpty) n++;
            return n;
        }
        else
        {
            int n = 0;
            foreach (var s in _localSlots) if (!string.IsNullOrEmpty(s)) n++;
            return n;
        }
    }

    private void RebuildGoldCache()
    {
        if (!IsServer) return;
        int total = 0;
        foreach (var s in _slots)
        {
            string id = s.ToString();
            if (!string.IsNullOrEmpty(id))
            {
                var item = _registry?.Find(id);
                if (item != null) total += item.goldValue;
            }
        }
        _chestGold.Value = total;
    }

    private void RebuildLocalGold()
    {
        // Offline only — no NetworkVariable involved
    }
}
