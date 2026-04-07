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
    private bool _requestedStart;

    private void Awake() => _setup = GetComponent<NetworkSetup>();

    private void OnGUI()
    {
        if (NetworkManager.Singleton == null) return;
        if (NetworkManager.Singleton.IsListening)
        {
            _requestedStart = false;
            return;
        }

        GUILayout.BeginArea(new Rect(10, 10, 220, 120));

        if (!_requestedStart && GUILayout.Button("Start Host"))
        {
            _requestedStart = true;
            _setup?.StartHost();
        }

        GUILayout.BeginHorizontal();
        _clientAddress = GUILayout.TextField(_clientAddress, GUILayout.Width(150));
        if (!_requestedStart && GUILayout.Button("Join"))
        {
            _requestedStart = true;
            _setup?.StartClient(_clientAddress);
        }
        GUILayout.EndHorizontal();

        GUILayout.EndArea();
    }
}
