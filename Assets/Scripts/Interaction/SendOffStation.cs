using UnityEngine;

public class SendOffStation : MonoBehaviour, IInteractable
{
    public string GetPromptText(PlayerController viewer)
    {
        return "[Hold E 2s] to Send Off Merchant";
    }

    public float HoldDurationFor(PlayerController viewer)
    {
        return 2;
    }

    public void OnInteractCancel(PlayerController player)
    {
        throw new System.NotImplementedException();
    }

    public void OnInteractHold(PlayerController player)
    {
        throw new System.NotImplementedException();
    }

    public void OnInteractStart(PlayerController player)
    {
        MerchantManager.Instance().SendOff();
    }

    public void Release(PlayerController player)
    {
        throw new System.NotImplementedException();
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
