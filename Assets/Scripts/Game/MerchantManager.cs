using GLTFast.Schema;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Merchant ship manager. Summons and Unsummons ship.
/// </summary>
public class MerchantManager : NetworkBehaviour
{
    public void Start()
    {
        QuotaManager.Instance.OnCycleChanged += Summon;
    }

    // ── Events ────────
    public event System.Action OnMerchantShipLeave;

    // ── Summoning/Releasing Merchant Ship ────────
    public void Summon()
    {
        Animator animator = GetComponent<Animator>();
        animator.ResetTrigger("SendOff");
        animator.SetTrigger("Summon");
    }

    public void SendOff()
    {
        Animator animator = GetComponent<Animator>();
        animator.ResetTrigger("Summon");
        animator.SetTrigger("SendOff");
    }


}
