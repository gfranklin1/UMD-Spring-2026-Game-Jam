using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

/// <summary>
/// Thin wrapper around Unity Netcode for GameObjects startup.
/// Attach to the NetworkManager GameObject in the scene.
///
/// For LAN play:
///   Host  — one player calls StartHost() (or run with --host flag)
///   Client — other players call StartClient(hostIPAddress)
///
/// The default port is 7777. Both machines must be on the same network
/// (or the host must have port 7777 open for internet play).
/// </summary>
public class NetworkSetup : MonoBehaviour
{
    [SerializeField] private string defaultAddress = "127.0.0.1";
    [SerializeField] private ushort port = 7777;
    private bool _startAttemptInProgress;

    private void Start()
    {
        // Command-line flag takes priority (CI / dedicated server builds)
        foreach (string arg in System.Environment.GetCommandLineArgs())
        {
            if (arg == "--host") { StartHost(); return; }
        }

        // Read intent set by MainMenuController before loading this scene
        switch (NetworkLauncher.LaunchIntent)
        {
            case NetworkLauncher.Intent.Host:
                NetworkLauncher.Clear();
                StartHost();
                GameManager.Instance?.RegisterNetworkCallbacks();
                break;
            case NetworkLauncher.Intent.Client:
                string addr = NetworkLauncher.ClientAddress;
                NetworkLauncher.Clear();
                StartClient(addr);
                GameManager.Instance?.RegisterNetworkCallbacks();
                break;
        }
    }

    /// <summary>Start as host (runs both server and local client).</summary>
    public void StartHost()
    {
        if (_startAttemptInProgress) return;
        if (NetworkManager.Singleton == null) return;
        if (NetworkManager.Singleton.IsListening) return;

        _startAttemptInProgress = true;

        // If a stale transport/driver is still alive in this editor process, clear it first.
        NetworkManager.Singleton.Shutdown();
        SetTransport(defaultAddress, port);
        bool ok = NetworkManager.Singleton.StartHost();
        _startAttemptInProgress = false;

        if (ok) Debug.Log($"[NetworkSetup] Hosting on :{port}");
        else    Debug.LogError($"[NetworkSetup] Failed to host on :{port}");
    }

    /// <summary>Connect to a host at the given IP address.</summary>
    public void StartClient(string address)
    {
        if (_startAttemptInProgress) return;
        if (NetworkManager.Singleton == null) return;
        if (NetworkManager.Singleton.IsListening) return;

        _startAttemptInProgress = true;

        // If a stale transport/driver is still alive in this editor process, clear it first.
        NetworkManager.Singleton.Shutdown();
        SetTransport(address, port);
        bool ok = NetworkManager.Singleton.StartClient();
        _startAttemptInProgress = false;

        if (ok) Debug.Log($"[NetworkSetup] Connecting to {address}:{port}");
        else    Debug.LogError($"[NetworkSetup] Failed to start client for {address}:{port}");
    }

    /// <summary>Start as a dedicated server (no local player).</summary>
    public void StartServer()
    {
        if (_startAttemptInProgress) return;
        if (NetworkManager.Singleton == null) return;
        if (NetworkManager.Singleton.IsListening) return;

        _startAttemptInProgress = true;

        NetworkManager.Singleton.Shutdown();
        SetTransport(defaultAddress, port);
        bool ok = NetworkManager.Singleton.StartServer();
        _startAttemptInProgress = false;

        if (ok) Debug.Log($"[NetworkSetup] Server listening on :{port}");
        else    Debug.LogError($"[NetworkSetup] Failed to start server on :{port}");
    }

    private void SetTransport(string address, ushort p)
    {
        var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        if (transport != null)
            transport.SetConnectionData(address, p);
    }
}
