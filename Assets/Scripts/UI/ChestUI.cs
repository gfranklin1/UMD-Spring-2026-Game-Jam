using Unity.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Minecraft-style chest overlay UI.
///
/// Layout:
///   Title       — "Storage Chest"
///   Top grid    — chest's 12 storage slots (read-only display)
///   Gold label  — total gold value of chest contents
///   Divider     — "Inventory"
///   Bottom row  — player's 5 hotbar slots; click one to deposit that item
///   Buttons     — "Deposit All" | "Sell All"
///
/// The normal hotbar HUD is hidden while this panel is open to avoid duplication.
/// Open/close driven by PlayerController.OnChestOpened / OnChestClosed.
/// E or Escape closes the chest (handled in PlayerController).
/// </summary>
public class ChestUI : MonoBehaviour
{
    [SerializeField] private PlayerController _player;
    [SerializeField] private PlayerInventory  _inventory;
    [SerializeField] private GameObject       _panel;

    [Header("Chest Slots (top grid)")]
    [SerializeField] private Image[] _chestSlotIcons;   // 12
    [SerializeField] private Text[]  _chestSlotLabels;  // 12
    [SerializeField] private Text    _totalGoldText;

    [Header("Player Inventory (bottom row) — click to deposit")]
    [SerializeField] private Image[]  _playerSlotIcons;   // 5
    [SerializeField] private Text[]   _playerSlotLabels;  // 5
    [SerializeField] private Button[] _playerSlotButtons; // 5

    [Header("Action Buttons")]
    [SerializeField] private Button _depositAllButton;
    [SerializeField] private Button _closeAndSellButton;

    [Header("Normal HUD to hide")]
    [SerializeField] private GameObject _hotbarPanel;       // Hotbar_Panel
    [SerializeField] private GameObject _itemNameLabel;     // ItemNameLabel
    [SerializeField] private GameObject _dropPromptLabel;   // DropPromptLabel
    [SerializeField] private GameObject _crosshair;         // Crosshair

    [SerializeField] private LootRegistry _registry;

    private StorageChest _chest;
    private int _pendingDepositSlot = -1;

    private void Start()
    {
        _panel.SetActive(false);

        // Only wire up on the local player's HUD — non-owner instances are disabled by PlayerHUD
        bool networked = Unity.Netcode.NetworkManager.Singleton != null
                      && Unity.Netcode.NetworkManager.Singleton.IsListening;
        if (networked && _player != null && !_player.IsOwner)
        {
            enabled = false;
            return;
        }

        _player.OnChestOpened += OnOpen;
        _player.OnChestClosed += OnClose;

        // Wire all buttons at runtime
        if (_playerSlotButtons != null)
        {
            for (int i = 0; i < _playerSlotButtons.Length; i++)
            {
                int idx = i;
                if (_playerSlotButtons[i] != null)
                    _playerSlotButtons[i].onClick.AddListener(() => OnPlayerSlotClicked(idx));
            }
        }

        if (_depositAllButton != null)
            _depositAllButton.onClick.AddListener(OnDepositAllClicked);
        else
            Debug.LogWarning("ChestUI: _depositAllButton is not assigned.");
        if (_closeAndSellButton != null)
            _closeAndSellButton.onClick.AddListener(OnCloseAndSellClicked);
        else
            Debug.LogWarning("ChestUI: _closeAndSellButton is not assigned.");
    }

    private void OnDestroy()
    {
        if (_player != null)
        {
            _player.OnChestOpened -= OnOpen;
            _player.OnChestClosed -= OnClose;
        }
        UnsubscribeChest();
        if (_inventory != null)
            _inventory.OnInventoryChanged -= RefreshPlayerSlots;
    }

    // ── Open / Close ──────────────────────────────────────────────────────────

    private void OnOpen(StorageChest chest)
    {
        _chest = chest;
        _chest.OnSlotsChanged    += RefreshChestSlots;
        _chest.OnDepositConfirmed += ClearInventorySlots;
        _inventory.OnInventoryChanged += RefreshPlayerSlots;

        // Hide normal HUD elements — chest UI shows its own inventory row
        SetHotbarVisible(false);

        _panel.SetActive(true);
        RefreshChestSlots();
        RefreshPlayerSlots();
    }

    private void OnClose()
    {
        _chest?.NotifyChestClosed();
        UnsubscribeChest();
        if (_inventory != null)
            _inventory.OnInventoryChanged -= RefreshPlayerSlots;
        _panel.SetActive(false);

        // Restore normal HUD
        SetHotbarVisible(true);
    }

    private void SetHotbarVisible(bool visible)
    {
        if (_hotbarPanel != null)     _hotbarPanel.SetActive(visible);
        if (_itemNameLabel != null)   _itemNameLabel.SetActive(visible);
        if (_dropPromptLabel != null) _dropPromptLabel.SetActive(visible);
        if (_crosshair != null)       _crosshair.SetActive(visible);
    }

