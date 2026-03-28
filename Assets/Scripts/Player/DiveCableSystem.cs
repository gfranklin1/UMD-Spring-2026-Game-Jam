using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Manages the two visual cables (air hose + communication rope) attached to the diver,
/// and enforces a max-distance constraint from the anchor (DivingSuitRack).
///
/// Constraint runs on the owner's client after each _cc.Move(); all clients render
/// the cables in LateUpdate using the synced anchor position and the player's
/// already-synced NetworkTransform.
/// </summary>
public class DiveCableSystem : NetworkBehaviour
{
    [Header("Cable Constraint")]
    [SerializeField] private float maxCableLength = 30f;

    [Header("Visual")]
    [SerializeField] private int   segmentCount  = 10;
    [SerializeField] private float maxSag        = 3f;
    [SerializeField] private float cableSpacing  = 0.15f;  // lateral gap between the two cables

    [Header("Air Hose")]
    [SerializeField] private Material airCableMaterial;
    [SerializeField] private float    airCableWidth = 0.08f;

    [Header("Comm Rope")]
    [SerializeField] private Material commRopeMaterial;
    [SerializeField] private float    commRopeWidth = 0.04f;

    // Synced so every client knows the anchor world position and whether to draw cables
    private NetworkVariable<Vector3> _anchorPosition = new NetworkVariable<Vector3>(
        Vector3.zero,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);

    private NetworkVariable<bool> _cablesActive = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);

    private LineRenderer _airLR;
    private LineRenderer _commLR;
    private CharacterController _cc;
    private readonly Vector3[] _points = new Vector3[0]; // resized in Init

    // ── Lifecycle ──────────────────────────────────────────────────────────────

    public override void OnNetworkSpawn()
    {
        _cc = GetComponent<CharacterController>();
        InitializeLineRenderers();
    }

    private void LateUpdate()
    {
        if (_airLR == null) return;
        bool show = _cablesActive.Value;
        _airLR.enabled  = show;
        _commLR.enabled = show;
        if (show) UpdateCableVisuals();
    }

    // ── Public API (called by PlayerController) ─────────────────────────────

    /// <summary>Call when the suit is equipped. Sets the anchor and shows cables.</summary>
    public void SetAnchor(Vector3 worldPos)
    {
        if (!IsOwner) return;
        _anchorPosition.Value = worldPos;
        _cablesActive.Value   = true;
    }

    /// <summary>Call when the suit is removed. Hides the cables.</summary>
    public void ClearAnchor()
    {
        if (!IsOwner) return;
        _cablesActive.Value = false;
    }

    /// <summary>
    /// Enforce the tether length constraint. Call this on the owner after every _cc.Move().
    /// Clamps the player position to a sphere of radius maxCableLength around the anchor.
    /// </summary>
    public void ClampToTetherLength()
    {
        if (!IsOwner || !_cablesActive.Value) return;

        Vector3 toPlayer = transform.position - _anchorPosition.Value;
        float dist = toPlayer.magnitude;
        if (dist > maxCableLength)
        {
            // Teleport back to the cable boundary — requires disabling CharacterController first
            _cc.enabled = false;
            transform.position = _anchorPosition.Value + toPlayer.normalized * maxCableLength;
            _cc.enabled = true;
        }
    }

    // ── LineRenderer setup ─────────────────────────────────────────────────────

    private void InitializeLineRenderers()
    {
        _airLR  = CreateLineRenderer("AirHose",  airCableMaterial,  airCableWidth);
        _commLR = CreateLineRenderer("CommRope", commRopeMaterial,  commRopeWidth);

        // Hide until suit is equipped
        _airLR.enabled  = false;
        _commLR.enabled = false;
    }

    private LineRenderer CreateLineRenderer(string goName, Material mat, float width)
    {
        var go = new GameObject(goName);
        go.transform.SetParent(transform, false);

        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace   = true;
        lr.positionCount   = segmentCount;
        lr.startWidth      = width;
        lr.endWidth        = width;
        lr.material        = mat != null ? mat : new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows  = false;
        return lr;
    }

    // ── Visual update ──────────────────────────────────────────────────────────

    private void UpdateCableVisuals()
    {
        Vector3 anchor = _anchorPosition.Value;
        Vector3 player = transform.position;
        float dist     = Vector3.Distance(anchor, player);
        float slack    = Mathf.Clamp01(1f - dist / maxCableLength);  // 1 = fully slack, 0 = taut

        // Lateral offset vectors so the two cables sit side-by-side
        Vector3 right   = (dist > 0.01f)
            ? Vector3.Cross((player - anchor).normalized, Vector3.up).normalized
            : Vector3.right;
        Vector3 offsetA =  right * cableSpacing * 0.5f;
        Vector3 offsetC = -right * cableSpacing * 0.5f;

        for (int i = 0; i < segmentCount; i++)
        {
            float t    = i / (float)(segmentCount - 1);
            float sag  = maxSag * 4f * t * (1f - t) * slack;  // parabola, peaks at midpoint

            Vector3 base3 = Vector3.Lerp(anchor, player, t);
            base3.y -= sag;

            _airLR.SetPosition(i,  base3 + offsetA);
            _commLR.SetPosition(i, base3 + offsetC);
        }
    }
}
