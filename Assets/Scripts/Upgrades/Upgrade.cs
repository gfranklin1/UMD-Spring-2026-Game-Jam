using UnityEngine;
using UnityEngine.UI;

public abstract class Upgrade : MonoBehaviour
{
    [SerializeField]
    public GameObject descriptionLabel;
    [SerializeField]
    public GameObject cantBuyLabel;
    [SerializeField]
    public Text costLabel;
    protected virtual void Awake()
    {
        if (costLabel != null) costLabel.text = $"{Cost()}g";
    }

    public void Buy()
    {
        if (!CanBuy() || GoldTracker.Instance.TotalGold < Cost())
        {
            cantBuyLabel.SetActive(true);
            return;
        }
        cantBuyLabel.SetActive(false);
        // Use RPC so the server-authoritative gold total is deducted correctly in multiplayer
        GoldTracker.Instance.AddGoldServerRpc(-Cost());
        ApplyUpgrade();
    }
    public abstract void ApplyUpgrade();
    public abstract int Cost();
    public abstract bool CanBuy();

    public void OnPointerEnter() { }

}
