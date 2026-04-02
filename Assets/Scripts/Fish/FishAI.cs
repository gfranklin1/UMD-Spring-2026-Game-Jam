using UnityEngine;

/// <summary>
/// Per-fish swimming AI. Handles boid-style flocking, player-reactive flee,
/// smooth rotation, body tilt, and vertical drift. Works on any mesh — no
/// Animator or Rigidbody required.
/// Run after FishSchool via Project Settings > Script Execution Order (e.g. -40).
/// </summary>
public class FishAI : MonoBehaviour
{
    // ── Wired by FishSpawner ──────────────────────────────────────────
    [Tooltip("The school this fish belongs to. Set by FishSpawner at spawn time.")]
    public FishSchool school;

    // ── Speed ────────────────────────────────────────────────────────
    [Header("Speed")]
    public float wanderSpeed      = 2.5f;   // m/s while wandering
    public float fleeSpeed        = 6.5f;   // m/s while fleeing
    public float returnSpeed      = 3.5f;   // m/s while returning to school
    public float accelerationRate = 4.0f;   // MoveTowards rate (m/s per second)
    public float rotationSpeed    = 120f;   // degrees/s for yaw tracking

    // ── Boid Forces ──────────────────────────────────────────────────
    [Header("Boid Forces")]
    public float separationRadius = 1.5f;
    public float separationWeight = 1.8f;
    public float alignmentWeight  = 0.8f;
    public float cohesionWeight   = 0.6f;

    // ── Flee ─────────────────────────────────────────────────────────
    [Header("Flee")]
    [Tooltip("Normal fish flee when player enters this radius.")]
    public float fleeRadius      = 8f;
    [Tooltip("Brave fish only flee when player enters this (smaller) radius.")]
    public float smallFleeRadius = 3.5f;
    [Tooltip("Roughly 20% of fish should have this set to true by FishSpawner.")]
    public bool  isBrave         = false;
    [Tooltip("Seconds the fish stays in flee state before returning to school.")]
    public float fleeDuration    = 5f;

    // ── Wander ───────────────────────────────────────────────────────
    [Header("Wander")]
    public float waypointReachedDist = 1.2f;
    public float waypointTimeout     = 8f;
    public float verticalDriftAmp    = 0.25f;  // metres of idle bob
    public float verticalDriftFreq   = 0.6f;   // Hz of idle bob

    // ── Tilt ─────────────────────────────────────────────────────────
    [Header("Tilt")]
    public float maxPitchAngle = 25f;   // degrees up/down
    public float maxRollAngle  = 30f;   // degrees bank into turn
    public float pitchSpeed    = 90f;   // degrees/s
    public float rollSpeed     = 60f;   // degrees/s

    // ── Public read-only state ────────────────────────────────────────
    public Vector3 Velocity { get; private set; }

    // ── Private state ─────────────────────────────────────────────────
    private enum FishState { Wandering, Fleeing, Returning }
    private FishState _state = FishState.Wandering;

    private Vector3 _currentVelocity;
    private Vector3 _desiredVelocity;

    private Vector3 _waypoint;
    private float   _waypointTimer;

    private Vector3 _fleeFrom;
    private float   _fleeTimer;

    private Vector3 _boundsMin;
    private Vector3 _boundsMax;
    private bool    _boundsEnabled;

    private float _driftPhaseOffset;
    private float _currentPitch;
    private float _currentRoll;
    private float _lastYaw;
    private Vector3 _separationForce;

    // ─────────────────────────────────────────────────────────────────
    #region Lifecycle

    private void Awake()
    {
        _driftPhaseOffset = Random.Range(0f, Mathf.PI * 2f);
        _lastYaw          = transform.eulerAngles.y;
    }

    private void Start()
    {
        PickNewWaypoint();
        _currentVelocity = transform.forward * wanderSpeed * 0.5f;
    }

