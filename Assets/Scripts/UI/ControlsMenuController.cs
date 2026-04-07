using UnityEngine;

/// <summary>
/// Minimal controller for the Controls reference panel.
/// The Back button calls Close() via a persistent Unity event listener.
/// </summary>
public class ControlsMenuController : MonoBehaviour
{
    public void Close() => gameObject.SetActive(false);
}
