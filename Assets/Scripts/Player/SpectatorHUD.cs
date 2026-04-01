using Unity.Collections;
using UnityEngine;
using UnityEngine.UI;

public class SpectatorHUD : MonoBehaviour
{
    [SerializeField] private Text _nameText;
    [SerializeField] private Text _quotaText;

    private GameObject _panel;
    private PlayerController _currentTarget;

    private void Awake()
    {
        var t = transform.Find("SpectatorCanvas");
        _panel = t != null ? t.gameObject : null;
        if (_panel) _panel.SetActive(false);
    }

    public void Show() { if (_panel) _panel.SetActive(true);  }
    public void Hide() { if (_panel) _panel.SetActive(false); }

    public void SetTarget(PlayerController target)
    {
        if (_currentTarget != null)
            _currentTarget.NetworkPlayerName.OnValueChanged -= OnTargetNameChanged;

        _currentTarget = target;

        if (_currentTarget != null)
            _currentTarget.NetworkPlayerName.OnValueChanged += OnTargetNameChanged;

        Show();
        RefreshName();
    }

    private void Update()
    {
        if (_quotaText == null || QuotaManager.Instance == null) return;
        var qm   = QuotaManager.Instance;
        int gold = GoldTracker.Instance?.TotalGold ?? 0;
        _quotaText.text = $"Day {qm.CurrentDay}/{qm.DaysPerCycle}\nQuota: {gold}/{qm.CurrentQuotaTarget}g";
    }

    private void OnTargetNameChanged(FixedString64Bytes _, FixedString64Bytes newVal) => RefreshName();

    private void RefreshName()
    {
        if (_nameText == null) return;
        _nameText.text = _currentTarget != null
            ? $"Spectating: {_currentTarget.NetworkPlayerName.Value}"
            : "Spectating";
    }
}
