using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Merchant ship manager. Summons and dismisses the merchant ship.
/// Summon is triggered by QuotaManager.OnCycleChanged (fires on all clients via NetworkVariable).
/// SendOff goes through a ServerRpc so all clients animate together.
/// </summary>
public class MerchantManager : NetworkBehaviour
{
    [SerializeField] Animator animator;
    [SerializeField] float stayDuration;

    static MerchantManager instance;

    public static MerchantManager Instance()
    {
        if (instance == null)
            instance = FindAnyObjectByType<MerchantManager>();
        return instance;
    }

    // ── Events ────────────────────────────────────────────────────────────────
    public event System.Action OnMerchantShipLeave;

    // ── Lifecycle ─────────────────────────────────────────────────────────────
    public void Start()
    {
        if (animator == null) animator = GetComponent<Animator>();
        // OnMerchantSummon fires on all clients when the server increments the summon counter,
        // so all clients animate the merchant arriving at the same time.
        QuotaManager.Instance.OnMerchantSummon += Summon;

        // Always start hidden regardless of saved scene state.
        // MerchantReset sets these same values; we mirror them here so the boat
        // is never visible at scene load even if the scene was saved mid-arrival.
        var buoyancy = GetComponentInChildren<ShipBuoyancy>();
        if (buoyancy != null) buoyancy.enabled = false;
        transform.localPosition = new Vector3(-10f, -1000f, 3.04507f);
        animator.Play("NotHere", 0, 0f);
    }

    // ── Local animation helpers ───────────────────────────────────────────────
    public void Summon()
    {
        if (animator == null) return;
        animator.ResetTrigger("SendOff");
        animator.SetTrigger("Summon");
    }

    public void SendOff()
    {
        if (animator == null) return;
        animator.ResetTrigger("Summon");
        animator.SetTrigger("SendOff");
    }

    // ── Network: send-off must broadcast so all clients animate ───────────────
    [ServerRpc(RequireOwnership = false)]
    public void SendOffServerRpc()
    {
        SendOffClientRpc();
    }

    [ClientRpc]
    private void SendOffClientRpc()
    {
        SendOff();
    }

    // ── Called by animation event when merchant ship has fully left ───────────
    public void SignalMerchantLeave()
    {
        OnMerchantShipLeave?.Invoke();
    }
}
