using Unity.Netcode;
using UnityEngine;
using Soor.RopeGenerator;

/// <summary>
/// Manages the air hose and comms rope attached to the diver using Soor.RopeGenerator's
/// physics-based rope. Segments provide physics/collision; a LineRenderer draws a smooth
/// cable visual for each rope.
/// First and last segments are kinematic anchors at station and player.
/// Middle segments drape under gravity with CharacterJoint physics.
/// </summary>
public class DiveCableSystem : NetworkBehaviour
{
    // ─── Inspector: Air Hose ────────────────────────────────────────────────
    [Header("Air Hose Constraint")]
    [SerializeField] private float initialMaxCableLength = 30f;
    [SerializeField] private float maxCableLength = 30f;
    [SerializeField] private float maxCableIncrement = 10f;

    [Header("Air Hose Rope Generator")]
    [SerializeField] private GameObject segmentPrefab;
    [SerializeField] private float ropeLength       = 50f;  // must be > maxCableLength to allow droop slack
    [SerializeField] private float segmentsDistance  = 0.5f;
    [SerializeField] private float ropeThickness    = 0.1f;

    [Header("Air Hose Appearance")]
    [SerializeField] private Material airCableMaterial;
    [SerializeField] private float    cableRadius = 0.04f;

    // ─── Inspector: Comms Rope ──────────────────────────────────────────────
    [Header("Comms Rope Constraint")]
    [SerializeField] private float commsMaxCableLength = 25f;

    [Header("Comms Rope Generator")]
    [SerializeField] private float commsRopeLength      = 45f;
    [SerializeField] private float commsSegmentsDistance = 0.5f;

    [Header("Comms Rope Appearance")]
    [SerializeField] private Material commsRopeMaterial;
    [SerializeField] private float    commsRopeRadius = 0.02f;

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
    private Vector3             _airAnchorPos;
    private Vector3             _commsAnchorPos;
    private Transform           _headAnchor;
    private float               _currentCommsLength;  // dynamic rope length (reeled in/out by winch)
    private bool                _offlineCablesActive;
    private bool                _initialized;

    // ─── Runtime state: air hose rope ───────────────────────────────────────
    private GameObject    _ropeParentGO;
    private Transform[]   _segmentTransforms;
    private Rigidbody     _firstSegmentRb;
    private Rigidbody     _lastSegmentRb;
    private LineRenderer  _lineRenderer;

    // ─── Runtime state: comms rope ──────────────────────────────────────────
    private GameObject    _commsRopeParentGO;
    private Transform[]   _commsSegmentTransforms;
    private Rigidbody     _commsFirstSegmentRb;
    private Rigidbody     _commsLastSegmentRb;
    private LineRenderer  _commsLineRenderer;

    // Shared across all instances so late-joining players can ignore existing rope colliders
    private static readonly System.Collections.Generic.List<Collider> s_allRopeColliders = new();

    // ─── Helpers ──────────────────────────────────────────────────────────────
    private bool IsNetworked  => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
    private bool CablesActive => IsNetworked ? _cablesActive.Value : _offlineCablesActive;

    /// <summary>Effective max distance the player can go from the ship (min of both cables).</summary>
    public float EffectiveMaxLength => Mathf.Min(maxCableLength, _currentCommsLength);

    /// <summary>Current dynamic comms rope length (shortened by winch reel-in).</summary>
    public float CurrentCommsLength => _currentCommsLength;

    public const float MinCommsRopeLength = 3f;

