using UnityEngine;

public class AirTube : Upgrade
{
    [SerializeField] int cost = 100;
    [SerializeField] AirPumpStation pump;

    void Start()
    {
        if (pump == null)
            pump = FindAnyObjectByType<AirPumpStation>();
    }

    public override void ApplyUpgrade()
    {
        bool net = Unity.Netcode.NetworkManager.Singleton != null
                && Unity.Netcode.NetworkManager.Singleton.IsListening;
        if (net)
        {
            // ServerRpc broadcasts the effect to all clients
            var pc = FindFirstObjectByType<PlayerController>();
            pc?.UpgradePumpServerRpc();
        }
        else
        {
            pump.Upgrade();
        }
    }

    public override bool CanBuy() => !pump.upgraded;

    public override int Cost() => cost;
}
