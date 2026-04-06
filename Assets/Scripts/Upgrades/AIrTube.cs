using Unity.VisualScripting;
using UnityEngine;

public class AirTube : Upgrade
{
    [SerializeField]
    int cost = 100;
    [SerializeField]
    AirPumpStation pump;

    void Start()
    {
        if (pump == null)
        {
            pump = FindAnyObjectByType<AirPumpStation>();
        }
    }
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
