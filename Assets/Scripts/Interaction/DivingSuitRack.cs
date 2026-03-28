using System.Collections;
using UnityEngine;

public class DivingSuitRack : MonoBehaviour, IInteractable
{
    [SerializeField] private float equipHoldTime   = 2f;
    [SerializeField] private float unequipHoldTime = 1f;

    private bool             _suitAvailable = true;
    private bool             _suitHasBoots  = true;   // false after the boots have been kicked off
    private PlayerController _suitWearer;   // who currently has the suit
    private Coroutine        _holdRoutine;

    public string GetPromptText()
    {
        if (_suitAvailable)  return $"[Hold E {equipHoldTime:F0}s] Equip Suit";
        if (_suitWearer != null) return $"[Hold E {unequipHoldTime:F0}s] Remove Suit";
        return "Suit in use";
    }

    public void OnInteractStart(PlayerController player)
    {
        // Equip: suit is available and player doesn't already have it
        if (_suitAvailable && _holdRoutine == null)
        {
            _holdRoutine = StartCoroutine(HoldRoutine(equipHoldTime, () =>
            {
                _suitAvailable = false;
                _suitWearer    = player;
                player.EquipSuit(this, _suitHasBoots);
            }));
            return;
        }

        // Unequip: player interacting is the one wearing the suit
        if (!_suitAvailable && player == _suitWearer && _holdRoutine == null)
        {
            _holdRoutine = StartCoroutine(HoldRoutine(unequipHoldTime, () =>
            {
                player.UnequipSuit();
            }));
        }
    }

    public void OnInteractHold(PlayerController player)   { }  // handled by coroutine
    public void OnInteractCancel(PlayerController player) { CancelHold(); }
    public void Release(PlayerController player)          { CancelHold(); }

    /// <summary>Called by PlayerController.UnequipSuit to return the suit.</summary>
    /// <param name="hadBoots">Whether the boots are still on — false if the diver kicked them off.</param>
    public void ReturnSuit(bool hadBoots)
    {
        _suitAvailable = true;
        _suitHasBoots  = hadBoots;   // boots don't magically reappear
        _suitWearer    = null;
    }

    private void CancelHold()
    {
        if (_holdRoutine != null)
        {
            StopCoroutine(_holdRoutine);
            _holdRoutine = null;
        }
    }

    private IEnumerator HoldRoutine(float duration, System.Action onComplete)
    {
        yield return new WaitForSeconds(duration);
        _holdRoutine = null;
        onComplete?.Invoke();
    }
}
