using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Minimal on-screen host/client buttons for playtesting.
/// Attach to the NetworkManager GameObject alongside NetworkSetup.
/// Remove or hide this in production.
/// </summary>
public class NetworkHUD : MonoBehaviour
{
    private NetworkSetup _setup;
    private string _clientAddress = "127.0.0.1";

    private void Awake() => _setup = GetComponent<NetworkSetup>();

    private void OnGUI()
    {
        if (NetworkManager.Singleton == null) return;
        if (NetworkManager.Singleton.IsListening)
            return;

        GUILayout.BeginArea(new Rect(10, 10, 220, 120));

        if (GUILayout.Button("Start Host"))
            _setup?.StartHost();

        GUILayout.BeginHorizontal();
        _clientAddress = GUILayout.TextField(_clientAddress, GUILayout.Width(150));
        if (GUILayout.Button("Join"))
            _setup?.StartClient(_clientAddress);
        GUILayout.EndHorizontal();

        GUILayout.EndArea();
    }
}
