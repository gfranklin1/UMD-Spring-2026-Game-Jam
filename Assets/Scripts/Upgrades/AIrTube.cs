using UnityEngine;

public class AirTube : Upgrade
{
    [SerializeField]
    int cost = 100;
    [SerializeField]
    AirPumpStation pump;
    public override void ApplyUpgrade()
    {
        pump.Upgrade();
    }

    public override bool CanBuy()
    {
        return !pump.upgraded;
    }

    public override int Cost()
    {
        return cost;
    }

}
