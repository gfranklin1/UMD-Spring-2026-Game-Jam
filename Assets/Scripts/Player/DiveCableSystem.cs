using Unity.Netcode;
using UnityEngine;
using Soor.RopeGenerator;

/// <summary>
/// Manages the air hose attached to the diver using Soor.RopeGenerator's physics-based rope.
/// Segments provide physics/collision; a LineRenderer draws a smooth cable visual.
/// First and last segments are kinematic anchors at pump and player.
/// Middle segments drape under gravity with CharacterJoint physics.
/// </summary>
public class DiveCableSystem : NetworkBehaviour
{
    // ─── Inspector ────────────────────────────────────────────────────────────
    [Header("Cable Constraint")]
    [SerializeField] private float maxCableLength = 30f;

    [Header("Rope Generator")]
    [SerializeField] private GameObject segmentPrefab;
    [SerializeField] private float ropeLength       = 50f;  // must be > maxCableLength to allow droop slack
    [SerializeField] private float segmentsDistance  = 0.5f;
    [SerializeField] private float ropeThickness    = 0.1f;

    [Header("Appearance")]
    [SerializeField] private Material airCableMaterial;
    [SerializeField] private float    cableRadius = 0.04f;

    [Header("Attachment")]
    [Tooltip("Assign the helmet / top-of-head transform on the player prefab. If null a child anchor is created automatically.")]
    [SerializeField] private Transform _playerAttachPoint;