    /// <summary>Set the dynamic comms rope length (called by PlayerController when winch reels in/out).</summary>
    public void SetCommsRopeLength(float length)
    {
        _currentCommsLength = Mathf.Clamp(length, MinCommsRopeLength, commsMaxCableLength);
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

        // When this player spawns, ignore all rope colliders already in the scene
        if (_cc != null)
            foreach (var col in s_allRopeColliders)
                if (col != null) Physics.IgnoreCollision(col, _cc);

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

    // ─── Physics update ────────────────────────────────────────────────────

    private void FixedUpdate()
    {
        if (!CablesActive) return;

        // Air hose anchors
        if (_firstSegmentRb != null && _pumpStation != null)
            _firstSegmentRb.MovePosition(_pumpStation.HoseTransform.position);
        if (_lastSegmentRb != null)
            _lastSegmentRb.MovePosition(PlayerAttachTransform.position);

        // Comms rope anchors
        if (_commsFirstSegmentRb != null && _winchStation != null)
            _commsFirstSegmentRb.MovePosition(_winchStation.RopeTransform.position);
        if (_commsLastSegmentRb != null)
            _commsLastSegmentRb.MovePosition(PlayerAttachTransform.position);
    }

    // ─── Visual update ─────────────────────────────────────────────────────

    private void LateUpdate()
    {
        if (!CablesActive) return;

        UpdateLineRenderer(_lineRenderer, _segmentTransforms,
            _pumpStation != null ? _pumpStation.HoseTransform.position : _airAnchorPos,
            PlayerAttachTransform.position,
            airCableMaterial, maxCableLength, _airAnchorPos);

        UpdateLineRenderer(_commsLineRenderer, _commsSegmentTransforms,
            _winchStation != null ? _winchStation.RopeTransform.position : _commsAnchorPos,
            PlayerAttachTransform.position,
            commsRopeMaterial, commsMaxCableLength, _commsAnchorPos);
    }

    private void UpdateLineRenderer(LineRenderer lr, Transform[] segments,
        Vector3 startPos, Vector3 endPos, Material mat, float maxLen, Vector3 anchorPos)
    {
        if (lr == null || segments == null) return;

        int count = segments.Length + 2;
        lr.positionCount = count;

        lr.SetPosition(0, startPos);
        for (int i = 0; i < segments.Length; i++)
            lr.SetPosition(i + 1, segments[i].position);
        lr.SetPosition(count - 1, endPos);

        // Tension colour feedback
        float tension = Mathf.Clamp01(Vector3.Distance(transform.position, anchorPos) / maxLen);
        Color baseColor = mat != null ? mat.color : new Color(0.226f, 0.226f, 0.226f);
        Color targetColor;

        if (tension > 0.75f)
        {
            float lerp  = Mathf.InverseLerp(0.75f, 1f, tension);
            float pulse = Mathf.Sin(Time.time * 8f) * 0.5f + 0.5f;
            targetColor = Color.Lerp(baseColor, Color.red, lerp * (0.6f + pulse * 0.4f));
        }
        else
        {
            targetColor = baseColor;
        }

        lr.startColor = targetColor;
        lr.endColor   = targetColor;
    }

    // ─── Initialisation ─────────────────────────────────────────────────────

    private void InitializeLocal()
    {
        if (_initialized) return;
        _initialized = true;

        if (_cc == null) _cc = GetComponent<CharacterController>();

        if (_pumpStation == null)
            _pumpStation = FindFirstObjectByType<AirPumpStation>();
        if (_pumpStation != null)
            _airAnchorPos = _pumpStation.HosePosition;

        if (_winchStation == null)
            _winchStation = FindFirstObjectByType<WinchStation>();
        if (_winchStation != null)
            _commsAnchorPos = _winchStation.RopePosition;
    }

    // ─── Rope creation / teardown ───────────────────────────────────────────

    private void StartRope()
    {
        InitializeLocal();

        // Air hose
        if (_pumpStation == null)
        {
            _pumpStation = FindFirstObjectByType<AirPumpStation>();
            if (_pumpStation == null) return;
        }
        _airAnchorPos = _pumpStation.HosePosition;

        if (_ropeParentGO != null) Destroy(_ropeParentGO);
        var airResult = BuildRopeObject("AirHoseRope",
            _pumpStation.HoseTransform.position, PlayerAttachTransform.position,
            ropeLength, segmentsDistance, ropeThickness,
            airCableMaterial, cableRadius);
        _ropeParentGO      = airResult.parent;
        _segmentTransforms = airResult.segments;
        _firstSegmentRb    = airResult.firstRb;
        _lastSegmentRb     = airResult.lastRb;
        _lineRenderer      = airResult.lr;

        // Comms rope
        if (_winchStation == null)
            _winchStation = FindFirstObjectByType<WinchStation>();
        if (_winchStation != null)
        {
            _commsAnchorPos = _winchStation.RopePosition;

            if (_commsRopeParentGO != null) Destroy(_commsRopeParentGO);
            var commsResult = BuildRopeObject("CommsRope",
                _winchStation.RopeTransform.position, PlayerAttachTransform.position,
                commsRopeLength, commsSegmentsDistance, ropeThickness,
                commsRopeMaterial, commsRopeRadius);
            _commsRopeParentGO      = commsResult.parent;
            _commsSegmentTransforms = commsResult.segments;
            _commsFirstSegmentRb    = commsResult.firstRb;
            _commsLastSegmentRb     = commsResult.lastRb;
            _commsLineRenderer      = commsResult.lr;

            // Ignore collisions between the two ropes
            if (_ropeParentGO != null && _commsRopeParentGO != null)
            {
                var airCols  = _ropeParentGO.GetComponentsInChildren<Collider>();
                var commsCols = _commsRopeParentGO.GetComponentsInChildren<Collider>();
                foreach (var a in airCols)
                    foreach (var c in commsCols)
                        Physics.IgnoreCollision(a, c);
            }
        }
    }

    private void StopRope()
    {
        DestroyRope(ref _ropeParentGO, ref _segmentTransforms,
            ref _firstSegmentRb, ref _lastSegmentRb, ref _lineRenderer);
        DestroyRope(ref _commsRopeParentGO, ref _commsSegmentTransforms,
            ref _commsFirstSegmentRb, ref _commsLastSegmentRb, ref _commsLineRenderer);
    }

    private void DestroyRope(ref GameObject parentGO, ref Transform[] segments,
        ref Rigidbody firstRb, ref Rigidbody lastRb, ref LineRenderer lr)
    {
        if (parentGO != null)
        {
            foreach (var col in parentGO.GetComponentsInChildren<Collider>())
                s_allRopeColliders.Remove(col);

            Destroy(parentGO);
            parentGO = null;
            firstRb  = null;
            lastRb   = null;
            segments = null;
            lr       = null;
        }
    }

    private (GameObject parent, Transform[] segments, Rigidbody firstRb, Rigidbody lastRb, LineRenderer lr)
        BuildRopeObject(string ropeName, Vector3 startPos, Vector3 endPos,
            float length, float segDist, float thickness, Material mat, float radius)
    {
        Vector3 midpoint = (startPos + endPos) * 0.5f;
        var parentGO = new GameObject(ropeName);
        parentGO.transform.position = midpoint;

        var ropeData = new RopeData
        {
            segmentPrefab             = segmentPrefab,
            ropeLength                = length,
            customizeSegmentsDistance  = true,
            segmentsDistance           = segDist,
            ropeThickness             = thickness,
            freezeFirstRopeSegment    = false,
            freezeLastRopeSegment     = false,
            setMaterialWhenSpawning   = false
        };

        var rope = new Soor.RopeGenerator.Rope(ropeData, parentGO);
        rope.SpawnRope();

        // Collect segment transforms
        int childCount = parentGO.transform.childCount;
        var segments = new Transform[childCount];
        for (int i = 0; i < childCount; i++)
            segments[i] = parentGO.transform.Find((i + 1).ToString());

        // Tune physics on every segment for stability
        foreach (var seg in segments)
        {
            foreach (var r in seg.GetComponentsInChildren<Renderer>())
                r.enabled = false;

            var rb = seg.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.mass                   = 0.1f;
                rb.linearDamping          = 5f;
                rb.angularDamping         = 5f;
                rb.interpolation          = RigidbodyInterpolation.Interpolate;
                rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
            }

            var joint = seg.GetComponent<CharacterJoint>();
            if (joint != null)
                joint.enableProjection = true;
        }

        // First segment: kinematic, anchored at start
        Rigidbody firstRb = null;
        if (childCount > 0 && segments[0] != null)
        {
            firstRb = segments[0].GetComponent<Rigidbody>();
            if (firstRb != null)
            {
                firstRb.isKinematic = true;
                firstRb.position = startPos;
            }
        }

        // Last segment: kinematic, anchored at player
        Rigidbody lastRb = null;
        if (childCount > 1 && segments[childCount - 1] != null)
        {
            lastRb = segments[childCount - 1].GetComponent<Rigidbody>();
            if (lastRb != null)
            {
                lastRb.isKinematic = true;
                lastRb.position = endPos;
            }
        }

        // Register colliders in static list so late-joining players can ignore them
        var ropeColliders = parentGO.GetComponentsInChildren<Collider>();
        s_allRopeColliders.AddRange(ropeColliders);

        // Ignore collisions with all currently spawned players
        foreach (var playerCC in FindObjectsByType<CharacterController>(FindObjectsSortMode.None))
            foreach (var col in ropeColliders)
                Physics.IgnoreCollision(col, playerCC);

        // LineRenderer for smooth cable visual
        var lr = parentGO.AddComponent<LineRenderer>();
        lr.useWorldSpace     = true;
        lr.positionCount     = childCount + 2;
        lr.startWidth        = radius * 2f;
        lr.endWidth          = radius * 2f;
        lr.numCornerVertices = 4;
        lr.numCapVertices    = 4;

        if (mat != null)
            lr.material = mat;
        else
            lr.material = new Material(Shader.Find("Universal Render Pipeline/Lit"));

        Color baseColor = mat != null ? mat.color : new Color(0.226f, 0.226f, 0.226f);
        lr.startColor = baseColor;
        lr.endColor   = baseColor;

        return (parentGO, segments, firstRb, lastRb, lr);
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
                ? Mathf.Clamp01(Vector3.Distance(transform.position, _commsAnchorPos) / _currentCommsLength)
                : 0f;

            return Mathf.Max(airTension, commsTension);
        }
    }

    public void ActivateCables()
    {
        if (IsNetworked && !IsOwner) return;

        _currentCommsLength = commsMaxCableLength;  // full slack on equip

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

    public void ClampToTetherLength()
    {
        if (IsNetworked && !IsOwner) return;
        if (!CablesActive || _cc == null) return;

        // Use the shorter of the two cables as the effective max
        float effectiveMax = EffectiveMaxLength;

        // Clamp to air hose anchor (pump station)
        Vector3 toPlayer = transform.position - _airAnchorPos;
        float   dist     = toPlayer.magnitude;
        if (dist > effectiveMax)
        {
            _cc.enabled        = false;
            transform.position = _airAnchorPos + toPlayer.normalized * effectiveMax;
            _cc.enabled        = true;
        }

        // Also clamp to comms rope anchor (winch station) if it exists
        if (_winchStation != null)
        {
            toPlayer = transform.position - _commsAnchorPos;
            dist     = toPlayer.magnitude;
            if (dist > _currentCommsLength)
            {
                _cc.enabled        = false;
                transform.position = _commsAnchorPos + toPlayer.normalized * _currentCommsLength;
                _cc.enabled        = true;
            }
        }
    }

    public void Upgrade()
    {
        maxCableLength += maxCableIncrement;
    }

    public override void OnDestroy()
    {
        if (_ropeParentGO      != null) Destroy(_ropeParentGO);
        if (_commsRopeParentGO != null) Destroy(_commsRopeParentGO);
        if (_headAnchor        != null) Destroy(_headAnchor.gameObject);
    }
}