    private void Update()
    {
        float dt = Time.deltaTime;

        GatherBoidInputs();
        TickStateMachine(dt);
        EnforceBounds();
        ApplyVerticalDrift(dt);

        transform.position += _currentVelocity * dt;
        Velocity = _currentVelocity;

        SmoothRotation(dt);
        ApplyTilt(dt);
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────
    #region Public API

    public void EnableBounds(Vector3 min, Vector3 max)
    {
        _boundsMin     = min;
        _boundsMax     = max;
        _boundsEnabled = true;
    }

    public void TriggerFlee(Vector3 fleeFrom)
    {
        float effectiveRadius = isBrave ? smallFleeRadius : fleeRadius;
        if (Vector3.Distance(transform.position, fleeFrom) > effectiveRadius) return;

        _fleeFrom  = fleeFrom;
        _fleeTimer = fleeDuration;
        _state     = FishState.Fleeing;
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────
    #region Boid

    private void GatherBoidInputs()
    {
        _separationForce = Vector3.zero;
        if (school == null) return;

        Vector3 myPos = transform.position;
        foreach (var m in school.members)
        {
            if (m == null || m == this) continue;
            Vector3 diff = myPos - m.transform.position;
            float dist   = diff.magnitude;
            if (dist > 0f && dist < separationRadius)
                _separationForce += diff.normalized / dist;
        }
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────
    #region State Machine

    private void TickStateMachine(float dt)
    {
        switch (_state)
        {
            case FishState.Wandering:  TickWandering(dt);  break;
            case FishState.Fleeing:    TickFleeing(dt);    break;
            case FishState.Returning:  TickReturning(dt);  break;
        }
    }

    private void TickWandering(float dt)
    {
        _waypointTimer -= dt;

        bool reached = Vector3.Distance(transform.position, _waypoint) < waypointReachedDist;
        if (reached || _waypointTimer <= 0f)
            PickNewWaypoint();

        Vector3 toWaypoint = (_waypoint - transform.position).normalized;

        Vector3 alignment = Vector3.zero;
        Vector3 cohesion  = Vector3.zero;
        if (school != null)
        {
            alignment = school.AverageVelocity - _currentVelocity;
            cohesion  = (school.SchoolCenter - transform.position).normalized;
        }

        _desiredVelocity = toWaypoint
                         + _separationForce * separationWeight
                         + alignment        * alignmentWeight
                         + cohesion         * cohesionWeight;

        _desiredVelocity = Vector3.ClampMagnitude(_desiredVelocity, wanderSpeed);
        _currentVelocity = Vector3.MoveTowards(_currentVelocity, _desiredVelocity, accelerationRate * dt);
    }

    private void TickFleeing(float dt)
    {
        _fleeTimer -= dt;
        if (_fleeTimer <= 0f)
        {
            _state = FishState.Returning;
            return;
        }

        Vector3 away = (transform.position - _fleeFrom);
        if (away.sqrMagnitude < 0.001f) away = Random.insideUnitSphere;
        away.Normalize();

        _desiredVelocity = away * fleeSpeed + _separationForce * separationWeight;
        _desiredVelocity = Vector3.ClampMagnitude(_desiredVelocity, fleeSpeed);
        _currentVelocity = Vector3.MoveTowards(_currentVelocity, _desiredVelocity, accelerationRate * 2f * dt);
    }

    private void TickReturning(float dt)
    {
        Vector3 center = school != null ? school.SchoolCenter : transform.position;

        if (Vector3.Distance(transform.position, center) < waypointReachedDist * 2f)
        {
            _state = FishState.Wandering;
            PickNewWaypoint();
            return;
        }

        Vector3 toCenter = (center - transform.position).normalized;
        _desiredVelocity = toCenter * returnSpeed + _separationForce * separationWeight;
        _desiredVelocity = Vector3.ClampMagnitude(_desiredVelocity, returnSpeed);
        _currentVelocity = Vector3.MoveTowards(_currentVelocity, _desiredVelocity, accelerationRate * dt);
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────
    #region Movement Helpers

    private void EnforceBounds()
    {
        if (!_boundsEnabled) return;

        Vector3 p = transform.position;
        Vector3 v = _currentVelocity;

        if (p.x < _boundsMin.x && v.x < 0f) v.x = -v.x;
        if (p.x > _boundsMax.x && v.x > 0f) v.x = -v.x;
        if (p.y < _boundsMin.y && v.y < 0f) v.y = -v.y;
        if (p.y > _boundsMax.y && v.y > 0f) v.y = -v.y;
        if (p.z < _boundsMin.z && v.z < 0f) v.z = -v.z;
        if (p.z > _boundsMax.z && v.z > 0f) v.z = -v.z;

        _currentVelocity = v;

        // Hard clamp so a fish that overshot (e.g. first frame) is snapped back
        p.x = Mathf.Clamp(p.x, _boundsMin.x, _boundsMax.x);
        p.y = Mathf.Clamp(p.y, _boundsMin.y, _boundsMax.y);
        p.z = Mathf.Clamp(p.z, _boundsMin.z, _boundsMax.z);
        transform.position = p;
    }

    private void ApplyVerticalDrift(float dt)
    {
        if (_state != FishState.Wandering) return;
        if (Vector3.Distance(transform.position, _waypoint) > waypointReachedDist * 3f) return;

        float drift = Mathf.Sin(Time.time * verticalDriftFreq * Mathf.PI * 2f + _driftPhaseOffset)
                    * verticalDriftAmp;
        transform.position += Vector3.up * drift * dt;
    }

    private void SmoothRotation(float dt)
    {
        if (_currentVelocity.sqrMagnitude < 0.001f) return;

        Quaternion targetRot = Quaternion.LookRotation(_currentVelocity.normalized, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, rotationSpeed * dt);
    }

    private void ApplyTilt(float dt)
    {
        float speedXZ = new Vector2(_currentVelocity.x, _currentVelocity.z).magnitude;

        float targetPitch = speedXZ > 0.01f
            ? -Mathf.Clamp(_currentVelocity.y / speedXZ, -1f, 1f) * maxPitchAngle
            : 0f;

        float yaw      = transform.eulerAngles.y;
        float yawDelta = Mathf.DeltaAngle(_lastYaw, yaw);
        float targetRoll = Mathf.Clamp(-yawDelta * 3f, -maxRollAngle, maxRollAngle);

        _currentPitch = Mathf.MoveTowards(_currentPitch, targetPitch, pitchSpeed * dt);
        _currentRoll  = Mathf.MoveTowards(_currentRoll,  targetRoll,  rollSpeed  * dt);

        transform.rotation *= Quaternion.Euler(_currentPitch, 0f, _currentRoll);
        _lastYaw = yaw;
    }

    private void PickNewWaypoint()
    {
        if (_boundsEnabled)
        {
            _waypoint = new Vector3(
                Random.Range(_boundsMin.x, _boundsMax.x),
                Random.Range(_boundsMin.y, _boundsMax.y),
                Random.Range(_boundsMin.z, _boundsMax.z));
        }
        else
        {
            _waypoint = transform.position + Random.insideUnitSphere * 5f;
        }

        _waypointTimer = waypointTimeout + Random.Range(-1f, 2f);
    }

    #endregion
}
