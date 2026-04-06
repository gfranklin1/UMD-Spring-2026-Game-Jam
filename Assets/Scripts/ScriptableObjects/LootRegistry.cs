using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "LootRegistry", menuName = "Game/Loot Registry")]
public class LootRegistry : ScriptableObject
{
    public ItemData[] items;

    private Dictionary<LootRarity, ItemData[]> _byRarity;

    /// <summary>Find an ItemData by its ScriptableObject asset name (e.g. "Shell", "Coin").</summary>
    public ItemData Find(string id)
    {
        foreach (var item in items)
            if (item != null && item.name == id) return item;
        return null;
    }

    /// <summary>Returns all items matching the given rarity. Caches on first call.</summary>
    public ItemData[] GetByRarity(LootRarity rarity)
    {
        if (_byRarity == null)
        {
            _byRarity = new Dictionary<LootRarity, ItemData[]>();
            foreach (LootRarity r in System.Enum.GetValues(typeof(LootRarity)))
                _byRarity[r] = System.Array.FindAll(items, i => i != null && i.rarity == r);
        }
        return _byRarity.TryGetValue(rarity, out var arr) ? arr : System.Array.Empty<ItemData>();
    }

    public static LootRarity ZoneToRarity(DepthZone zone) => zone switch
    {
        DepthZone.Shallow => LootRarity.Common,
        DepthZone.Mid     => LootRarity.Uncommon,
        DepthZone.Deep    => LootRarity.Rare,
        _                 => LootRarity.Common
    };
}
