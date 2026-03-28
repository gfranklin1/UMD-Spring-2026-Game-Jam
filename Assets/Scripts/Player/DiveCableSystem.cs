using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Manages the visual cables attached to the diver using Verlet rope simulation
/// and procedural tube meshes. Air hose anchors to the AirPumpStation in the scene;
/// comm rope is hidden until commRopeAnchorTransform is assigned (future rope-pull station).
/// </summary>
public class DiveCableSystem : NetworkBehaviour
{
    // ─── Inspector ────────────────────────────────────────────────────────────
    [Header("Cable Constraint")]
    [SerializeField] private float maxCableLength = 30f;

    [Header("Rope Physics")]
    [SerializeField] private int       ropeNodeCount        = 22;
    [SerializeField] private int       constraintIterations = 4;
    [SerializeField] private float     ropeGravity          = 9.8f;
    [SerializeField] private float     nodeCollisionRadius  = 0.10f;
    [SerializeField] private LayerMask collisionMask        = ~0;

    [Header("Tube Appearance")]
    [SerializeField] private int   tubeSides      = 6;
    [SerializeField] private float airTubeRadius  = 0.04f;
    [SerializeField] private float commTubeRadius = 0.02f;
    [SerializeField] private float cableSpacing   = 0.15f;

    [Header("Air Hose")]
    [SerializeField] private Material airCableMaterial;

    [Header("Comm Rope")]
    [SerializeField] private Material commRopeMaterial;
    [Tooltip("Leave null — comm rope is hidden until the rope-pull station is built.")]
    [SerializeField] private Transform commRopeAnchorTransform;

    [Header("Attachment")]
    [Tooltip("Assign the helmet / top-of-head transform on the player prefab. If null, uses top of CharacterController capsule.")]
    [SerializeField] private Transform _playerAttachPoint;