    // ─── NetworkVariables ─────────────────────────────────────────────────────
    private readonly NetworkVariable<bool> _cablesActive = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);

    // ─── Runtime state ────────────────────────────────────────────────────────
    private CharacterController _cc;
    private AirPumpStation      _pumpStation;
    private Vector3             _airAnchorPos;

    private GameObject    _ropeParentGO;
    private Transform[]   _segmentTransforms;
    private Rigidbody     _firstSegmentRb;
    private Rigidbody     _lastSegmentRb;
    private LineRenderer  _lineRenderer;
    private Transform     _headAnchor;

    private bool _offlineCablesActive;
    private bool _initialized;

    // Shared across all instances so late-joining players can ignore existing rope colliders
    private static readonly System.Collections.Generic.List<Collider> s_allRopeColliders = new();

    // ─── Helpers ──────────────────────────────────────────────────────────────
    private bool IsNetworked  => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
    private bool CablesActive => IsNetworked ? _cablesActive.Value : _offlineCablesActive;

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

    // ─── Lifecycle ────────────────────────────────────────────────────────────

    private void Awake()
    {
        _cc = GetComponent<CharacterController>();
    }

    private void Start()
    {
        if (!IsNetworked) InitializeLocal();
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

    // ─── Visual update ──────────────────────────────────────────────────────

    private void FixedUpdate()
    {
        if (!CablesActive) return;

        // Anchor first segment at pump, last segment at player
        if (_firstSegmentRb != null)
            _firstSegmentRb.MovePosition(_pumpStation.HoseTransform.position);
        if (_lastSegmentRb != null)
            _lastSegmentRb.MovePosition(PlayerAttachTransform.position);
    }

    private void LateUpdate()
    {
        if (!CablesActive || _segmentTransforms == null || _lineRenderer == null) return;

        // Update the LineRenderer to follow segment positions
        int count = _segmentTransforms.Length + 2;
        _lineRenderer.positionCount = count;

        _lineRenderer.SetPosition(0, _pumpStation.HoseTransform.position);
        for (int i = 0; i < _segmentTransforms.Length; i++)
            _lineRenderer.SetPosition(i + 1, _segmentTransforms[i].position);
        _lineRenderer.SetPosition(count - 1, PlayerAttachTransform.position);

        // Tension colour feedback
        float tension   = TetherTension;
        Color baseColor = airCableMaterial != null ? airCableMaterial.color : new Color(0.226f, 0.226f, 0.226f);
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

        _lineRenderer.startColor = targetColor;
        _lineRenderer.endColor   = targetColor;
    }

    // ─── Initialisation ───────────────────────────────────────────────────────

    private void InitializeLocal()
    {
        if (_initialized) return;
        _initialized = true;

        if (_cc == null) _cc = GetComponent<CharacterController>();

        if (_pumpStation == null)
            _pumpStation = FindFirstObjectByType<AirPumpStation>();
        if (_pumpStation != null)
            _airAnchorPos = _pumpStation.HosePosition;
    }

    // ─── Rope creation / teardown ─────────────────────────────────────────────

    private void StartRope()
    {
        InitializeLocal();

        if (_pumpStation == null)
        {
            _pumpStation = FindFirstObjectByType<AirPumpStation>();
            if (_pumpStation == null) return;
        }
        _airAnchorPos = _pumpStation.HosePosition;

        if (_ropeParentGO != null) Destroy(_ropeParentGO);
        BuildRopeObject();
    }

    private void StopRope()
    {
        if (_ropeParentGO != null)
        {
            foreach (var col in _ropeParentGO.GetComponentsInChildren<Collider>())
                s_allRopeColliders.Remove(col);

            Destroy(_ropeParentGO);
            _ropeParentGO      = null;
            _firstSegmentRb    = null;
            _lastSegmentRb     = null;
            _segmentTransforms = null;
            _lineRenderer      = null;
        }
    }

    private void BuildRopeObject()
    {
        Vector3 startPos = _pumpStation.HoseTransform.position;
        Vector3 endPos   = PlayerAttachTransform.position;

        // Place the parent at the midpoint between pump and player so the vertical
        // spawn column is centered — segments will settle faster under gravity.
        Vector3 midpoint = (startPos + endPos) * 0.5f;
        _ropeParentGO = new GameObject("AirHoseRope");
        _ropeParentGO.transform.position = midpoint;

        // Do NOT freeze either end — we will make them kinematic manually
        var ropeData = new RopeData
        {
            segmentPrefab             = segmentPrefab,
            ropeLength                = ropeLength,
            customizeSegmentsDistance  = true,
            segmentsDistance           = segmentsDistance,
            ropeThickness             = ropeThickness,
            freezeFirstRopeSegment    = false,
            freezeLastRopeSegment     = false,
            setMaterialWhenSpawning   = false
        };

        var rope = new Soor.RopeGenerator.Rope(ropeData, _ropeParentGO);
        rope.SpawnRope();

        // Collect segment transforms
        int childCount = _ropeParentGO.transform.childCount;
        _segmentTransforms = new Transform[childCount];
        for (int i = 0; i < childCount; i++)
            _segmentTransforms[i] = _ropeParentGO.transform.Find((i + 1).ToString());

        // Tune physics on every segment for stability
        foreach (var seg in _segmentTransforms)
        {
            // Hide capsule visuals — LineRenderer handles the look
            foreach (var r in seg.GetComponentsInChildren<Renderer>())
                r.enabled = false;

            // Tune physics: high drag for stability, CCD to prevent tunnelling through ship geometry
            var rb = seg.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.mass                  = 0.1f;
                rb.linearDamping         = 5f;
                rb.angularDamping        = 5f;
                rb.interpolation         = RigidbodyInterpolation.Interpolate;
                rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
            }

            // Enable joint projection to prevent segment separation
            var joint = seg.GetComponent<CharacterJoint>();
            if (joint != null)
                joint.enableProjection = true;
        }

        // First segment: kinematic, anchored at pump
        if (childCount > 0 && _segmentTransforms[0] != null)
        {
            _firstSegmentRb = _segmentTransforms[0].GetComponent<Rigidbody>();
            if (_firstSegmentRb != null)
            {
                _firstSegmentRb.isKinematic = true;
                _firstSegmentRb.position = startPos;
            }
        }

        // Last segment: kinematic, anchored at player
        if (childCount > 1 && _segmentTransforms[childCount - 1] != null)
        {
            _lastSegmentRb = _segmentTransforms[childCount - 1].GetComponent<Rigidbody>();
            if (_lastSegmentRb != null)
            {
                _lastSegmentRb.isKinematic = true;
                _lastSegmentRb.position = endPos;
            }
        }

        // Register colliders in static list so late-joining players can ignore them
        var ropeColliders = _ropeParentGO.GetComponentsInChildren<Collider>();
        s_allRopeColliders.AddRange(ropeColliders);

        // Ignore collisions with all currently spawned players
        foreach (var playerCC in FindObjectsByType<CharacterController>(FindObjectsSortMode.None))
            foreach (var col in ropeColliders)
                Physics.IgnoreCollision(col, playerCC);

        // LineRenderer for smooth cable visual
        _lineRenderer = _ropeParentGO.AddComponent<LineRenderer>();
        _lineRenderer.useWorldSpace      = true;
        _lineRenderer.positionCount      = childCount + 2;
        _lineRenderer.startWidth         = cableRadius * 2f;
        _lineRenderer.endWidth           = cableRadius * 2f;
        _lineRenderer.numCornerVertices  = 4;
        _lineRenderer.numCapVertices     = 4;

        if (airCableMaterial != null)
            _lineRenderer.material = airCableMaterial;
        else
            _lineRenderer.material = new Material(Shader.Find("Universal Render Pipeline/Lit"));

        Color baseColor = airCableMaterial != null ? airCableMaterial.color : new Color(0.226f, 0.226f, 0.226f);
        _lineRenderer.startColor = baseColor;
        _lineRenderer.endColor   = baseColor;
    }

    // ─── Public API ──────────────────────────────────────────────────────────

    public float TetherTension => (CablesActive && _pumpStation != null)
        ? Mathf.Clamp01(Vector3.Distance(transform.position, _airAnchorPos) / maxCableLength)
        : 0f;

    public void ActivateCables()
    {
        if (IsNetworked && !IsOwner) return;

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

        Vector3 toPlayer = transform.position - _airAnchorPos;
        float   dist     = toPlayer.magnitude;
        if (dist > maxCableLength)
        {
            _cc.enabled        = false;
            transform.position = _airAnchorPos + toPlayer.normalized * maxCableLength;
            _cc.enabled        = true;
        }
    }

    public override void OnDestroy()
    {
        if (_ropeParentGO  != null) Destroy(_ropeParentGO);
        if (_headAnchor    != null) Destroy(_headAnchor.gameObject);
    }
}
