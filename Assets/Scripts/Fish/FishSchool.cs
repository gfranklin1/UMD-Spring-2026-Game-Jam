using System.Collections.Generic;
using UnityEngine;
using FishAlive;

/// <summary>
/// Manages a school of fish: each member gets a personal target GO parented to the
/// school's wander centre so they spread out naturally while moving as a group.
/// Triggers flee when a player enters schoolFleeRadius, then restores formation.
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

    [Header("Wander")]
    [Tooltip("Seconds between wander target relocations (±2 s jitter).")]
    public float wanderInterval = 10f;
    public Vector3 wanderBoundsMin;
    public Vector3 wanderBoundsMax;

    [Header("Performance")]
    [Tooltip("How often (seconds) to re-query the scene for PlayerController instances.")]
    public float playerCheckInterval = 0.5f;

    public Vector3 SchoolCenter => transform.position;

    private GameObject _wanderTarget;
    private GameObject _fleeTargetGO;
    private float _fleeTimer;
    private float _wanderTimer;

    // Per-fish personal targets (children of _wanderTarget, offset within school spread)
    private readonly List<GameObject> _personalTargets = new();

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

    /// <summary>Called by FishSpawner once per fish to register it with its personal target GO.</summary>
    public void AddMember(FishMotion motion, GameObject personalTarget)
    {
        members.Add(motion);
        _personalTargets.Add(personalTarget);
    }

    /// <summary>Called by FishSpawner to tell the school which GO is the wander centre.</summary>
    public void SetWanderTarget(GameObject go) => _wanderTarget = go;

    /// <summary>Called by FishSpawner to set the map-wide bounds the school may roam within.</summary>
    public void SetWanderBounds(Vector3 min, Vector3 max)
    {
        wanderBoundsMin = min;
        wanderBoundsMax = max;
        _wanderTimer = Random.Range(0f, wanderInterval); // stagger first relocation
    }

    private void Update()
    {
        // Tick flee timer; restore when expired
        if (_fleeTimer > 0f)
        {
            _fleeTimer -= Time.deltaTime;
            if (_fleeTimer <= 0f)
                RestoreTargets();
        }

        // Periodically move wander target to a new map position
        if (_fleeTimer <= 0f && wanderBoundsMin != wanderBoundsMax)
        {
            _wanderTimer -= Time.deltaTime;
            if (_wanderTimer <= 0f)
            {
                PickNewWanderPoint();
                _wanderTimer = wanderInterval + Random.Range(-2f, 2f);
            }
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

    private void PickNewWanderPoint()
    {
        if (_wanderTarget == null) return;
        _wanderTarget.transform.position = new Vector3(
            Random.Range(wanderBoundsMin.x, wanderBoundsMax.x),
            Random.Range(wanderBoundsMin.y, wanderBoundsMax.y),
            Random.Range(wanderBoundsMin.z, wanderBoundsMax.z));
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
        for (int i = 0; i < members.Count; i++)
        {
            if (members[i] != null && i < _personalTargets.Count)
                members[i].target = _personalTargets[i];
        }
    }
}
