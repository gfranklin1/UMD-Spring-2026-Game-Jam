using UnityEngine;

public class Cable : Upgrade
{
    [SerializeField] int cost = 100;
    [SerializeField] DiveCableSystem cable;

    void Start()
    {
        if (cable == null)
            cable = FindAnyObjectByType<DiveCableSystem>();
    }

    public override void ApplyUpgrade()
    {
        bool net = Unity.Netcode.NetworkManager.Singleton != null
                && Unity.Netcode.NetworkManager.Singleton.IsListening;
        if (net)
        {
            var pc = FindFirstObjectByType<PlayerController>();
            pc?.UpgradeCableServerRpc();
        }
        else
        {
            cable.Upgrade();
        }
    }

    public override bool CanBuy() => true;

    public override int Cost() => cost;
}
