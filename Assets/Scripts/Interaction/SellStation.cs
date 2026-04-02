using NUnit.Framework.Constraints;
using UnityEngine;

public class SellStation : MonoBehaviour, IInteractable
{



    // ── IInteractable ─────────────────────────────────────────────────────────
    public string GetPromptText(PlayerController viewer)
    {
        return "[E] Sell Treasure";
    }

    public float HoldDurationFor(PlayerController viewer)
    {
        return 0f;
    }

    public void OnInteractCancel(PlayerController player)  { }

    public void OnInteractHold(PlayerController player)  { }

    public void OnInteractStart(PlayerController player)
    {
        player.LockToStation(this);
    }

    public void Release(PlayerController player)
    {
        
    }
}
