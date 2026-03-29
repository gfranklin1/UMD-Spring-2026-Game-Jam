using UnityEngine;

[CreateAssetMenu(fileName = "NewItem", menuName = "Game/Item Data")]
public class ItemData : ScriptableObject
{
    public string     itemName;
    public int        goldValue;
    public Sprite     icon;          // for HUD display (can be null initially)
    public GameObject worldPrefab;   // prefab to spawn when dropped
}
