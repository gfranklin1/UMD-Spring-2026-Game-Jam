using System.Collections;
using Unity.Netcode;
using UnityEngine;

public class DivingSuitRack : NetworkBehaviour, IInteractable
{
    [SerializeField] private float equipHoldTime   = 2f;
    [SerializeField] private float unequipHoldTime = 1f;

    [Tooltip("The inner suit model that hides when the suit is taken and reappears when returned.")]
    [SerializeField] private GameObject _suitModel;

    // ─── Networked state (server-authoritative) ───────────────────────────────
    private NetworkVariable<bool> _networkSuitAvailable = new(
        true,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private NetworkVariable<ulong> _networkSuitWearerObjectId = new(
        0UL,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private NetworkVariable<bool> _networkSuitHasBoots = new(
        true,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    // ─── Offline-only mirrors (used when !IsNetworked) ───────────────────────
    private bool             _localSuitAvailable = true;
    private bool             _localSuitHasBoots  = true;
    private PlayerController _localSuitWearer;

    private Coroutine _holdRoutine;

    private bool IsNetworked => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            // Start each match from a known default state.
            _networkSuitAvailable.Value      = true;
            _networkSuitWearerObjectId.Value = 0UL;
            _networkSuitHasBoots.Value       = true;
        }

        _networkSuitAvailable.OnValueChanged += OnSuitAvailableChanged;
        UpdateSuitModel(_networkSuitAvailable.Value);
    }

    private void OnSuitAvailableChanged(bool prev, bool available)
    {
        UpdateSuitModel(available);
    }

    private void UpdateSuitModel(bool available)
    {
        if (_suitModel != null) _suitModel.SetActive(available);
    }

    // ─── Public read properties (used by PlayerController ServerRpcs) ─────────
    public bool  NetworkSuitAvailable   => IsNetworked ? _networkSuitAvailable.Value      : _localSuitAvailable;
    public ulong NetworkSuitWearerObjId => IsNetworked ? _networkSuitWearerObjectId.Value  : 0UL;
    public bool  NetworkSuitHasBoots    => IsNetworked ? _networkSuitHasBoots.Value        : _localSuitHasBoots;

    public float HoldDurationFor(PlayerController viewer)
    {
        if (NetworkSuitAvailable) return equipHoldTime;
        bool viewerIsWearer = IsNetworked
            ? (viewer != null && viewer.NetworkObjectId == _networkSuitWearerObjectId.Value)
            : (viewer != null && viewer == _localSuitWearer);
        return viewerIsWearer ? unequipHoldTime : 0f; // non-wearers can't interact — no ring
    }
    public string GetPromptText(PlayerController viewer)
    {
        bool available = IsNetworked ? _networkSuitAvailable.Value : _localSuitAvailable;
        if (available) return $"[Hold E {equipHoldTime:F0}s] Equip Suit";

        // Suit is taken — only the wearer sees "Remove Suit"; everyone else sees "In Use"
        bool viewerIsWearer = IsNetworked
            ? (viewer != null && viewer.NetworkObjectId == _networkSuitWearerObjectId.Value)
            : (viewer != null && viewer == _localSuitWearer);

        return viewerIsWearer
            ? $"[Hold E {unequipHoldTime:F0}s] Remove Suit"
            : "Suit in use";
    }

    public void OnInteractStart(PlayerController player)
    {
        if (IsNetworked)
        {
            bool available = _networkSuitAvailable.Value;
            bool isWearer  = _networkSuitWearerObjectId.Value != 0UL
                             && player.NetworkObjectId == _networkSuitWearerObjectId.Value;

            if (available && _holdRoutine == null)
            {
                _holdRoutine = StartCoroutine(HoldRoutine(equipHoldTime, () =>
                    player.RequestEquipSuitServerRpc(NetworkObjectId)));
            }
            else if (!available && isWearer && _holdRoutine == null)
            {
                _holdRoutine = StartCoroutine(HoldRoutine(unequipHoldTime, () =>
                    player.RequestUnequipSuitServerRpc(NetworkObjectId)));
            }
        }
        else
        {
            // Offline path — original local logic
            if (_localSuitAvailable && _holdRoutine == null)
            {
                _holdRoutine = StartCoroutine(HoldRoutine(equipHoldTime, () =>
                {
                    _localSuitAvailable = false;
                    _localSuitWearer    = player;
                    UpdateSuitModel(false);
                    player.EquipSuit(this, _localSuitHasBoots);
                }));
            }
            else if (!_localSuitAvailable && player == _localSuitWearer && _holdRoutine == null)
            {
                _holdRoutine = StartCoroutine(HoldRoutine(unequipHoldTime, () =>
                    player.UnequipSuit()));
            }
        }
    }

    public void OnInteractHold(PlayerController player)   { }  // handled by coroutine
    public void OnInteractCancel(PlayerController player) { CancelHold(); }
    public void Release(PlayerController player)          { CancelHold(); }

    // ─── Server-side suit state mutations ────────────────────────────────────

    /// <summary>Called by server inside PlayerController.RequestEquipSuitServerRpc.</summary>
    public void ServerTakeSuit(ulong wearerNetworkObjectId)
    {
        _networkSuitAvailable.Value      = false;
        _networkSuitWearerObjectId.Value = wearerNetworkObjectId;
    }

    /// <summary>Called by server inside PlayerController.RequestUnequipSuitServerRpc.</summary>
    public void ServerReturnSuit(bool hadBoots)
    {
        _networkSuitAvailable.Value      = true;
        _networkSuitHasBoots.Value       = hadBoots;
        _networkSuitWearerObjectId.Value = 0UL;
    }

    /// <summary>Called by PlayerController.UnequipSuit on the offline path only.</summary>
    public void ReturnSuit(bool hadBoots)
    {
        if (IsNetworked) return;   // networked path uses ServerReturnSuit via RPC
        _localSuitAvailable = true;
        _localSuitHasBoots  = hadBoots;
        _localSuitWearer    = null;
        UpdateSuitModel(true);
    }

    /// <summary>Server-only: restore boots to the suit without returning it (upgrade effect).</summary>
    public void ServerRestoreBoots()
    {
        if (!IsServer) return;
        _networkSuitHasBoots.Value = true;
    }

    // ─── Game Reset ───────────────────────────────────────────────────────────

    /// <summary>
    /// Called by QuotaManager.ResetGame on the server. Returns the suit to the rack
    /// and force-unequips whoever is wearing it.
    /// </summary>
    public void ServerForceReset()
    {
        CancelHold();

        bool networked = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        if (networked)
        {
            if (!IsServer) return;

            ulong wearerId = _networkSuitWearerObjectId.Value;

            _networkSuitAvailable.Value      = true;
            _networkSuitWearerObjectId.Value = 0UL;
            _networkSuitHasBoots.Value       = true;

            if (wearerId != 0UL &&
                NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(wearerId, out var obj))
            {
                obj.GetComponent<PlayerController>()?.ForceUnequipSuitClientRpc();
            }
        }
        else
        {
            var wearer = _localSuitWearer;
            _localSuitAvailable = true;
            _localSuitHasBoots  = true;
            _localSuitWearer    = null;
            UpdateSuitModel(true);
            wearer?.ForceUnequipSuitLocal();
        }
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private void CancelHold()
    {
        if (_holdRoutine != null)
        {
            StopCoroutine(_holdRoutine);
            _holdRoutine = null;
        }
    }

    private IEnumerator HoldRoutine(float duration, System.Action onComplete)
    {
        yield return new WaitForSeconds(duration);
        _holdRoutine = null;
        onComplete?.Invoke();
    }
}