    // ─── NetworkVariables ────────────────────────────────────────────────────
    private readonly NetworkVariable<bool> _cablesActive = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);

    // ─── Runtime state ───────────────────────────────────────────────────────
    private VerletRope _airRope;
    private VerletRope _commRope;
    private bool _airRopeReady;
    private bool _commRopeReady;

    private GameObject   _airTubeGO;
    private GameObject   _commTubeGO;
    private Mesh         _airMesh;
    private Mesh         _commMesh;
    private MeshRenderer _airTubeMR;

    private AirPumpStation _pumpStation;
    private Vector3        _airAnchorPos;

    private CharacterController _cc;

    private bool _offlineCablesActive;
    private bool _initialized;

    // ─── Helpers ──────────────────────────────────────────────────────────────
    private bool IsNetworked => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
    private bool CablesActive => IsNetworked ? _cablesActive.Value : _offlineCablesActive;

    /// <summary>Where the cable attaches to the diver (top of helmet/head).</summary>
    private Vector3 PlayerAttach => _playerAttachPoint != null
        ? _playerAttachPoint.position
        : transform.position + Vector3.up * (_cc != null ? _cc.height : 1.8f);

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

        // Non-owners subscribe to cable state so they can run their own local
        // Verlet simulation and see the rope visually.
        if (!IsOwner)
            _cablesActive.OnValueChanged += OnCablesActiveChanged;
    }

    private void OnCablesActiveChanged(bool oldVal, bool newVal)
    {
        if (newVal)
        {
            // Non-owner: start rope simulation from current synced positions
            if (_pumpStation == null)
            {
                _pumpStation = FindObjectOfType<AirPumpStation>();
                if (_pumpStation != null) _airAnchorPos = _pumpStation.transform.position;
            }
            if (_pumpStation != null)
            {
                _airRope.Activate(_airAnchorPos, PlayerAttach);
                _airRopeReady = true;
            }
            if (commRopeAnchorTransform != null)
            {
                _commRope.Activate(commRopeAnchorTransform.position, PlayerAttach);
                _commRopeReady = true;
            }
        }
        else
        {
            _airRopeReady  = false;
            _commRopeReady = false;
        }
    }

    private void InitializeLocal()
    {
        if (_initialized) return;
        _initialized = true;

        if (_cc == null) _cc = GetComponent<CharacterController>();

        if (_pumpStation == null)
            _pumpStation = FindObjectOfType<AirPumpStation>();
        if (_pumpStation != null)
            _airAnchorPos = _pumpStation.HosePosition;

        float restLen = maxCableLength / (ropeNodeCount - 1);
        _airRope  = new VerletRope(ropeNodeCount, restLen, ropeGravity, constraintIterations);
        _commRope = new VerletRope(ropeNodeCount, restLen, ropeGravity, constraintIterations);

        CreateTubeMeshObjects();
        _airTubeGO?.SetActive(false);
        _commTubeGO?.SetActive(false);
    }

    private void FixedUpdate()
    {
        if (!CablesActive) return;
        float   dt           = Time.fixedDeltaTime;
        Vector3 playerAttach = PlayerAttach;

        if (_airRopeReady)
            _airRope.Step(dt, _airAnchorPos, playerAttach, collisionMask, nodeCollisionRadius);

        if (_commRopeReady && commRopeAnchorTransform != null)
            _commRope.Step(dt, commRopeAnchorTransform.position, playerAttach, collisionMask, nodeCollisionRadius);
    }

    private void LateUpdate()
    {
        bool show = CablesActive;

        _airTubeGO?.SetActive(show && _airRopeReady);
        _commTubeGO?.SetActive(show && _commRopeReady && commRopeAnchorTransform != null);

        if (!show) return;

        Vector3 dir   = transform.position - _airAnchorPos;
        Vector3 right = dir.sqrMagnitude > 0.0001f
            ? Vector3.Cross(dir.normalized, Vector3.up).normalized
            : Vector3.right;

        if (_airRopeReady && _airTubeGO != null && _airTubeGO.activeSelf)
            TubeMeshBuilder.RebuildMesh(_airMesh, _airRope.Positions, tubeSides, airTubeRadius,
                right * cableSpacing * 0.5f);

        if (_commRopeReady && commRopeAnchorTransform != null && _commTubeGO != null && _commTubeGO.activeSelf)
            TubeMeshBuilder.RebuildMesh(_commMesh, _commRope.Positions, tubeSides, commTubeRadius,
                -right * cableSpacing * 0.5f);

        // Tether tension color feedback — pulse red when >75% extended
        if (_airTubeMR != null && _airRopeReady)
        {
            float tension   = TetherTension;
            Color baseColor = airCableMaterial != null ? airCableMaterial.color : new Color(0.91f, 0.63f, 0.19f);
            if (tension > 0.75f)
            {
                float lerp  = Mathf.InverseLerp(0.75f, 1f, tension);
                float pulse = Mathf.Sin(Time.time * 8f) * 0.5f + 0.5f;
                _airTubeMR.material.color = Color.Lerp(baseColor, Color.red, lerp * (0.6f + pulse * 0.4f));
            }
            else
            {
                _airTubeMR.material.color = baseColor;
            }
        }
    }

    // ─── Public API ──────────────────────────────────────────────────────────

    /// <summary>0–1 ratio of current tether distance to max cable length.</summary>
    public float TetherTension => (CablesActive && _pumpStation != null)
        ? Mathf.Clamp01(Vector3.Distance(transform.position, _airAnchorPos) / maxCableLength)
        : 0f;

    /// <summary>Call when the suit is equipped. Shows cables and starts rope simulation.</summary>
    public void ActivateCables()
    {
        if (IsNetworked && !IsOwner) return;

        if (IsNetworked) _cablesActive.Value = true;
        else             _offlineCablesActive = true;

        // Lazily find pump in case it wasn't in the scene during initialization
        if (_pumpStation == null)
        {
            _pumpStation = FindObjectOfType<AirPumpStation>();
            if (_pumpStation != null) _airAnchorPos = _pumpStation.HosePosition;
        }

        if (_pumpStation != null)
        {
            _airRope.Activate(_airAnchorPos, PlayerAttach);
            _airRopeReady = true;
        }

        if (commRopeAnchorTransform != null)
        {
            _commRope.Activate(commRopeAnchorTransform.position, PlayerAttach);
            _commRopeReady = true;
        }
    }

    /// <summary>Call when the suit is removed. Hides the cables.</summary>
    public void ClearAnchor()
    {
        if (IsNetworked && !IsOwner) return;

        if (IsNetworked) _cablesActive.Value = false;
        else             _offlineCablesActive = false;

        _airRopeReady  = false;
        _commRopeReady = false;
    }

    /// <summary>
    /// Enforce the tether length constraint. Call this on the owner after every _cc.Move().
    /// </summary>
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

    // ─── Setup helpers ───────────────────────────────────────────────────────

    private void CreateTubeMeshObjects()
    {
        _airMesh  = new Mesh { name = "AirTubeMesh" };
        _commMesh = new Mesh { name = "CommTubeMesh" };
        _airTubeGO  = CreateTubeGO("AirTube",  _airMesh,  airCableMaterial);
        _commTubeGO = CreateTubeGO("CommTube", _commMesh, commRopeMaterial);
        // Cache instanced material so we can tint it for tether tension feedback
        _airTubeMR = _airTubeGO.GetComponent<MeshRenderer>();
        _ = _airTubeMR.material; // triggers auto-instancing
    }

    public override void OnDestroy()
    {
        if (_airTubeGO  != null) Destroy(_airTubeGO);
        if (_commTubeGO != null) Destroy(_commTubeGO);
    }

    private GameObject CreateTubeGO(string goName, Mesh mesh, Material mat)
    {
        var go = new GameObject(goName);
        // No parent — mesh vertices are world-space, so this GO must live at scene root
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.material          = mat != null ? mat : new Material(Shader.Find("Universal Render Pipeline/Lit"));
        mr.shadowCastingMode = ShadowCastingMode.On;
        mr.receiveShadows    = true;
        return go;
    }

    // ─── Verlet rope simulation ───────────────────────────────────────────────

    private class VerletRope
    {
        private readonly Vector3[]  _pos;
        private readonly Vector3[]  _prev;
        private readonly Collider[] _collHits = new Collider[8];
        private readonly int   _n;
        private readonly float _restLen;
        private readonly float _gravity;
        private readonly int   _iters;

        public Vector3[] Positions => _pos;

        public VerletRope(int nodeCount, float restLen, float gravity, int iters)
        {
            _n       = nodeCount;
            _restLen = restLen;
            _gravity = gravity;
            _iters   = iters;
            _pos     = new Vector3[_n];
            _prev    = new Vector3[_n];
        }

        /// <summary>Distribute nodes in a straight line; zero velocity.</summary>
        public void Activate(Vector3 anchor, Vector3 player)
        {
            for (int i = 0; i < _n; i++)
            {
                float t = i / (float)(_n - 1);
                _pos[i] = Vector3.Lerp(anchor, player, t);
            }
            System.Array.Copy(_pos, _prev, _n);
        }

        public void Step(float dt, Vector3 anchor, Vector3 player, LayerMask mask, float collRadius)
        {
            // Phase 1: Verlet integrate interior nodes
            for (int i = 1; i < _n - 1; i++)
            {
                Vector3 vel = _pos[i] - _prev[i];
                _prev[i]   = _pos[i];
                _pos[i]   += vel + Vector3.down * (_gravity * dt * dt);
            }

            // Phase 2: Pin endpoints
            _pos[0]      = anchor;
            _pos[_n - 1] = player;

            // Phase 3: Distance constraints
            for (int iter = 0; iter < _iters; iter++)
            {
                for (int i = 0; i < _n - 1; i++)
                {
                    Vector3 delta = _pos[i + 1] - _pos[i];
                    float   dist  = delta.magnitude;
                    if (dist < 0.0001f) continue;

                    Vector3 correction = delta * (1f - _restLen / dist) * 0.5f;
                    if (i != 0)           _pos[i]     += correction;
                    if (i + 1 != _n - 1) _pos[i + 1] -= correction;
                }
                // Re-pin after each pass to avoid endpoint drift
                _pos[0]      = anchor;
                _pos[_n - 1] = player;
            }

            // Phase 4: Three-tier collision push-out
            for (int i = 1; i < _n - 1; i++)
            {
                // (a) Downward raycast — catches floors/decks including non-convex MeshColliders.
                //     Physics.Raycast works for all collider types; ClosestPoint does not.
                float checkAbove = collRadius + 0.25f;
                if (Physics.Raycast(_pos[i] + Vector3.up * checkAbove, Vector3.down,
                    out RaycastHit gHit, checkAbove + collRadius, mask, QueryTriggerInteraction.Ignore))
                {
                    float floor = gHit.point.y + collRadius;
                    if (_pos[i].y < floor)
                    {
                        _pos[i].y  = floor;
                        _prev[i].y = floor; // kill downward velocity
                    }
                }

                // (b) SphereCast along segment — catches walls and ceilings.
                //     Skip i == 1: _pos[0] is inside the pump box so its SphereCast
                //     origin is already overlapping a collider and silently returns false.
                if (i > 1)
                {
                    Vector3 seg = _pos[i] - _pos[i - 1];
                    float   len = seg.magnitude;
                    if (len > 0.0001f)
                    {
                        Vector3 dir = seg / len;
                        if (Physics.SphereCast(_pos[i - 1], collRadius, dir, out RaycastHit sHit,
                            len, mask, QueryTriggerInteraction.Ignore))
                        {
                            _pos[i]  = sHit.point + sHit.normal * collRadius;
                            _prev[i] = _pos[i]; // kill velocity to stop vibration
                            continue;
                        }
                    }
                }

                // (c) OverlapSphere fallback — handles convex colliders already overlapping the node.
                //     Non-convex MeshColliders are handled by (a)/(b) above; skip them here.
                int count = Physics.OverlapSphereNonAlloc(_pos[i], collRadius, _collHits, mask,
                    QueryTriggerInteraction.Ignore);
                for (int h = 0; h < count; h++)
                {
                    if (_collHits[h] is MeshCollider mc && !mc.convex) continue;
                    Vector3 closest = _collHits[h].ClosestPoint(_pos[i]);
                    Vector3 pushDir = _pos[i] - closest;
                    float   pushMag = pushDir.magnitude;
                    if (pushMag < 0.0001f)
                    {
                        pushDir = _pos[i] - _collHits[h].bounds.center;
                        if (pushDir.sqrMagnitude < 0.0001f) continue;
                        pushDir.Normalize();
                        pushMag = 0f;
                    }
                    else
                    {
                        pushDir /= pushMag;
                    }
                    _pos[i] += pushDir * (collRadius - pushMag);
                }
            }
        }
    }

    // ─── Procedural tube mesh builder ─────────────────────────────────────────

    private static class TubeMeshBuilder
    {
        /// <summary>
        /// Rebuilds a cylinder mesh along the given spine positions.
        /// All nodes are offset by lateralOffset so two ropes can sit side-by-side.
        /// </summary>
        public static void RebuildMesh(Mesh mesh, Vector3[] spine, int sides, float radius, Vector3 lateralOffset)
        {
            int n = spine.Length;
            int vertCount    = n * sides + 2;
            int triIndexCount = ((n - 1) * sides * 2 + sides * 2) * 3;

            Vector3[] verts = new Vector3[vertCount];
            int[]     tris  = new int[triIndexCount];
            int ti = 0;

            // Build rings
            for (int i = 0; i < n; i++)
            {
                Vector3 forward;
                if (i == 0)          forward = spine[1] - spine[0];
                else if (i == n - 1) forward = spine[n - 1] - spine[n - 2];
                else                 forward = spine[i + 1] - spine[i - 1];

                if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
                forward.Normalize();

                Vector3    up    = Mathf.Abs(Vector3.Dot(forward, Vector3.up)) < 0.99f ? Vector3.up : Vector3.forward;
                Quaternion frame = Quaternion.LookRotation(forward, up);

                for (int s = 0; s < sides; s++)
                {
                    float   angle  = s / (float)sides * Mathf.PI * 2f;
                    Vector3 offset = frame * new Vector3(Mathf.Cos(angle) * radius,
                                                         Mathf.Sin(angle) * radius, 0f);
                    verts[i * sides + s] = spine[i] + lateralOffset + offset;
                }
            }

            // Cap center vertices
            int capStart = n * sides;
            verts[capStart]     = spine[0]     + lateralOffset;
            verts[capStart + 1] = spine[n - 1] + lateralOffset;

            // Tube quad strips
            for (int i = 0; i < n - 1; i++)
            {
                for (int s = 0; s < sides; s++)
                {
                    int a = i       * sides + s;
                    int b = i       * sides + (s + 1) % sides;
                    int c = (i + 1) * sides + s;
                    int d = (i + 1) * sides + (s + 1) % sides;

                    tris[ti++] = a; tris[ti++] = b; tris[ti++] = c;
                    tris[ti++] = b; tris[ti++] = d; tris[ti++] = c;
                }
            }

            // Start cap
            for (int s = 0; s < sides; s++)
            {
                tris[ti++] = capStart;
                tris[ti++] = (s + 1) % sides;
                tris[ti++] = s;
            }

            // End cap
            int endRing = (n - 1) * sides;
            for (int s = 0; s < sides; s++)
            {
                tris[ti++] = capStart + 1;
                tris[ti++] = endRing + s;
                tris[ti++] = endRing + (s + 1) % sides;
            }

            mesh.Clear();
            mesh.vertices  = verts;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }
    }
}
