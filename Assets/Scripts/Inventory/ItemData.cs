using UnityEngine;

public enum LootRarity { Common, Uncommon, Rare }

[CreateAssetMenu(fileName = "NewItem", menuName = "Game/Item Data")]
public class ItemData : ScriptableObject
{
    public string     itemName;
    public int        goldValue;
    public LootRarity rarity;
    public Sprite     icon;          // for HUD display (can be null initially)
    public GameObject worldPrefab;   // prefab to spawn when dropped
}
