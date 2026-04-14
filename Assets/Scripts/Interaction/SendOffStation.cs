using System.Collections;
using UnityEngine;

public class SendOffStation : MonoBehaviour, IInteractable
{
    [SerializeField] private float holdTime = 2f;

    private Coroutine _holdRoutine;

    public string GetPromptText(PlayerController viewer) => "[Hold E] Send Off Merchant";
    public float  HoldDurationFor(PlayerController viewer) => holdTime;

    public void OnInteractStart(PlayerController player)
    {
        if (_holdRoutine == null)
            _holdRoutine = StartCoroutine(HoldRoutine(player));
    }

    public void OnInteractCancel(PlayerController player) => CancelHold();
    public void OnInteractHold(PlayerController player)   { }
    public void Release(PlayerController player)          => CancelHold();

    private void CancelHold()
    {
        if (_holdRoutine != null) { StopCoroutine(_holdRoutine); _holdRoutine = null; }
    }

    private IEnumerator HoldRoutine(PlayerController player)
    {
        yield return new WaitForSeconds(holdTime);
        _holdRoutine = null;

        bool networked = Unity.Netcode.NetworkManager.Singleton != null
                      && Unity.Netcode.NetworkManager.Singleton.IsListening;
        if (networked)
        {
            player?.RequestMerchantSendOffServerRpc();
        }
        else
        {
            QuotaManager.Instance?.TriggerMerchantSendOff();
        }
    }
}
