using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class LootPickup : NetworkBehaviour
{
    [SerializeField] private ItemData itemData;
    [SerializeField] private string   itemId;    // must match the ItemData SO asset name in LootRegistry

    public ItemData Item   => itemData;
    public string   ItemId => itemId;

    public string GetPromptText()
    {
        if (itemData == null) return "[E] Pick up";
        string gold = itemData.goldValue > 0 ? $"  ({itemData.goldValue}g)" : "";
        return $"[E] Pick up {itemData.itemName}{gold}";
    }
}
