using UnityEngine;
using UnityEngine.UI;

public class InventoryHUD : MonoBehaviour
{
    [SerializeField] private PlayerInventory inventory;
    [SerializeField] private RectTransform[] slotFrames;        // 5 slot background rects
    [SerializeField] private Image[]         slotIcons;         // item icon images per slot
    [SerializeField] private Image           selectionHighlight;
    [SerializeField] private Text            lootPromptText;    // "[E] Pick up Shell"
    [SerializeField] private Color           emptySlotColor  = new Color(1f, 1f, 1f, 0.15f);
    [SerializeField] private Color           filledSlotColor = new Color(1f, 1f, 1f, 0.8f);

    [Header("Labels")]
    [SerializeField] private Text  itemNameLabel;
    [SerializeField] private Text  goldTotalLabel;
    [SerializeField] private Text  dropPromptLabel;

    [Header("Animation")]
    [SerializeField] private float highlightLerpSpeed = 12f;
    [SerializeField] private float flashDuration      = 0.35f;
    [SerializeField] private Color flashColor         = new Color(1f, 1f, 0.3f, 0.9f);

    private PlayerController _player;
    private Vector3          _highlightTargetPos;
    private float[]          _slotFlashTimers;
    private ItemData[]       _prevSlots;
    private bool             _started;

    private void Start()
    {
        _player = inventory != null ? inventory.GetComponent<PlayerController>() : null;

        _slotFlashTimers = new float[slotFrames.Length];
        _prevSlots       = new ItemData[inventory != null ? inventory.MaxSlots : slotFrames.Length];

        SnapHighlight();

        if (inventory != null)
            inventory.OnInventoryChanged += Refresh;
        Refresh();
        _started = true;
    }

    // Called by PlayerHUD.ShowForRespawn() — waits a frame so Canvas layout is computed.
    public void SnapHighlightNextFrame()
    {
        if (_started) StartCoroutine(SnapNextFrame());
    }

    private System.Collections.IEnumerator SnapNextFrame()
    {
        yield return null;
        SnapHighlight();
    }

    private void SnapHighlight()
    {
        Canvas.ForceUpdateCanvases();
        if (selectionHighlight != null && slotFrames != null && slotFrames.Length > 0 && inventory != null)
        {
            _highlightTargetPos = slotFrames[inventory.SelectedIndex].position;
            selectionHighlight.rectTransform.position = _highlightTargetPos;
        }
    }

    private void OnDestroy()
    {
        if (inventory != null)
            inventory.OnInventoryChanged -= Refresh;
    }

    private void Update()
    {
        // Loot pickup prompt
        if (lootPromptText != null)
        {
            var loot = _player != null ? _player.NearestLoot : null;
            if (loot != null && !inventory.IsFull)
                lootPromptText.text = loot.GetPromptText(_player);
            else if (loot != null && inventory.IsFull)
                lootPromptText.text = "Inventory full";
            else
                lootPromptText.text = "";
        }

        // Smooth highlight slide
        if (selectionHighlight != null)
        {
            selectionHighlight.rectTransform.position = Vector3.Lerp(
                selectionHighlight.rectTransform.position,
                _highlightTargetPos,
                highlightLerpSpeed * Time.deltaTime);
        }

        // Per-slot flash decay
        for (int i = 0; i < _slotFlashTimers.Length; i++)
        {
            if (_slotFlashTimers[i] <= 0f) continue;
            _slotFlashTimers[i] -= Time.deltaTime;

            var bg = slotFrames[i].GetComponent<Image>();
            if (bg != null)
            {
                float t = _slotFlashTimers[i] / flashDuration;
                bg.color = Color.Lerp(filledSlotColor, flashColor, t);
            }
        }
    }

    private void Refresh()
    {
        if (inventory == null || inventory.Slots == null) return;

        for (int i = 0; i < slotFrames.Length && i < inventory.MaxSlots; i++)
        {
            var item   = inventory.Slots[i];
            bool filled = item != null;

            // Flash newly filled slots
            if (_prevSlots != null && i < _prevSlots.Length && _prevSlots[i] == null && filled)
                _slotFlashTimers[i] = flashDuration;

            if (_prevSlots != null && i < _prevSlots.Length)
                _prevSlots[i] = item;

            if (slotIcons != null && i < slotIcons.Length)
            {
                slotIcons[i].enabled = filled && item.icon != null;
                if (filled && item.icon != null)
                    slotIcons[i].sprite = item.icon;
            }

            // Tint slot background (only if not currently flashing)
            if (_slotFlashTimers != null && i < _slotFlashTimers.Length && _slotFlashTimers[i] <= 0f)
            {
                var bg = slotFrames[i].GetComponent<Image>();
                if (bg != null)
                    bg.color = filled ? filledSlotColor : emptySlotColor;
            }
        }

        // Update selection highlight target
        if (selectionHighlight != null && inventory.SelectedIndex < slotFrames.Length)
            _highlightTargetPos = slotFrames[inventory.SelectedIndex].position;

        // Item name label
        var selected = inventory.SelectedIndex < inventory.Slots.Length
            ? inventory.Slots[inventory.SelectedIndex] : null;

        if (itemNameLabel != null)
            itemNameLabel.text = selected != null ? selected.itemName : "";

        // Gold total label intentionally not updated here — gold comes from a separate system

        // Drop prompt visibility
        if (dropPromptLabel != null)
            dropPromptLabel.enabled = selected != null;
    }
}
