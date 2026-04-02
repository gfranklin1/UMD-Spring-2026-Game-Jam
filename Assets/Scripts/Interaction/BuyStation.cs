using UnityEngine;

public class BuyStation : MonoBehaviour, IInteractable
{
    // ── IInteractable ─────────────────────────────────────────────────────────
    public string GetPromptText(PlayerController viewer)
    {
        return "[E] Buy Items";
    }

    public float HoldDurationFor(PlayerController viewer)
    {
        throw new System.NotImplementedException();
    }

    public void OnInteractCancel(PlayerController player) { }

    public void OnInteractHold(PlayerController player) { }

    public void OnInteractStart(PlayerController player)
    {
        throw new System.NotImplementedException();
    }

    public void Release(PlayerController player)
    {
        player.LockToStation(this);
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
