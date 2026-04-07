using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Manages the air hose and comms rope attached to the diver using Verlet integration
/// for stable physics and a procedural tube mesh for 3D visuals.
/// Pin particle 0 at the station, pin particle N-1 at the player helmet attach point.
/// </summary>
[DefaultExecutionOrder(100)] // run after ShipBuoyancy so station transforms are up-to-date
public class DiveCableSystem : NetworkBehaviour
{
    // ─── Inspector: Air Hose ────────────────────────────────────────────────
    [Header("Air Hose Constraint")]
    [SerializeField] private float initialMaxCableLength = 30f;
    [SerializeField] private float maxCableLength = 30f;
    [SerializeField] private float maxCableIncrement = 10f;

    [Header("Air Hose Appearance")]
    [SerializeField] private Material airCableMaterial;
    [SerializeField] private float    cableRadius = 0.04f;

    // ─── Inspector: Comms Rope ──────────────────────────────────────────────
    [Header("Comms Rope Constraint")]
    [SerializeField] private float commsMaxCableLength = 25f;

    [Header("Comms Rope Appearance")]
    [SerializeField] private Material commsRopeMaterial;
    [SerializeField] private float    commsRopeRadius = 0.02f;

    // ─── Inspector: Verlet Simulation ───────────────────────────────────────
    [Header("Verlet Simulation")]
    [SerializeField] private int   particleCount        = 25;
    [SerializeField] private int   constraintIterations = 4;
    [SerializeField] private float airRopeSlack          = 34f;   // slightly longer than maxCableLength (30) for natural droop
    [SerializeField] private float commsRopeSlack         = 29f;   // slightly longer than commsMaxCableLength (25)
    [SerializeField] private float airGravity            = -9.81f;
    [SerializeField] private float waterGravity          = -2f;
    [SerializeField] private float airDrag               = 0.99f;
    [SerializeField] private float waterDrag             = 0.92f;
    [SerializeField] private float catenarySag           = 3f;    // sag for non-owner catenary fallback

    [Header("Tube Mesh")]
    [SerializeField] private int tubeSides = 8;

    [Header("Tug")]
    [SerializeField] private float tugStrength = 2f;

    [Header("Attachment")]
    [Tooltip("Assign the helmet / top-of-head transform on the player prefab. If null a child anchor is created automatically.")]
    [SerializeField] private Transform _playerAttachPoint;

