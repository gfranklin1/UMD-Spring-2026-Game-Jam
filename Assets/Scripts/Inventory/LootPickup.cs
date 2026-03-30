using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class LootPickup : NetworkBehaviour
{
    [SerializeField] private ItemData itemData;
    [SerializeField] private string   itemId;    // must match the ItemData SO asset name in LootRegistry

    public ItemData Item   => itemData;
    public string   ItemId => itemId;

    /// <summary>
    /// Hide until network-spawned so late-joining clients don't see
    /// loot that was already picked up (despawned server-side).
    /// </summary>
    public override void OnNetworkSpawn()
    {
        SetVisible(true);
    }

    public override void OnNetworkDespawn()
    {
        SetVisible(false);
    }

    private void SetVisible(bool vis)
    {
        foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = vis;
        foreach (var c in GetComponentsInChildren<Collider>())  c.enabled = vis;
    }

    public string GetPromptText(PlayerController viewer)
    {
        if (itemData == null) return "[E] Pick up";
        string gold = itemData.goldValue > 0 ? $"  ({itemData.goldValue}g)" : "";
        return $"[E] Pick up {itemData.itemName}{gold}";
    }
}
