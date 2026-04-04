using GLTFast.Schema;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Merchant ship manager. Summons and Unsummons ship.
/// </summary>
public class MerchantManager : NetworkBehaviour
{
    [SerializeField]
    Animator animator;
    public void Start()
    {
        QuotaManager.Instance.OnCycleChanged += Summon;
        animator = GetComponent<Animator>();
    }

    // ── Events ────────
    public event System.Action OnMerchantShipLeave;

    // ── Summoning/Releasing Merchant Ship ────────
    public void Summon()
    {
        animator.ResetTrigger("SendOff");
        animator.SetTrigger("Summon");
    }

    public void SendOff()
    {
        animator.ResetTrigger("Summon");
        animator.SetTrigger("SendOff");
    }

    public void SignalMerchantLeave()
    {
        OnMerchantShipLeave.Invoke();
    }
}