    private void UnsubscribeChest()
    {
        if (_chest == null) return;
        _chest.OnSlotsChanged     -= RefreshChestSlots;
        _chest.OnDepositConfirmed -= ClearInventorySlots;
        _chest = null;
    }

    // ── Player slot buttons — click to deposit one item ───────────────────────

    public void OnPlayerSlotClicked(int slotIndex)
    {
        if (_chest == null || _inventory == null) return;
        if (slotIndex < 0 || slotIndex >= _inventory.MaxSlots) return;
        var item = _inventory.Slots[slotIndex];
        if (item == null) return;

        bool networked = Unity.Netcode.NetworkManager.Singleton != null
                      && Unity.Netcode.NetworkManager.Singleton.IsListening;
        if (networked)
        {
            // Disable immediately to prevent double-click before RPC roundtrip completes
            if (_playerSlotButtons != null && slotIndex < _playerSlotButtons.Length)
                _playerSlotButtons[slotIndex].interactable = false;

            _chest.DepositOneServerRpc(new FixedString64Bytes(item.name));
            _pendingDepositSlot = slotIndex;
        }
        else
        {
            int accepted = _chest.DepositLocal(new[] { item.name });
            if (accepted > 0) _inventory.RemoveAt(slotIndex);
        }
    }

    // ── Deposit All button ────────────────────────────────────────────────────

    public void OnDepositAllClicked()
    {
        if (_chest == null || _inventory == null) return;
        var ids = new System.Collections.Generic.List<string>();
        foreach (var item in _inventory.Slots)
            if (item != null) ids.Add(item.name);
        if (ids.Count == 0) return;

        bool networked = Unity.Netcode.NetworkManager.Singleton != null
                      && Unity.Netcode.NetworkManager.Singleton.IsListening;
        if (networked)
        {
            _chest.DepositItemsServerRpc(new FixedString512Bytes(string.Join(",", ids)));
            _pendingDepositSlot = -1;
        }
        else
        {
            int accepted = _chest.DepositLocal(ids.ToArray());
            ClearInventorySlots(accepted);
        }
    }

    // ── Sell All button ──────────────────────────────────────────────────────

    public void OnCloseAndSellClicked()
    {
        if (_chest == null) return;
        bool networked = Unity.Netcode.NetworkManager.Singleton != null
                      && Unity.Netcode.NetworkManager.Singleton.IsListening;
        if (networked)
            _chest.SellAllServerRpc();
        else
            _chest.SellAllLocal();
        _player.CloseChest();
    }

    // ── Inventory clear after deposit confirmed ───────────────────────────────

    private void ClearInventorySlots(int acceptedCount)
    {
        if (_inventory == null || acceptedCount <= 0) return;

        if (_pendingDepositSlot >= 0)
        {
            _inventory.RemoveAt(_pendingDepositSlot);
            _pendingDepositSlot = -1;
        }
        else
        {
            int cleared = 0;
            for (int i = 0; i < _inventory.MaxSlots && cleared < acceptedCount; i++)
            {
                if (_inventory.Slots[i] != null) { _inventory.RemoveAt(i); cleared++; }
            }
        }
    }

    // ── Refresh ───────────────────────────────────────────────────────────────

    private void RefreshChestSlots()
    {
        if (_chest == null) return;
        var items = _chest.GetItems();
        for (int i = 0; i < _chestSlotIcons.Length; i++)
        {
            var item = i < items.Length ? items[i] : null;
            _chestSlotIcons[i].enabled = item?.icon != null;
            if (item?.icon != null) _chestSlotIcons[i].sprite = item.icon;
            if (_chestSlotLabels != null && i < _chestSlotLabels.Length)
                _chestSlotLabels[i].text = item?.itemName ?? "";
        }
        if (_totalGoldText != null)
            _totalGoldText.text = $"Chest Value: {_chest.ChestGold}g";
    }

    private void RefreshPlayerSlots()
    {
        if (_inventory == null) return;
        for (int i = 0; i < _playerSlotIcons.Length; i++)
        {
            var item = i < _inventory.MaxSlots ? _inventory.Slots[i] : null;
            _playerSlotIcons[i].enabled = item?.icon != null;
            if (item?.icon != null) _playerSlotIcons[i].sprite = item.icon;
            if (_playerSlotLabels != null && i < _playerSlotLabels.Length)
                _playerSlotLabels[i].text = item?.itemName ?? "";
            // Highlight filled slots, dim empty ones
            if (_playerSlotButtons != null && i < _playerSlotButtons.Length)
            {
                var bg = _playerSlotButtons[i].GetComponent<Image>();
                if (bg != null) bg.color = item != null
                    ? new Color(0.4f, 0.4f, 0.35f, 0.7f)
                    : new Color(0.25f, 0.25f, 0.25f, 0.4f);
                _playerSlotButtons[i].interactable = item != null;
            }
        }
    }
}
