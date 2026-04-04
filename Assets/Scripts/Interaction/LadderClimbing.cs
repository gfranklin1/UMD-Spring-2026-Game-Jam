using UnityEngine;

/// <summary>
/// IInteractable ladder on the ship side. Player grabs it from any state
/// (including Underwater), climbs with W/S, auto-exits at the top anchor.
/// Multiplayer sync is handled entirely by the existing _netPlatformOffset system.
///
/// All spatial math is world-space and anchor-relative, so it is immune to
/// the ladder's baked rotation and to ship buoyancy/pitch/roll.
/// </summary>
[RequireComponent(typeof(Collider))]
public class LadderClimbing : MonoBehaviour, IInteractable
{
    [SerializeField] private Transform topAnchor;
    [SerializeField] private Transform bottomAnchor;
    [SerializeField] private float     _climbSpeed = 2.5f;
    [SerializeField] private float     _faceOffset = 0.45f;  // metres out from the ladder face

    public float ClimbSpeed  => _climbSpeed;

    /// <summary>World-space distance between the two anchors (invariant to ship rotation).</summary>
    public float LadderLength => Vector3.Distance(topAnchor.position, bottomAnchor.position);

    /// <summary>World-space direction up the rungs (from bottom to top anchor).</summary>
    public Vector3 LadderUp => (topAnchor.position - bottomAnchor.position).normalized;

    public Vector3 TopExitPosition => topAnchor.position;

    // ── IInteractable ─────────────────────────────────────────────────────────

    public string GetPromptText(PlayerController viewer) => "[E] Climb";
    public float  HoldDurationFor(PlayerController viewer) => 0f;

    public void OnInteractStart(PlayerController player) => player.GrabLadder(this);
    public void OnInteractHold(PlayerController player)   { }
    public void OnInteractCancel(PlayerController player) { }
    public void Release(PlayerController player)          { }

    // ── Spatial helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// World position on the ladder at parameter t (metres from the bottom anchor).
    /// The outward direction is derived from the ship's centre so it always points
    /// away from the hull regardless of ship orientation.
    /// </summary>
    public Vector3 GetPositionAtT(float t)
    {
        Vector3 up     = LadderUp;
        Vector3 onAxis = bottomAnchor.position + up * t;

        // Outward = direction from ship centre to the ladder, flattened to be
        // perpendicular to LadderUp.  Works correctly as the ship pitches/rolls.
        Vector3 shipToLadder = transform.position - transform.root.position;
        Vector3 outward      = Vector3.ProjectOnPlane(shipToLadder, up);
        if (outward.sqrMagnitude < 0.001f)
            outward = Vector3.ProjectOnPlane(transform.forward, up);
        outward.Normalize();

        return onAxis + outward * _faceOffset;
    }

    /// <summary>
    /// Projects the player's world position onto the ladder axis and returns t
    /// (clamped to [0, LadderLength]).  Used to initialise _ladderClimbT on grab.
    /// </summary>
    public float ProjectOntoLadder(Vector3 playerWorldPos)
    {
        float t = Vector3.Dot(playerWorldPos - bottomAnchor.position, LadderUp);
        return Mathf.Clamp(t, 0f, LadderLength);
    }
}
