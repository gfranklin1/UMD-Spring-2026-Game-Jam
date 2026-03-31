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
    [SerializeField] private Transform[] spawnPoints;

    private int _nextIndex;

    private void Start()
    {
        if (NetworkManager.Singleton == null) return;
        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
    }

    private void OnDestroy()
    {
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

        Vector3 pos = spawnPoints[_nextIndex % spawnPoints.Length].position;
        _nextIndex++;

        var rpcParams = new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } }
        };
        pc.AssignSpawnPointClientRpc(pos, rpcParams);
    }
}
