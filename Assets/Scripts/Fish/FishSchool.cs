using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Manages a group of fish: aggregates school center / average velocity,
/// and triggers flee behaviour on all members when a player gets too close.
/// Run this before FishAI via Project Settings > Script Execution Order (e.g. -50).
/// </summary>
public class FishSchool : MonoBehaviour
{
    [Tooltip("Fish instances belonging to this school. Populated by FishSpawner at runtime.")]
    public List<FishAI> members = new();

    [Header("Flee Trigger")]
    [Tooltip("If any player enters this radius around the school centre, all members are told to flee.")]
    public float schoolFleeRadius = 12f;

    [Header("Performance")]
    [Tooltip("How often (seconds) to re-query the scene for PlayerController instances.")]
    public float playerCheckInterval = 0.5f;

    public Vector3 SchoolCenter    { get; private set; }
    public Vector3 AverageVelocity { get; private set; }

    private PlayerController[] _players = System.Array.Empty<PlayerController>();
    private float _playerRefreshTimer;

    private void Update()
    {
        RecalculateAggregates();

        // Throttled player search
        _playerRefreshTimer -= Time.deltaTime;
        if (_playerRefreshTimer <= 0f)
        {
            _players = FindObjectsByType<PlayerController>(FindObjectsSortMode.None);
            _playerRefreshTimer = playerCheckInterval;
        }

        // Find closest player to school centre
        Vector3 closestPos = Vector3.zero;
        float   closestSq  = float.MaxValue;
        bool    found      = false;

        foreach (var pc in _players)
        {
            if (pc == null) continue;
            float sq = (pc.transform.position - SchoolCenter).sqrMagnitude;
            if (sq < closestSq) { closestSq = sq; closestPos = pc.transform.position; found = true; }
        }

        if (found && closestSq < schoolFleeRadius * schoolFleeRadius)
        {
            foreach (var m in members)
                if (m != null) m.TriggerFlee(closestPos);
        }
    }

    private void RecalculateAggregates()
    {
        if (members.Count == 0)
        {
            SchoolCenter    = transform.position;
            AverageVelocity = Vector3.zero;
            return;
        }

        Vector3 sumPos = Vector3.zero;
        Vector3 sumVel = Vector3.zero;
        int count = 0;

        foreach (var m in members)
        {
            if (m == null) continue;
            sumPos += m.transform.position;
            sumVel += m.Velocity;
            count++;
        }

        if (count > 0)
        {
            SchoolCenter    = sumPos / count;
            AverageVelocity = sumVel / count;
        }
    }
}
