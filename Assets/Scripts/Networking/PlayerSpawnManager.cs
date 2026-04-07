using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Server-side: assigns each connecting player to a deck spawn point so they
/// don't materialise inside the boat geometry.
/// Attach to any root GameObject in the scene and assign <see cref="spawnPoints"/>
/// in the inspector.
/// </summary>
public class PlayerSpawnManager : MonoBehaviour
{
    public static PlayerSpawnManager Instance { get; private set; }

    [SerializeField] private Transform[] spawnPoints;

    private int _nextIndex;

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        if (NetworkManager.Singleton == null) return;
        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
    }

    private void OnClientConnected(ulong clientId)
    {
        if (!NetworkManager.Singleton.IsServer) return;
        if (spawnPoints == null || spawnPoints.Length == 0) return;

        var playerObj = NetworkManager.Singleton.ConnectedClients[clientId].PlayerObject;
        if (playerObj == null) return;

        var pc = playerObj.GetComponent<PlayerController>();
        if (pc == null) return;

        int index = _nextIndex % spawnPoints.Length;
        _nextIndex++;

        var rpcParams = new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } }
        };
        pc.AssignSpawnPointClientRpc(index, rpcParams);
    }

    /// <summary>
    /// Returns the current world position of a spawn point by index.
    /// Used by PlayerController at respawn time so the position tracks the moving ship.
    /// </summary>
    public Vector3 GetSpawnPosition(int index)
    {
        if (spawnPoints == null || spawnPoints.Length == 0)
            return Vector3.zero;
        return spawnPoints[index % spawnPoints.Length].position;
    }
}
