using UnityEngine;

public class Cable : Upgrade
{
    [SerializeField]
    int cost = 100;
    [SerializeField]
    DiveCableSystem cable;

    void Start()
    {
        if (cable == null)
        {
            cable = FindAnyObjectByType<DiveCableSystem>();
        }
    }

    public override void ApplyUpgrade()
    {
        cable.Upgrade();
    }

    public override bool CanBuy()
    {
        return true;
    }

    public override int Cost()
    {
        return cost;
    }
}
