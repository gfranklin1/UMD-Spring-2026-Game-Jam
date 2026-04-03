using System.Collections.Generic;
using UnityEngine;
using FishAlive;

/// <summary>
/// Manages a group of fish: triggers flee behaviour when a player gets too close,
/// then restores normal wandering after the flee duration.
/// Works with FishMotion (DenysAlmaral FishAlive) — target swapping drives flee.
/// </summary>
public class FishSchool : MonoBehaviour
{
    [Tooltip("Fish instances belonging to this school. Populated by FishSpawner at runtime.")]
    public List<FishMotion> members = new();

    [Header("Flee Trigger")]
    [Tooltip("If any player enters this radius around the school centre, all members flee.")]
    public float schoolFleeRadius = 12f;
    [Tooltip("Seconds fish flee before returning to their wander target.")]
    public float fleeDuration = 5f;

    [Header("Performance")]
    [Tooltip("How often (seconds) to re-query the scene for PlayerController instances.")]
    public float playerCheckInterval = 0.5f;

    public Vector3 SchoolCenter => transform.position;

    private GameObject _wanderTarget;
    private GameObject _fleeTargetGO;
    private float _fleeTimer;

    private PlayerController[] _players = System.Array.Empty<PlayerController>();
    private float _playerRefreshTimer;

    // ─────────────────────────────────────────────────────────────────

    private void Awake()
    {
        _fleeTargetGO = new GameObject("FleeTarget_" + name);
        _fleeTargetGO.transform.SetParent(transform);
    }

    private void OnDestroy()
    {
        if (_fleeTargetGO) Destroy(_fleeTargetGO);
    }

    /// <summary>Called by FishSpawner to tell the school which GO each fish should wander around.</summary>
    public void SetWanderTarget(GameObject go) => _wanderTarget = go;

    private void Update()
    {
        // Tick flee timer; restore when expired
        if (_fleeTimer > 0f)
        {
            _fleeTimer -= Time.deltaTime;
            if (_fleeTimer <= 0f)
                RestoreTargets();
        }

        // Throttled player search
        _playerRefreshTimer -= Time.deltaTime;
        if (_playerRefreshTimer <= 0f)
        {
            _players = FindObjectsByType<PlayerController>(FindObjectsSortMode.None);
            _playerRefreshTimer = playerCheckInterval;
        }

        // Only bother if not already fleeing
        if (_fleeTimer > 0f) return;

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
            TriggerFlee(closestPos);
    }

    private void TriggerFlee(Vector3 playerPos)
    {
        Vector3 away = SchoolCenter - playerPos;
        if (away.sqrMagnitude < 0.001f) away = Random.insideUnitSphere;
        away.Normalize();

        _fleeTargetGO.transform.position = SchoolCenter + away * 20f;
        _fleeTimer = fleeDuration;

        foreach (var m in members)
        {
            if (m != null) m.target = _fleeTargetGO;
        }
    }

    private void RestoreTargets()
    {
        foreach (var m in members)
        {
            if (m != null) m.target = _wanderTarget;
        }
    }
}
