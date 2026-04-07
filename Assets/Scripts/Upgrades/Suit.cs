using UnityEngine;

public class Suit : Upgrade
{
    [SerializeField] int cost = 100;
    [SerializeField] DivingSuitRack suitRack;

    void Start()
    {
        if (suitRack == null)
            suitRack = FindAnyObjectByType<DivingSuitRack>();
    }

    public override void ApplyUpgrade()
    {
        bool net = Unity.Netcode.NetworkManager.Singleton != null
                && Unity.Netcode.NetworkManager.Singleton.IsListening;
        if (net)
        {
            // UpgradeSuitServerRpc calls ServerRestoreBoots on the server,
            // which sets the NetworkVariable — no ClientRpc needed
            var pc = FindFirstObjectByType<PlayerController>();
            pc?.UpgradeSuitServerRpc();
        }
        else
        {
            suitRack.ReturnSuit(true);
        }
    }

    public override bool CanBuy() => !suitRack.NetworkSuitHasBoots;

    public override int Cost() => cost;
}
