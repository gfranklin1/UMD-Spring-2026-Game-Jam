using UnityEngine;

[CreateAssetMenu(fileName = "LootRegistry", menuName = "Game/Loot Registry")]
public class LootRegistry : ScriptableObject
{
    public ItemData[] items;

    /// <summary>Find an ItemData by its ScriptableObject asset name (e.g. "Shell", "Coin").</summary>
    public ItemData Find(string id)
    {
        foreach (var item in items)
            if (item != null && item.name == id) return item;
        return null;
    }
}
