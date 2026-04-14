using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Anchor deploy/retract system. Press E to drop anchor (stops ship),
/// press E again to slowly reel it back up.
/// </summary>
[RequireComponent(typeof(Collider))]
public class AnchorSystem : NetworkBehaviour, IInteractable
{
    public enum AnchorState : byte { Stowed, Dropping, Deployed, Raising }

    [Header("References")]
    [SerializeField] private ShipMovement shipMovement;
    [SerializeField] private Transform anchorAttachPoint;
    [SerializeField] private GameObject anchorVisualPrefab;
    [SerializeField] private LineRenderer anchorRope;
    [Tooltip("Optional object to hide while the anchor is lowered or moving, and show again when fully stowed.")]
    [SerializeField] private GameObject hideWhenLowered;

    [Header("Speeds")]
    [SerializeField] private float deploySpeed  = 15f;
    [SerializeField] private float retractSpeed  = 3f;

    [Header("Rope")]
    [SerializeField] private int ropeMidPoints  = 3;
    [SerializeField] private float ropeSagAmount = 2f;

    // Networked state
    private NetworkVariable<AnchorState> _netState = new(
        AnchorState.Stowed,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private NetworkVariable<Vector3> _netAnchorPos = new(
        Vector3.zero,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    // Offline fallback state
    private AnchorState _offlineState = AnchorState.Stowed;
    private Vector3 _offlineAnchorPos;

    private GameObject _anchorVisual;

    private bool IsNetworked => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;

    private AnchorState CurrentState
    {
        get => IsNetworked ? _netState.Value : _offlineState;
        set { if (IsNetworked) _netState.Value = value; else _offlineState = value; }
    }

    private Vector3 CurrentAnchorPos
    {
        get => IsNetworked ? _netAnchorPos.Value : _offlineAnchorPos;
        set { if (IsNetworked) _netAnchorPos.Value = value; else _offlineAnchorPos = value; }
    }

    // ── IInteractable ────────────────────────────────────────────

    public string GetPromptText(PlayerController viewer)
    {
        return CurrentState switch
        {
            AnchorState.Stowed   => "[E] Drop Anchor",
            AnchorState.Deployed => "[E] Raise Anchor",
            AnchorState.Dropping => "Anchor dropping...",
            AnchorState.Raising  => "Anchor raising...",
            _ => ""
        };
    }

    public float HoldDurationFor(PlayerController viewer) => 0f;

    public void OnInteractStart(PlayerController player)
    {
        var s = CurrentState;
        if (s != AnchorState.Stowed && s != AnchorState.Deployed) return;

        if (IsNetworked) ToggleAnchorServerRpc();
        else             ToggleAnchor();
    }

    public void OnInteractHold(PlayerController player) { }
    public void OnInteractCancel(PlayerController player) { }
    public void Release(PlayerController player) { }

    // ── Lifecycle ────────────────────────────────────────────────

    private void Start()
    {
        if (anchorRope != null) anchorRope.enabled = false;
        UpdateLoweredVisibility(CurrentState);
    }

    public override void OnNetworkSpawn()
    {
        _netState.OnValueChanged += OnStateChanged;

        UpdateLoweredVisibility(_netState.Value);

        // Late-join: sync visual state
        if (_netState.Value != AnchorState.Stowed)
            SpawnAnchorVisual(_netAnchorPos.Value);
    }

    public override void OnNetworkDespawn()
    {
        _netState.OnValueChanged -= OnStateChanged;
    }

    private void OnStateChanged(AnchorState oldState, AnchorState newState)
    {
        UpdateLoweredVisibility(newState);

        if (newState == AnchorState.Stowed)
            DestroyAnchorVisual();
        else if (oldState == AnchorState.Stowed)
            SpawnAnchorVisual(anchorAttachPoint != null ? anchorAttachPoint.position : transform.position);
    }

    private void Update()
    {
        bool authority = IsNetworked ? IsServer : true;
        var state = CurrentState;

        if (!IsNetworked)
            UpdateLoweredVisibility(state);

        if (authority)
        {
            if (state == AnchorState.Dropping)
                UpdateDropping();
            else if (state == AnchorState.Raising)
                UpdateRaising();
        }

        // Update visual position and rope on all clients
        if (_anchorVisual != null)
        {
            _anchorVisual.transform.position = CurrentAnchorPos;
            UpdateRopeVisual();
        }
        // Offline: handle visual spawn/destroy since OnValueChanged won't fire
        else if (!IsNetworked && state != AnchorState.Stowed)
        {
            SpawnAnchorVisual(CurrentAnchorPos);
        }
    }

    // ── Drop / Raise logic (authority only) ──────────────────────

    private void UpdateDropping()
    {
        Vector3 pos = CurrentAnchorPos;
        pos.y -= deploySpeed * Time.deltaTime;

        float floorY = SeabedManager.Instance != null
            ? SeabedManager.Instance.GetFloorY(pos.x, pos.z)
            : -30f;

        if (pos.y <= floorY)
        {
            pos.y = floorY;
            CurrentAnchorPos = pos;
            CurrentState = AnchorState.Deployed;
            shipMovement?.SetAnchored(true);
        }
        else
        {
            CurrentAnchorPos = pos;
        }
    }

    private void UpdateRaising()
    {
        Vector3 target = anchorAttachPoint != null ? anchorAttachPoint.position : transform.position;
        Vector3 pos = Vector3.MoveTowards(CurrentAnchorPos, target, retractSpeed * Time.deltaTime);
        CurrentAnchorPos = pos;

        if (Vector3.Distance(pos, target) < 0.5f)
        {
            CurrentState = AnchorState.Stowed;
            shipMovement?.SetAnchored(false);
            if (!IsNetworked) DestroyAnchorVisual();
        }
    }

    // ── Networking ───────────────────────────────────────────────

    [ServerRpc(RequireOwnership = false)]
    private void ToggleAnchorServerRpc()
    {
        ToggleAnchor();
    }

    private void ToggleAnchor()
    {
        if (CurrentState == AnchorState.Stowed)
        {
            Vector3 startPos = anchorAttachPoint != null ? anchorAttachPoint.position : transform.position;
            CurrentAnchorPos = startPos;
            CurrentState = AnchorState.Dropping;
        }
        else if (CurrentState == AnchorState.Deployed)
        {
            CurrentState = AnchorState.Raising;
        }
    }

    // ── Visuals ──────────────────────────────────────────────────

    private void SpawnAnchorVisual(Vector3 position)
    {
        if (_anchorVisual != null) return;
        if (anchorVisualPrefab != null)
            _anchorVisual = Instantiate(anchorVisualPrefab, position, anchorVisualPrefab.transform.rotation);
        if (anchorRope != null) anchorRope.enabled = true;
    }

    private void DestroyAnchorVisual()
    {
        if (_anchorVisual != null)
        {
            Destroy(_anchorVisual);
            _anchorVisual = null;
        }
        if (anchorRope != null) anchorRope.enabled = false;
    }

    private void UpdateRopeVisual()
    {
        if (anchorRope == null || anchorAttachPoint == null) return;

        Vector3 top = anchorAttachPoint.position;
        Vector3 bottom = CurrentAnchorPos;
        int totalPoints = ropeMidPoints + 2;
        anchorRope.positionCount = totalPoints;

        for (int i = 0; i < totalPoints; i++)
        {
            float t = (float)i / (totalPoints - 1);
            Vector3 point = Vector3.Lerp(top, bottom, t);

            // Catenary sag on middle points
            if (i > 0 && i < totalPoints - 1)
                point.y -= Mathf.Sin(t * Mathf.PI) * ropeSagAmount;

            anchorRope.SetPosition(i, point);
        }
    }

    private void UpdateLoweredVisibility(AnchorState state)
    {
        if (hideWhenLowered == null) return;

        bool isStowed = state == AnchorState.Stowed;
        hideWhenLowered.SetActive(isStowed);
    }
}
