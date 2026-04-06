using UnityEngine;

public abstract class Upgrade : MonoBehaviour
{
    public void Buy()
    {
        if (!CanBuy() && GoldTracker.Instance.TotalGold < Cost())
        {
            return;
        }

        GoldTracker.Instance.AddGoldDirect(-Cost());
        ApplyUpgrade();
    }
    public abstract void ApplyUpgrade();
    public abstract int Cost();
    public abstract bool CanBuy(); 
}
