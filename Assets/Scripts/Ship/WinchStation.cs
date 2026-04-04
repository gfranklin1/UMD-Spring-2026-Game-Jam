using UnityEngine;

/// <summary>
/// A topside station where a player holds Space to reel in (pull diver up + shorten rope)
/// or Ctrl to pay out (extend rope so diver can go deeper).
/// Pull/lower speeds ramp up while held and decay when released.
///
/// Pure local MonoBehaviour — all networking is handled by PlayerController,
/// which reads CurrentPullSpeed/CurrentLowerSpeed and sends it via its own ServerRpc.
/// </summary>
[RequireComponent(typeof(Collider))]
public class WinchStation : MonoBehaviour, IInteractable
{
    [SerializeField] private string stationName       = "Winch";
    [SerializeField] private float  winchPullSpeed    = 3f;   // m/s reel-in rate (upward velocity + rope shortening)
    [SerializeField] private float  winchLowerSpeed   = 2f;   // m/s pay-out rate (rope extension)
    [SerializeField] private float  winchAcceleration = 6f;   // how fast pull/lower ramps up (m/s²)

    [Header("Rope Attachment")]
    [Tooltip("Assign a child Transform at the point where the comms rope exits the winch.")]
    [SerializeField] private Transform _ropePort;

    /// <summary>Transform at the rope port (used as comms rope start anchor).</summary>
    public Transform RopeTransform => _ropePort != null ? _ropePort : transform;

    /// <summary>World position where the comms rope connects to this winch.</summary>
    public Vector3 RopePosition => RopeTransform.position;

    private PlayerController _operator;
    private bool _isCranking;
    private bool _isLowering;

    /// <summary>Current reel-in speed (0 when idle, ramps to winchPullSpeed while cranking).</summary>
    public float CurrentPullSpeed { get; private set; }

    /// <summary>Current pay-out speed (0 when idle, ramps to winchLowerSpeed while lowering).</summary>
    public float CurrentLowerSpeed { get; private set; }

    // ── IInteractable ─────────────────────────────────────────────────────────

    public string GetPromptText(PlayerController viewer) => _operator == null ? $"[E] Use {stationName}" : "In use";
    public float  HoldDurationFor(PlayerController viewer) => 0f;

    public void OnInteractStart(PlayerController player)
    {
        if (_operator != null) return;
        _operator = player;
        player.LockToStation(this);
    }

    public void OnInteractHold(PlayerController player) { }
    public void OnInteractCancel(PlayerController player) { }

    public void Release(PlayerController player)
    {
        if (_operator == player)
        {
            _operator = null;
            _isCranking = false;
            _isLowering = false;
            CurrentPullSpeed = 0f;
            CurrentLowerSpeed = 0f;
        }
    }

    // ── Input (called by PlayerController each frame while at this station) ──

    public void SetCranking(bool cranking) => _isCranking = cranking;
    public void SetLowering(bool lowering) => _isLowering = lowering;

    public void OnOperatorLeft(PlayerController op)
    {
        _isCranking = false;
        _isLowering = false;
        CurrentPullSpeed = 0f;
        CurrentLowerSpeed = 0f;
    }

    // ── Ramp speeds each frame ──────────────────────────────────────────────

    private void Update()
    {
        if (_operator == null) return;

        // Pull takes priority over lower
        if (_isCranking)
        {
            CurrentPullSpeed = Mathf.MoveTowards(CurrentPullSpeed, winchPullSpeed, winchAcceleration * Time.deltaTime);
            CurrentLowerSpeed = Mathf.MoveTowards(CurrentLowerSpeed, 0f, winchAcceleration * 2f * Time.deltaTime);
        }
        else if (_isLowering)
        {
            CurrentLowerSpeed = Mathf.MoveTowards(CurrentLowerSpeed, winchLowerSpeed, winchAcceleration * Time.deltaTime);
            CurrentPullSpeed = Mathf.MoveTowards(CurrentPullSpeed, 0f, winchAcceleration * 2f * Time.deltaTime);
        }
        else
        {
            CurrentPullSpeed = Mathf.MoveTowards(CurrentPullSpeed, 0f, winchAcceleration * 2f * Time.deltaTime);
            CurrentLowerSpeed = Mathf.MoveTowards(CurrentLowerSpeed, 0f, winchAcceleration * 2f * Time.deltaTime);
        }
    }
}
