using UnityEngine;

public class Suit : Upgrade
{
    [SerializeField]
    int cost = 100;
    [SerializeField]
    DivingSuitRack suitRack;

    void Start()
    {
        if (suitRack == null)
        {
            suitRack = FindAnyObjectByType<DivingSuitRack>();
        }   
    }

    public override void ApplyUpgrade()
    {
        suitRack.ServerReturnSuit(true);
    }

    public override bool CanBuy()
    {
        return !suitRack.NetworkSuitAvailable;
    }

    public override int Cost()
    {
        return cost;
    }
}
