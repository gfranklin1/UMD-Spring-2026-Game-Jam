using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Singleton game manager. Persists across scenes.
/// Handles restart flow after game over.
/// </summary>
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    private bool _returningToMenu = false;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    /// <summary>
    /// Register the client-disconnect callback each time gameplay networking starts.
    /// Called by NetworkSetup after StartHost/StartClient.
    /// </summary>
    public void RegisterNetworkCallbacks()
    {
        if (NetworkManager.Singleton == null) return;
        NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (_returningToMenu) return;
        if (NetworkManager.Singleton == null) return;

        // On the host, this fires for other clients leaving — ignore.
        // On a client, this fires when OUR connection to the host is lost.
        bool weAreClient = !NetworkManager.Singleton.IsServer;
        if (weAreClient)
            RestartGame();
    }

    public void RestartGame()
    {
        if (_returningToMenu) return;
        _returningToMenu = true;

        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;

            if (NetworkManager.Singleton.IsListening)
                NetworkManager.Singleton.Shutdown();

            // Destroy the NetworkManager so its DontDestroyOnLoad objects
            // (NetworkHUD, etc.) don't persist into the MainMenu scene.
            Destroy(NetworkManager.Singleton.gameObject);
        }

        SceneManager.LoadScene("MainMenu");
        _returningToMenu = false;
    }

    /// <summary>
    /// User-initiated leave from the pause menu.
    /// Client gracefully disconnects without ending the host session.
    /// Host leaving shuts down the session (all clients get disconnected).
    /// </summary>
    public void LeaveGame()
    {
        if (_returningToMenu) return;
        _returningToMenu = true;

        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;

            if (NetworkManager.Singleton.IsListening)
                NetworkManager.Singleton.Shutdown();

            Destroy(NetworkManager.Singleton.gameObject);
        }

        SceneManager.LoadScene("MainMenu");
        _returningToMenu = false;
    }
}
