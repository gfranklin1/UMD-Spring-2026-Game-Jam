using UnityEngine;

public class PlayerInventory : MonoBehaviour
{
    [SerializeField] private int maxSlots = 5;

    private ItemData[] _slots;
    private int        _selectedIndex;

    public int        MaxSlots      => maxSlots;
    public int        SelectedIndex => _selectedIndex;
    public ItemData[] Slots         => _slots;

    public bool IsFull
    {
        get
        {
            for (int i = 0; i < _slots.Length; i++)
                if (_slots[i] == null) return false;
            return true;
        }
    }

    public event System.Action OnInventoryChanged;

    private void Awake()
    {
        _slots = new ItemData[maxSlots];
    }

    public bool TryAddItem(ItemData item)
    {
        for (int i = 0; i < _slots.Length; i++)
        {
            if (_slots[i] == null)
            {
                _slots[i] = item;
                OnInventoryChanged?.Invoke();
                return true;
            }
        }
        return false;
    }

    public ItemData RemoveSelected()
    {
        var item = _slots[_selectedIndex];
        if (item == null) return null;
        _slots[_selectedIndex] = null;
        OnInventoryChanged?.Invoke();
        return item;
    }

    public void SelectSlot(int index)
    {
        if (index < 0 || index >= maxSlots) return;
        _selectedIndex = index;
        OnInventoryChanged?.Invoke();
    }

    public void SelectNext()
    {
        _selectedIndex = (_selectedIndex + 1) % maxSlots;
        OnInventoryChanged?.Invoke();
    }

    public void SelectPrevious()
    {
        _selectedIndex = (_selectedIndex - 1 + maxSlots) % maxSlots;
        OnInventoryChanged?.Invoke();
    }

    public void RemoveAt(int index)
    {
        if (index < 0 || index >= _slots.Length) return;
        _slots[index] = null;
        OnInventoryChanged?.Invoke();
    }

    public int TotalGoldValue()
    {
        int total = 0;
        foreach (var s in _slots)
            if (s != null) total += s.goldValue;
        return total;
    }
}
