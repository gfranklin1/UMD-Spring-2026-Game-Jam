using UnityEngine;

/// <summary>
/// A topside station where a player manually cranks an air pump for the diver.
/// The operator presses Space (Jump) to add pump momentum.
/// Momentum decays when they stop cranking; flow rate = momentum * maxPumpRate.
///
/// This is a pure local MonoBehaviour — all networking is handled by PlayerController,
/// which reads CurrentFlowRate and sends it via its own ServerRpc.
/// </summary>
[RequireComponent(typeof(Collider))]
public class AirPumpStation : MonoBehaviour, IInteractable
{
    [SerializeField] private string stationName   = "Air Pump";
    [SerializeField] private float maxPumpRate    = 3f;    // oxygen/s at full momentum
    [SerializeField] private float crankStrength  = 0.3f;  // momentum added per crank press
    [SerializeField] private float crankUpgradedStrength = 0.5f;
    [SerializeField] private float momentumDecay  = 0.15f; // momentum lost per second (0–1 scale)

    private float curCrankStrength;
    public bool upgraded = false;

    [Header("Hose Attachment")]
    [Tooltip("Assign an empty child GameObject at the nozzle/port where the air hose exits the pump box.")]
    [SerializeField] private Transform _hosePort;

    /// <summary>World position where the air hose connects to this pump.</summary>
    public Vector3 HosePosition => _hosePort != null
        ? _hosePort.position
        : transform.position + transform.up * 0.5f;

    /// <summary>Transform at the hose port (used as rope start anchor).</summary>
    public Transform HoseTransform => _hosePort != null ? _hosePort : transform;

    private PlayerController _operator;
    private float _pumpMomentum;

    /// <summary>Current oxygen flow rate (momentum * maxPumpRate). Read by PlayerController.</summary>
    public float CurrentFlowRate => _pumpMomentum * maxPumpRate;

    /// <summary>Pump momentum 0–1. 0 = no flow, 1 = full flow.</summary>
    public float PumpMomentum => _pumpMomentum;
    void Start()
    {
        curCrankStrength = crankStrength;
    }

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
        if (_operator == player) _operator = null;
    }

    // ── Crank input (called by PlayerController while at this station) ────────

    public void OnCrank()
    {
        _pumpMomentum = Mathf.Min(1f, _pumpMomentum + curCrankStrength);
        Debug.Log($"[Pump] Crank! momentum={_pumpMomentum:F2} flow={CurrentFlowRate:F2}");
    }

    public void OnOperatorLeft(PlayerController op) => _pumpMomentum = 0f;

    // ── Decay momentum each frame ────────────────────────────────────────────

    private void Update()
    {
        if (_operator == null) return;
        
        _pumpMomentum = Mathf.Max(0f, _pumpMomentum - momentumDecay * Time.deltaTime);
    }

    //  ── Upgrade ────────────────────────────────────────────
    
    public void Upgrade()
    {
        upgraded = true;
        curCrankStrength = crankUpgradedStrength;
    }
}
