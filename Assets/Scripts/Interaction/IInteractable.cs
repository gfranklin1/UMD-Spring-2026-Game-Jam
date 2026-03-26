public interface IInteractable
{
    string GetPromptText();
    void OnInteractStart(PlayerController player);   // fires on button press (started)
    void OnInteractHold(PlayerController player);    // fires after full hold (performed)
    void OnInteractCancel(PlayerController player);  // fires if hold released early
}