    // ─── NetworkVariables ─────────────────────────────────────────────────────
    private readonly NetworkVariable<bool> _cablesActive = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);

    // ─── Runtime state: shared ──────────────────────────────────────────────
    private CharacterController _cc;
    private AirPumpStation      _pumpStation;
    private WinchStation        _winchStation;
    private OceanWaves          _oceanWaves;
    private Vector3             _airAnchorPos;
    private Vector3             _commsAnchorPos;
    private Transform           _headAnchor;
    private readonly NetworkVariable<float> _currentCommsLength = new(0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);
    private bool                _offlineCablesActive;
    private bool                _initialized;

    // ─── Runtime state: ropes + tube meshes ────────────────────────────────
    private VerletRope         _airRope;
    private VerletRope         _commsRope;
    private TubeMeshGenerator  _airMesh;
    private TubeMeshGenerator  _commsMesh;
    private float              _tugSagBoost;      // transient sag increase on tug, decays over time
    private Vector3            _prevCommsAnchorPos;
    private bool               _prevCommsAnchorValid;

    // ─── Helpers ──────────────────────────────────────────────────────────────
    private bool IsNetworked  => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
    private bool CablesActive => IsNetworked ? _cablesActive.Value : _offlineCablesActive;

    /// <summary>Effective max distance the player can go from the ship (min of both cables).</summary>
    public float EffectiveMaxLength => Mathf.Min(maxCableLength, _currentCommsLength.Value);

    /// <summary>Current dynamic comms rope length (shortened by winch reel-in).</summary>
    public float CurrentCommsLength => _currentCommsLength.Value;

    public const float MinCommsRopeLength = 3f;

    /// <summary>Set the dynamic comms rope length (called by PlayerController when winch reels in/out).</summary>
    public void SetCommsRopeLength(float length)
    {
        _currentCommsLength.Value = Mathf.Clamp(length, MinCommsRopeLength, commsMaxCableLength);
    }

    private Transform PlayerAttachTransform
    {
        get
        {
            if (_playerAttachPoint != null) return _playerAttachPoint;

            if (_headAnchor == null)
            {
                var go = new GameObject("_HoseHeadAnchor");
                _headAnchor = go.transform;
                _headAnchor.SetParent(transform);
                _headAnchor.localPosition = Vector3.up * (_cc != null ? _cc.height : 1.8f);
            }
            return _headAnchor;
        }
    }

    // ─── Lifecycle ──────────────────────────────────────────────────────────

    private void Awake()
    {
        _cc = GetComponent<CharacterController>();
    }

    private void Start()
    {
        if (!IsNetworked) InitializeLocal();
        maxCableLength = initialMaxCableLength;
    }

    public override void OnNetworkSpawn()
    {
        InitializeLocal();

        if (!IsOwner)
        {
            _cablesActive.OnValueChanged += OnCablesActiveChanged;
            if (_cablesActive.Value)
                OnCablesActiveChanged(false, true);
        }
    }

    private void OnCablesActiveChanged(bool oldVal, bool newVal)
    {
        if (newVal) StartRope();
        else        StopRope();
    }

    // ─── Simulation + visual update ─────────────────────────────────────────

    private void LateUpdate()
    {
        if (!CablesActive) return;
        if (_airRope == null && _commsRope == null) return;

        // Keep anchor positions fresh (ship moves every frame)
        if (_pumpStation  != null) _airAnchorPos   = _pumpStation.HosePosition;
        if (_winchStation != null) _commsAnchorPos = _winchStation.RopePosition;

        // Decay tug sag boost
        _tugSagBoost = Mathf.MoveTowards(_tugSagBoost, 0f, Time.deltaTime * 6f);

        // --- Air hose (catenary) ---
        if (_airRope != null)
        {
            Vector3 airStart = _pumpStation != null ? _pumpStation.HoseTransform.position : _airAnchorPos;
            Vector3 airEnd   = PlayerAttachTransform.position;
            float sag = catenarySag * Mathf.Clamp01(Vector3.Distance(airStart, airEnd) / maxCableLength);
            _airRope.GenerateCatenary(airStart, airEnd, sag);
            _airMesh?.UpdateMesh(_airRope.Positions, _airRope.ParticleCount);
        }

        // --- Comms rope (catenary + tug boost) ---
        if (_commsRope != null)
        {
            Vector3 commsStart = _winchStation != null ? _winchStation.RopeTransform.position : _commsAnchorPos;
            Vector3 commsEnd   = PlayerAttachTransform.position;
            float sag = catenarySag * Mathf.Clamp01(Vector3.Distance(commsStart, commsEnd) / _currentCommsLength.Value);
            _commsRope.GenerateCatenary(commsStart, commsEnd, sag + _tugSagBoost);
            _commsMesh?.UpdateMesh(_commsRope.Positions, _commsRope.ParticleCount);
        }
    }

    // ─── Initialisation ─────────────────────────────────────────────────────

    private void InitializeLocal()
    {
        if (_initialized) return;
        _initialized = true;

        if (_cc == null) _cc = GetComponent<CharacterController>();

        if (_pumpStation == null)
            _pumpStation = FindAnyObjectByType<AirPumpStation>();
        if (_pumpStation != null)
            _airAnchorPos = _pumpStation.HosePosition;

        if (_winchStation == null)
            _winchStation = FindAnyObjectByType<WinchStation>();
        if (_winchStation != null)
            _commsAnchorPos = _winchStation.RopePosition;

        if (_oceanWaves == null)
            _oceanWaves = FindAnyObjectByType<OceanWaves>();
    }

    // ─── Rope creation / teardown ───────────────────────────────────────────

    private void StartRope()
    {
        InitializeLocal();

        // Air hose
        if (_pumpStation == null)
        {
            _pumpStation = FindAnyObjectByType<AirPumpStation>();
            if (_pumpStation == null) return;
        }
        _airAnchorPos = _pumpStation.HosePosition;

        Vector3 airStart = _pumpStation.HoseTransform.position;
        Vector3 attachPos = PlayerAttachTransform.position;

        _airRope = new VerletRope(particleCount, airRopeSlack, constraintIterations);
        _airRope.Initialize(airStart, attachPos);
        _airMesh = new TubeMeshGenerator(particleCount, tubeSides, cableRadius, airCableMaterial, "AirHoseMesh");

        // Comms rope
        if (_winchStation == null)
            _winchStation = FindAnyObjectByType<WinchStation>();
        if (_winchStation != null)
        {
            _commsAnchorPos       = _winchStation.RopePosition;
            _prevCommsAnchorPos   = _commsAnchorPos;
            _prevCommsAnchorValid = true;
            Vector3 commsStart = _winchStation.RopeTransform.position;

            _commsRope = new VerletRope(particleCount, commsRopeSlack, constraintIterations);
            _commsRope.Initialize(commsStart, attachPos);
            _commsMesh = new TubeMeshGenerator(particleCount, tubeSides, commsRopeRadius, commsRopeMaterial, "CommsRopeMesh");
        }
    }

    private void StopRope()
    {
        _prevCommsAnchorValid = false;
        _airRope = null;
        _commsRope = null;

        _airMesh?.Dispose();
        _airMesh = null;

        _commsMesh?.Dispose();
        _commsMesh = null;
    }

    // ─── Public API ────────────────────────────────────────────────────────

    /// <summary>
    /// Tether tension (0–1) based on the tighter of the two cables.
    /// Used by UI for visual feedback.
    /// </summary>
    public float TetherTension
    {
        get
        {
            if (!CablesActive) return 0f;

            float airTension = _pumpStation != null
                ? Mathf.Clamp01(Vector3.Distance(transform.position, _airAnchorPos) / maxCableLength)
                : 0f;
            float commsTension = _winchStation != null
                ? Mathf.Clamp01(Vector3.Distance(transform.position, _commsAnchorPos) / _currentCommsLength.Value)
                : 0f;

            return Mathf.Max(airTension, commsTension);
        }
    }

    public void ActivateCables()
    {
        if (IsNetworked && !IsOwner) return;

        _currentCommsLength.Value = commsMaxCableLength;  // full slack on equip

        if (IsNetworked) _cablesActive.Value = true;
        else             _offlineCablesActive = true;

        StartRope();
    }

    public void ClearAnchor()
    {
        if (IsNetworked && !IsOwner) return;

        if (IsNetworked) _cablesActive.Value = false;
        else             _offlineCablesActive = false;

        StopRope();
    }

    public void ClampToTetherLength(bool applyShipDrag = false)
    {
        if (IsNetworked && !IsOwner) return;
        if (!CablesActive || _cc == null) return;

        // Keep anchor positions fresh (ship moves every frame)
        if (_pumpStation  != null) _airAnchorPos   = _pumpStation.HosePosition;
        if (_winchStation != null) _commsAnchorPos = _winchStation.RopePosition;

        // Drag the diver along with the ship by applying the anchor's per-frame delta.
        // Only when actually underwater — not when standing on deck wearing the suit.
        if (applyShipDrag && _prevCommsAnchorValid && _winchStation != null)
        {
            Vector3 anchorDelta = _commsAnchorPos - _prevCommsAnchorPos;
            if (anchorDelta.sqrMagnitude > 0.0001f)
                _cc.Move(anchorDelta);
        }
        _prevCommsAnchorPos   = _commsAnchorPos;
        _prevCommsAnchorValid = _winchStation != null;

        // Use the shorter of the two cables as the effective max
        float effectiveMax = EffectiveMaxLength;

        // Clamp to air hose anchor (pump station)
        ClampToAnchor(_airAnchorPos, effectiveMax);

        // Also clamp to comms rope anchor (winch station) if it exists
        if (_winchStation != null)
            ClampToAnchor(_commsAnchorPos, _currentCommsLength.Value);
    }

    /// <summary>
    /// Pull the player back toward the anchor using CC.Move so the CharacterController
    /// handles collision naturally (prevents teleporting inside the ship hull).
    /// </summary>
    private void ClampToAnchor(Vector3 anchorPos, float maxDist)
    {
        Vector3 toPlayer = transform.position - anchorPos;
        float dist = toPlayer.magnitude;
        if (dist <= maxDist) return;

        Vector3 clampedPos = anchorPos + toPlayer.normalized * maxDist;
        Vector3 correction = clampedPos - transform.position;
        _cc.Move(correction);
    }

    /// <summary>
    /// Visual rope tug: briefly increases the catenary sag on the comms rope,
    /// creating a visible downward pulse that decays back to normal.
    /// </summary>
    public void ApplyTugImpulse(bool fromStation)
    {
        _tugSagBoost = tugStrength;
    }

    public void Upgrade()
    {
        maxCableLength += maxCableIncrement;
    }

    public override void OnDestroy()
    {
        StopRope();
        if (_headAnchor != null) Destroy(_headAnchor.gameObject);
    }
}
