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
    public void Buy()
    {
        if (!CanBuy() && GoldTracker.Instance.TotalGold < Cost())
        {
            cantBuyLabel.SetActive(true);
            return;
        }
        cantBuyLabel.SetActive(false);
        GoldTracker.Instance.AddGoldDirect(-Cost());
        ApplyUpgrade();
    }
    public abstract void ApplyUpgrade();
    public abstract int Cost();
    public abstract bool CanBuy();

    public void OnPointerEnter()
    {
        costLabel.text = $"{Cost()}g";
    }

}
