public interface IInteractable
{
    string GetPromptText();
    float  HoldDuration { get; }                     // 0 = instant press, >0 = requires hold
    void OnInteractStart(PlayerController player);   // fires on button press (started)
    void OnInteractHold(PlayerController player);    // fires after full hold (performed)
    void OnInteractCancel(PlayerController player);  // fires if hold released early
    void Release(PlayerController player);           // called when player leaves a station
}
