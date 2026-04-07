using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Merchant ship manager. Summons and Unsummons ship.
/// </summary>
public class MerchantManager : NetworkBehaviour
{
    [SerializeField]
    Animator animator;
    [SerializeField]
    float stayDuration;

    static MerchantManager instance;

    public static MerchantManager Instance()
    {
        if(instance == null)
        {
            instance = FindAnyObjectByType<MerchantManager>();
        }
        return instance;
    }

    float timer;
    public void Start()
    {
        QuotaManager.Instance.OnCycleChanged += Summon;
        animator = GetComponent<Animator>();
    }

    //private void Update()
    //{
    //    if (animator.GetCurrentAnimatorStateInfo(0).IsName("Here") &&  stayDuration > 0)
    //    {
    //        stayDuration -= Time.deltaTime;
    //        if (stayDuration <= 0)
    //        {
    //            SendOff();
    //        }
    //    }
    //}

    // ── Events ────────
    public event System.Action OnMerchantShipLeave;

    // ── Summoning/Releasing Merchant Ship ────────
    public void Summon()
    {
        animator.ResetTrigger("SendOff");
        animator.SetTrigger("Summon");
        //timer = stayDuration;
    }

    public void SendOff()
    {
        animator.ResetTrigger("Summon");
        animator.SetTrigger("SendOff");
    }

    public void SignalMerchantLeave()
    {
        OnMerchantShipLeave?.Invoke();
    }
}
