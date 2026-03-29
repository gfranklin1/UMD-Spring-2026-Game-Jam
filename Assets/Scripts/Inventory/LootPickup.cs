using UnityEngine;

[RequireComponent(typeof(Collider))]
public class LootPickup : MonoBehaviour
{
    [SerializeField] private ItemData itemData;

    public ItemData Item => itemData;

    public string GetPromptText()
    {
        if (itemData == null) return "[E] Pick up";
        return $"[E] Pick up {itemData.itemName}";
    }
}
