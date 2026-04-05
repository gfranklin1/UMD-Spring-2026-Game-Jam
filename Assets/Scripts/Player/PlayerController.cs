using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

[DefaultExecutionOrder(50)]
[RequireComponent(typeof(CharacterController))]
public class PlayerController : NetworkBehaviour
{
    private enum PlayerState { OnDeck, AtStation, WearingSuit, Underwater, Dead }

    [Header("Movement")]
    [SerializeField] private float walkSpeed = 6f;
    [SerializeField] private float sprintSpeed = 10f;
    [SerializeField] private float suitWalkSpeed = 2f;
    [SerializeField] private float swimSpeed = 2.5f;
    [SerializeField] private float swimVerticalSpeed = 2f;
    [SerializeField] private float gravity = -15f;
    [SerializeField] private float jumpForce = 6f;
    [SerializeField] private float surfaceFloatDepth = 1.2f;
    [SerializeField] private float buoyancySpring = 6f;
    [SerializeField] private float speedChangeRate = 15f;  // units/s — acceleration rate between walk and sprint
    [SerializeField] private Animator playerAnim;

    [Header("Diving Boots")]
    [SerializeField] private float bootSinkSpeed    = 8f;   // max downward speed (no buoyancy)
    [SerializeField] private float bootWalkSpeed    = 1.5f; // horizontal speed on ocean floor
    [SerializeField] private float bootHopForce     = 3f;   // small hop impulse on floor
    [SerializeField] private float bootKickHoldTime = 1f;   // seconds to hold Q to kick boots off

    [Header("Player Stats")]
    [SerializeField] private float maxHealth              = 100f;
    [SerializeField] private float maxBreathSeconds       = 30f;   // no-suit breath hold
    [SerializeField] private float maxSuitBuffer          = 60f;   // line buffer with suit
    [SerializeField] private float drownDamageRate        = 20f;   // HP/s when oxygen == 0 underwater
    [SerializeField] private float depthDrainMultiplier   = 0.05f; // +5% drain per metre depth (suited)
    [SerializeField] private float movementDrainMultiplier = 0.08f;// +8% drain per m/s movement (suited)

    [Header("References")]
    [SerializeField] private OceanWaves oceanWaves;
    [SerializeField] private Transform cameraRoot;
    [SerializeField] private Transform bodyRoot;
    private Renderer[] _bodyRenderers = System.Array.Empty<Renderer>();
    [SerializeField] private PlayerInput playerInput;
    [SerializeField] private LootRegistry _lootRegistry;

    private CharacterController _cc;
    private DiveCableSystem _cableSystem;
    private PlayerState _state = PlayerState.OnDeck;
    private PlayerState _preDiveState = PlayerState.OnDeck;
    private float _holdStartTime = -1f;
    private float _holdDuration  = 0f;
    private float _verticalVelocity;
    private float _currentWaveHeight;
    private InputAction _moveAction;
    private InputAction _jumpAction;
    private InputAction _crouchAction;
    private InputAction _interactAction;
    private InputAction _sprintAction;
    private InputAction _dropAction;
    private InputAction _removeBootsAction;
    private InputAction _scrollInventoryAction;
    private bool  _hasBoots      = false;
    private float _bootKickTimer = 0f;
    private IInteractable _nearestInteractable;
    private IInteractable _currentStation;
    private DivingSuitRack _suitRack;
    private PlayerInventory _inventory;
    private LootPickup _nearestLoot;
    private StorageChest _openChest;
    private bool  _hasDied = false;
    private int _spawnPointIndex = -1;
    private bool _quotaResetSubscribed = false;
    private const float InteractRange = 2.5f;

    // Moving platform tracking (ship riding) — simulated parenting via local-space offsets
    private Transform _platformTransform;
    private Vector3 _localPlatformPosition;     // player pos in ship-local space
    private float _lastPlatformYaw;             // ship yaw last frame (for yaw-delta only)
    private float _platformGraceTimer;
    private float _ladderClimbT;       // metres from bottom anchor along the ladder axis
    private ShipMovement _shipMovement;         // cached ref for remote-player position correction

    // Synced so all clients reconstruct the player's position from the current ship transform
    // instead of the NT-stale world position (fixes host seeing remote players lag behind ship).
    private NetworkVariable<Vector3> _netPlatformOffset = new NetworkVariable<Vector3>(
        Vector3.zero,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);
    private NetworkVariable<bool> _netOnShip = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);

    public StorageChest CurrentOpenChest => _openChest;
    public event System.Action<StorageChest> OnChestOpened;
    public event System.Action               OnChestClosed;
    private float _health;
    private float _oxygen;
    private float _currentSpeed;
    private float _pumpFlowRate;   // oxygen/s currently delivered by the pump (0 when not pumping)
    private float _lastSentPumpFlow;
    private float _winchPullSpeed;   // m/s upward pull from winch operator (0 when idle)
    private float _lastSentWinchPull;

    // Synced so the server can find the suited player when routing pump RPCs
    private NetworkVariable<bool> _networkWearingSuit = new NetworkVariable<bool>(false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);

    // Synced so all clients know who is dead (spectating)
    public NetworkVariable<bool> NetworkIsDead = new NetworkVariable<bool>(false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public NetworkVariable<FixedString64Bytes> NetworkPlayerName = new NetworkVariable<FixedString64Bytes>(
        new FixedString64Bytes("Player"),
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public bool      IsDead     => NetworkIsDead.Value;
    public Transform CameraRoot => cameraRoot;

    private void Awake()
    {
        _cc           = GetComponent<CharacterController>();
        _cableSystem  = GetComponent<DiveCableSystem>();
        _inventory    = GetComponent<PlayerInventory>();
        if (bodyRoot != null)
            _bodyRenderers = bodyRoot.GetComponentsInChildren<Renderer>(true);
    }

    private void Start()
    {
        if (oceanWaves == null)
            oceanWaves = FindFirstObjectByType<OceanWaves>();
        _shipMovement = FindFirstObjectByType<ShipMovement>();
    }

    public override void OnNetworkSpawn()
    {
        if (IsOwner)
        {
            foreach (var r in _bodyRenderers) r.enabled = false;

            if (playerInput != null)
            {
                playerInput.enabled = true;
                _moveAction   = playerInput.actions["Move"];
                _jumpAction   = playerInput.actions["Jump"];
                _crouchAction = playerInput.actions["Crouch"];
                _interactAction = playerInput.actions["Interact"];
                _sprintAction   = playerInput.actions["Sprint"];
                _dropAction        = playerInput.actions["Drop"];
                _removeBootsAction     = playerInput.actions["RemoveBoots"];
                _scrollInventoryAction = playerInput.actions["ScrollInventory"];
                _interactAction.started   += OnInteractStarted;
                _interactAction.performed += OnInteractHeld;
                _interactAction.canceled  += OnInteractCanceled;
            }

            _health = maxHealth;
            _oxygen = maxBreathSeconds;
            _currentSpeed = walkSpeed;
            enabled = true;

            string localName = NetworkLauncher.PlayerName;
            if (IsServer) NetworkPlayerName.Value = new FixedString64Bytes(localName);
            else          SetPlayerNameServerRpc(new FixedString64Bytes(localName));
            return;
        }

        // Non-owner: disable input and camera so this machine doesn't control the remote player
        if (playerInput != null) playerInput.enabled = false;

        if (cameraRoot != null)
        {
            var cam = cameraRoot.GetComponent<Camera>();
            if (cam != null) cam.enabled = false;

            var listener = cameraRoot.GetComponent<AudioListener>();
            if (listener != null) listener.enabled = false;

            var playerCam = cameraRoot.GetComponent<PlayerCamera>();
            if (playerCam != null) playerCam.enabled = false;
        }

        // Disable CharacterController so physics depenetration doesn't fight
        // NetworkTransform when it repositions the remote player each tick.
        if (_cc != null) _cc.enabled = false;

        // Track remote players' dead state to hide their bodies
        NetworkIsDead.OnValueChanged += OnNetworkIsDeadChanged;
        if (NetworkIsDead.Value) ApplyDeadVisuals();

        // Don't disable the component — LateUpdate must run for non-owners
        // to override their NT position with the ship-relative offset.
        // Update() already returns early for non-owners via the IsOwner check.
    }

    private void OnNetworkIsDeadChanged(bool _, bool isDead) { if (isDead) ApplyDeadVisuals(); else UndoDeadVisuals(); }

    /// <summary>
    /// Server → owning client: teleport to an assigned deck spawn point.
    /// Stores the index so respawns always use the spawn point's current world position
    /// (the ship moves, so a cached Vector3 would go stale).
    /// </summary>
    [ClientRpc]
    public void AssignSpawnPointClientRpc(int spawnIndex, ClientRpcParams _ = default)
    {
        if (!IsOwner) return;
        _spawnPointIndex = spawnIndex;
        TeleportToSpawnPoint();
    }

    private void TeleportToSpawnPoint()
    {
        if (_spawnPointIndex < 0) return;
        var mgr = PlayerSpawnManager.Instance;
        if (mgr == null) return;
        Vector3 pos = mgr.GetSpawnPosition(_spawnPointIndex);
        _verticalVelocity = 0f;
        _cc.enabled = false;
        transform.position = pos + Vector3.up * 0.5f;
        _cc.enabled = true;
    }
    private void ApplyDeadVisuals() { foreach (var r in _bodyRenderers) r.enabled = false; if (_cc) _cc.enabled = false; }
    private void UndoDeadVisuals()  { if (!IsOwner) foreach (var r in _bodyRenderers) r.enabled = true; if (_cc) _cc.enabled = true; }

    public override void OnNetworkDespawn()
    {
        if (IsOwner && _interactAction != null)
        {
            _interactAction.started   -= OnInteractStarted;
            _interactAction.performed -= OnInteractHeld;
            _interactAction.canceled  -= OnInteractCanceled;
        }
        if (_quotaResetSubscribed && QuotaManager.Instance != null)
        {
            QuotaManager.Instance.OnGameReset     -= OnQuotaReset;
            QuotaManager.Instance.OnCycleAdvanced -= OnCycleAdvancedRevive;
        }
        NetworkIsDead.OnValueChanged -= OnNetworkIsDeadChanged;
    }

    private void OnQuotaReset()
    {
        if (!IsOwner) return;
        _hasDied = false;
        _health  = maxHealth;
        _oxygen  = maxBreathSeconds;

        // Close chest UI if open
        if (_openChest != null) CloseChest();

        // Wipe inventory — full restart means fresh start for everyone
        _inventory?.Clear();

        // Teleport back to spawn — reads the spawn point's current world position
        TeleportToSpawnPoint();

        // Reset suit state unconditionally (RPC may arrive late or out of order)
        _holdStartTime = -1f;
        _hasBoots      = false;
        _bootKickTimer = 0f;
        SetPumpFlowRate(0f);
        _winchPullSpeed = 0f;
        _suitRack = null;
        _networkWearingSuit.Value = false;
        _cableSystem?.ClearAnchor();

        // Restore from spectator / any other state
        _state = PlayerState.OnDeck;
        GetComponent<SpectatorCamera>()?.Deactivate();
        GetComponent<SpectatorHUD>()?.Hide();
        GetComponent<PlayerHUD>()?.ShowForRespawn();
        if (cameraRoot != null) { var pc = cameraRoot.GetComponent<PlayerCamera>(); if (pc) pc.enabled = true; }

        bool net = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        if (net && !IsServer) ClearDeadStateServerRpc();
        else if (net) NetworkIsDead.Value = false;
    }

    /// <summary>
    /// Called on every client when the quota is met and a new cycle begins.
    /// Only dead owners are affected — alive players keep playing uninterrupted.
    /// </summary>
    private void OnCycleAdvancedRevive()
    {
        if (!IsOwner || !_hasDied) return;

        // Full fresh-spawn reset (same as OnQuotaReset, plus inventory wipe)
        _hasDied = false;
        _health  = maxHealth;
        _oxygen  = maxBreathSeconds;

        TeleportToSpawnPoint();

        _holdStartTime = -1f;
        _hasBoots      = false;
        _bootKickTimer = 0f;
        SetPumpFlowRate(0f);
        _suitRack = null;
        _networkWearingSuit.Value = false;
        _cableSystem?.ClearAnchor();

        _inventory?.Clear();

        _state = PlayerState.OnDeck;
        enabled = true;   // re-enable Update() — dead players have this disabled externally
        GetComponent<SpectatorCamera>()?.Deactivate();
        GetComponent<SpectatorHUD>()?.Hide();
        GetComponent<PlayerHUD>()?.ShowForRespawn();
        if (cameraRoot != null) { var pc = cameraRoot.GetComponent<PlayerCamera>(); if (pc) pc.enabled = true; }

        bool net = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        if (net && !IsServer) ClearDeadStateServerRpc();
        else if (net) NetworkIsDead.Value = false;
    }

    private void Update()
    {
        // Only the owning client controls this player
        bool networked = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        if (networked && !IsOwner) return;

        // Subscribe to quota events as soon as QuotaManager is available
        if (!_quotaResetSubscribed && QuotaManager.Instance != null)
        {
            _quotaResetSubscribed = true;
            QuotaManager.Instance.OnGameReset    += OnQuotaReset;
            QuotaManager.Instance.OnCycleAdvanced += OnCycleAdvancedRevive;
        }

        // Game over — freeze everything
        if (QuotaManager.Instance != null && QuotaManager.Instance.IsGameOver) return;

        // Dead — spectating, skip all game logic
        if (_state == PlayerState.Dead) return;

        // Platform tracking must always run so the player stays on the ship even while
        // the chest UI is open or other states block movement input.
        Physics.SyncTransforms();
        UpdatePlatformTracking();
        ApplyPlatformDelta();

        // Chest UI open — freeze all movement/interaction; only check for close keys
        if (_openChest != null)
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame)
                CloseChest();
            return;
        }

        ScanForInteractables();
        ScanForLoot();

        // Release from station if player goes underwater (e.g. ship sinks)
        if (_state == PlayerState.AtStation && oceanWaves != null)
        {
            float waveH = oceanWaves.GetWaveHeight(transform.position);
            if (transform.position.y < waveH)
            {
                ReleaseFromStation();
                _preDiveState = PlayerState.OnDeck;
                _state = PlayerState.Underwater;
                if (_verticalVelocity > 0f) _verticalVelocity = 0f;
            }
        }

        bool onFoot = _state == PlayerState.OnDeck || _state == PlayerState.WearingSuit;
        if (!onFoot && playerAnim != null)
        {
            playerAnim.SetBool("IsRunning", false);
            playerAnim.SetBool("IsJumping", false);
            playerAnim.SetBool("IsInAir", false);
            playerAnim.SetBool("IsFalling", false);
            playerAnim.SetBool("IsGrounded", false);
            playerAnim.SetBool("IsInWater", _state == PlayerState.Underwater);
        }

        switch (_state)
        {
            case PlayerState.OnDeck:
            case PlayerState.WearingSuit:
                UpdateWaterState();
                HandleDeckMovement();
                break;
            case PlayerState.AtStation:
                HandleAtStationState();
                break;
            case PlayerState.Underwater:
                UpdateWaterState();
                if (_hasBoots) HandleSuitedUnderwaterMovement();
                else           HandleUnderwaterMovement();
                break;
        }
        // Run after UpdateWaterState so _state reflects this frame
        UpdateOxygen();
        UpdateHealth();

        // Inventory: drop selected item (Q)
        if (_dropAction != null && _dropAction.WasPressedThisFrame() && _state != PlayerState.AtStation)
        {
            Vector3 dropPos = transform.position + transform.forward + Vector3.up * 0.5f;
            if (networked)
            {
                var item = _inventory?.Slots[_inventory.SelectedIndex];
                if (item != null) DropItemServerRpc(item.name, dropPos);
            }
            else
            {
                var item = _inventory?.RemoveSelected();
                if (item?.worldPrefab != null)
                    Instantiate(item.worldPrefab, dropPos, Quaternion.identity);
            }
        }

        // Inventory: scroll wheel cycles slots, number keys 1-5 select directly
        if (_scrollInventoryAction != null && _inventory != null)
        {
            float scroll = _scrollInventoryAction.ReadValue<float>();
            if (scroll > 0f) _inventory.SelectPrevious();
            else if (scroll < 0f) _inventory.SelectNext();
        }
        if (_inventory != null)
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null)
            {
                if (kb.digit1Key.wasPressedThisFrame) _inventory.SelectSlot(0);
                if (kb.digit2Key.wasPressedThisFrame) _inventory.SelectSlot(1);
                if (kb.digit3Key.wasPressedThisFrame) _inventory.SelectSlot(2);
                if (kb.digit4Key.wasPressedThisFrame) _inventory.SelectSlot(3);
                if (kb.digit5Key.wasPressedThisFrame) _inventory.SelectSlot(4);
            }
        }

        SyncLocalPlatformOffset();
    }

    /// <summary>
    /// For remote players: override their NT-synced world position with one derived from the
    /// current (locally up-to-date) ship transform.  This eliminates the NT interpolation lag
    /// that makes non-host players appear to trail behind the ship on the host's screen.
    /// Runs after Update (and after NGO's network update writes NT positions).
    /// </summary>
    private void LateUpdate()
    {
        if (IsOwner) return;
        bool networked = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        if (!networked || !_netOnShip.Value || _shipMovement == null) return;

        // Reconstruct world position using yaw-only rotation (matching how the offset
        // was computed).  This makes the result immune to pitch/roll and Y oscillation
        // differences between clients — the player rides the current ship height and yaw.
        Transform ship = _shipMovement.transform;
        Quaternion yaw = Quaternion.Euler(0f, ship.eulerAngles.y, 0f);
        transform.position = ship.position + yaw * _netPlatformOffset.Value;
    }

    private void UpdateWaterState()
    {
        if (oceanWaves == null) return;
        _currentWaveHeight = oceanWaves.GetWaveHeight(transform.position);
        bool inWater = transform.position.y < _currentWaveHeight;

        if (inWater && (_state == PlayerState.OnDeck || _state == PlayerState.WearingSuit))
        {
            _preDiveState = _state;
            _state = PlayerState.Underwater;
            if (_verticalVelocity > 0f) _verticalVelocity = 0f;
            // Boots are heavy — ensure immediate weighted sinking on entry
            if (_hasBoots && _verticalVelocity > -bootSinkSpeed * 0.3f)
                _verticalVelocity = -bootSinkSpeed * 0.3f;
            Debug.Log($"[Player] Entered water (was {_preDiveState})");
        }
        else if (!inWater && _state == PlayerState.Underwater)
        {
            _state = _preDiveState;
            // Preserve upward velocity so Space-swimming can launch the player onto a ship.
            // Only kill it if somehow negative (shouldn't happen but safe to clamp).
            if (_verticalVelocity < 0f) _verticalVelocity = 0f;
            Debug.Log($"[Player] Surfaced → {_state}");
        }
    }

    private void UpdatePlatformTracking()
    {
        if (_state == PlayerState.Underwater || _state == PlayerState.Dead)
        {
            _platformTransform = null;
            _platformGraceTimer = 0f;
            return;
        }

        // At a station the player is pinned on the ship — don't let a transient raycast
        // miss null out the platform and reset _lastPlatformYaw, which would zero yawDelta
        // and break ship-rotation tracking for non-host clients.
        // Ladder climbing is included: the raycast misses when the player is on the ship's
        // side, so we must keep the grace timer alive here. SyncLocalPlatformOffset (line 430)
        // updates _localPlatformPosition every frame regardless of this early return.
        if (_state == PlayerState.AtStation && _platformTransform != null)
        {
            _platformGraceTimer = 0.5f;
            return;
        }

        float rayDist = _cc.height + 3f;
        if (Physics.Raycast(transform.position, Vector3.down, out RaycastHit hit, rayDist))
        {
            var ship = hit.collider.GetComponentInParent<ShipMovement>();
            if (ship != null)
            {
                if (_platformTransform != ship.transform)
                {
                    // First acquisition: snapshot local offset
                    _platformTransform = ship.transform;
                    _localPlatformPosition = _platformTransform.InverseTransformPoint(transform.position);
                    _lastPlatformYaw = _platformTransform.eulerAngles.y;
                }
                _platformGraceTimer = 0.5f;
                return;
            }
        }

        if (_platformGraceTimer > 0f)
        {
            _platformGraceTimer -= Time.deltaTime;
            return;
        }
        _platformTransform = null;
    }

    /// <summary>
    /// Repositions the player to match the ship's current transform using stored local offset.
    /// Disables CharacterController during the teleport to bypass collision rejection.
    /// Only applies yaw delta (not pitch/roll) so mouse look isn't overridden.
    /// </summary>
    private void ApplyPlatformDelta()
    {
        if (_platformTransform == null) return;

        Vector3 targetPos = _platformTransform.TransformPoint(_localPlatformPosition);

        float currentPlatformYaw = _platformTransform.eulerAngles.y;
        float yawDelta = Mathf.DeltaAngle(_lastPlatformYaw, currentPlatformYaw);

        // Disable CC so collision doesn't reject the repositioning
        _cc.enabled = false;
        // TransformPoint already orbits the position to account for ship rotation —
        // only rotate the player's facing direction, don't reposition.
        transform.position = targetPos;
        if (Mathf.Abs(yawDelta) > 0.01f)
            transform.Rotate(0f, yawDelta, 0f, Space.World);
        _cc.enabled = true;

        _lastPlatformYaw = currentPlatformYaw;
    }

    /// <summary>
    /// Records the player's position in ship-local space AFTER all movement for this frame.
    /// Also writes to NetworkVariables so all clients can reconstruct world position from the
    /// current (authoritative) ship transform rather than the NT-stale world position.
    /// </summary>
    private void SyncLocalPlatformOffset()
    {
        bool onShip = _platformTransform != null;
        if (onShip)
            _localPlatformPosition = _platformTransform.InverseTransformPoint(transform.position);

        if (!IsOwner) return;
        bool networked = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        if (!networked) return;

        if (onShip)
        {
            _netOnShip.Value = true;
            // Compute the offset using yaw-only rotation so it is immune to the
            // pitch/roll and Y oscillation that differ between clients.  The full-
            // transform _localPlatformPosition is kept for local tracking only.
            Vector3 delta = transform.position - _platformTransform.position;
            Quaternion invYaw = Quaternion.Inverse(
                Quaternion.Euler(0f, _platformTransform.eulerAngles.y, 0f));
            _netPlatformOffset.Value = invYaw * delta;
        }
        else if (_netOnShip.Value)
        {
            _netOnShip.Value = false;
        }
    }

    private void HandleDeckMovement()
    {
        if (_moveAction == null) return;
        var moveInput = _moveAction.ReadValue<Vector2>();
        bool wasGrounded = _cc.isGrounded;
        bool jumpPressedThisFrame = _cc.isGrounded && _jumpAction != null && _jumpAction.WasPressedThisFrame();

        if (_cc.isGrounded && _verticalVelocity < 0f)
            _verticalVelocity = -2f;

        if (jumpPressedThisFrame)
            _verticalVelocity = jumpForce;

        _verticalVelocity += gravity * Time.deltaTime;

        bool sprinting = _state == PlayerState.OnDeck && _sprintAction != null && _sprintAction.IsPressed();
        float targetSpeed = _state == PlayerState.WearingSuit ? suitWalkSpeed
                          : sprinting                         ? sprintSpeed
                          :                                    walkSpeed;
        _currentSpeed = Mathf.MoveTowards(_currentSpeed, targetSpeed, speedChangeRate * Time.deltaTime);
        Vector3 move = transform.TransformDirection(new Vector3(moveInput.x, 0f, moveInput.y));
        _cc.Move((move * _currentSpeed + Vector3.up * _verticalVelocity) * Time.deltaTime);
        if (_state == PlayerState.WearingSuit) _cableSystem?.ClampToTetherLength();

        if (playerAnim != null)
        {
            bool moving = moveInput.sqrMagnitude > 0.01f;
            bool grounded = _cc.isGrounded;
            bool justLeftGround = wasGrounded && !grounded;
            bool rising = !grounded && (_verticalVelocity > 0.1f || jumpPressedThisFrame);
            bool falling = !grounded && !rising && (_verticalVelocity <= 0.1f || justLeftGround);

            playerAnim.SetBool("IsRunning", moving);
            playerAnim.SetBool("IsJumping", jumpPressedThisFrame);
            playerAnim.SetBool("IsInAir", !grounded && !falling);
            playerAnim.SetBool("IsFalling", falling);
            playerAnim.SetBool("IsGrounded", grounded);
            playerAnim.SetBool("IsInWater", _state == PlayerState.Underwater);
        }
    }

    private void HandleUnderwaterMovement()
    {
        if (_moveAction == null) return;
        var moveInput = _moveAction.ReadValue<Vector2>();

        Transform cam = cameraRoot != null ? cameraRoot : transform;
        Vector3 forward    = Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized;
        Vector3 right      = Vector3.ProjectOnPlane(cam.right,   Vector3.up).normalized;
        Vector3 horizontal = (forward * moveInput.y + right * moveInput.x) * swimSpeed;

        bool pushingUp   = _jumpAction   != null && _jumpAction.IsPressed();
        bool pushingDown = _crouchAction != null && _crouchAction.IsPressed();

        if (pushingUp)
        {
            _verticalVelocity = Mathf.Lerp(_verticalVelocity, swimVerticalSpeed * 2.5f, Time.deltaTime * 10f);
        }
        else if (pushingDown)
        {
            _verticalVelocity = Mathf.Lerp(_verticalVelocity, -swimVerticalSpeed * 1.5f, Time.deltaTime * 10f);
        }
        else
        {
            // Water drag: momentum decays to ~35% in 1 second — feels like water resistance, not a wall
            _verticalVelocity *= Mathf.Pow(0.35f, Time.deltaTime);
            // Buoyancy spring adds force (not a velocity target) so it works with existing momentum
            float targetY = _currentWaveHeight - surfaceFloatDepth;
            float error   = targetY - transform.position.y;
            _verticalVelocity += error * buoyancySpring * Time.deltaTime;
            // Allow higher downward speed so fall momentum can carry the player deep before buoyancy wins
            _verticalVelocity = Mathf.Clamp(_verticalVelocity, -swimVerticalSpeed * 6f, swimVerticalSpeed * 2f);
        }

        // Winch: positive = reel in (pull up + shorten rope), negative = pay out (extend rope)
        if (_winchPullSpeed > 0f)
        {
            float dampen = Mathf.Clamp01((_currentWaveHeight - transform.position.y) / 2f);
            _verticalVelocity = Mathf.Max(_verticalVelocity, _winchPullSpeed * dampen);
            _cableSystem?.SetCommsRopeLength(_cableSystem.CurrentCommsLength - _winchPullSpeed * Time.deltaTime);
        }
        else if (_winchPullSpeed < 0f)
        {
            _cableSystem?.SetCommsRopeLength(_cableSystem.CurrentCommsLength + (-_winchPullSpeed) * Time.deltaTime);
        }

        _cc.Move((horizontal + Vector3.up * _verticalVelocity) * Time.deltaTime);
        _cableSystem?.ClampToTetherLength();
    }

    private void HandleSuitedUnderwaterMovement()
    {
        var moveInput = _moveAction?.ReadValue<Vector2>() ?? Vector2.zero;
        Vector3 move  = transform.TransformDirection(new Vector3(moveInput.x, 0f, moveInput.y));

        // Floor walking: snap grounded velocity and allow a small hop
        if (_cc.isGrounded)
        {
            if (_verticalVelocity < 0f) _verticalVelocity = -2f;
            if (_jumpAction != null && _jumpAction.WasPressedThisFrame())
                _verticalVelocity = bootHopForce;
        }

        // Winch: positive = reel in (pull up + shorten rope), negative = pay out (extend rope)
        if (_winchPullSpeed > 0f)
        {
            // Winch overrides gravity: ramp toward pull speed
            float dampen = Mathf.Clamp01((_currentWaveHeight - transform.position.y) / 2f);
            _verticalVelocity = Mathf.Lerp(_verticalVelocity, _winchPullSpeed * dampen, Time.deltaTime * 6f);
            _cableSystem?.SetCommsRopeLength(_cableSystem.CurrentCommsLength - _winchPullSpeed * Time.deltaTime);
        }
        else
        {
            if (_winchPullSpeed < 0f)
                _cableSystem?.SetCommsRopeLength(_cableSystem.CurrentCommsLength + (-_winchPullSpeed) * Time.deltaTime);

            _verticalVelocity += gravity * Time.deltaTime;
            _verticalVelocity  = Mathf.Max(_verticalVelocity, -bootSinkSpeed);
        }

        _cc.Move((move * bootWalkSpeed + Vector3.up * _verticalVelocity) * Time.deltaTime);
        _cableSystem?.ClampToTetherLength();

        // Boot kick-off: hold G for bootKickHoldTime seconds
        if (_removeBootsAction != null && _removeBootsAction.IsPressed())
        {
            _bootKickTimer += Time.deltaTime;
            if (_bootKickTimer >= bootKickHoldTime)
                KickOffBoots();
        }
        else
        {
            _bootKickTimer = 0f;
        }
    }

    private void KickOffBoots()
    {
        _hasBoots      = false;
        _bootKickTimer = 0f;
        _verticalVelocity = swimVerticalSpeed;  // initial upward push when freed
        Debug.Log("[Player] Boots kicked off → free swimming");
    }

    private void ScanForInteractables()
    {
        if (_state == PlayerState.AtStation)
        {
            _nearestInteractable = null;
            return;
        }

        // SphereCast along camera look direction — interact with what you aim at.
        // Start slightly behind the camera so the sphere doesn't begin inside nearby colliders.
        const float CastRadius  = 0.5f;
        const float StartOffset = 0.6f;
        Vector3 origin = cameraRoot.position - cameraRoot.forward * StartOffset;
        Ray ray = new Ray(origin, cameraRoot.forward);
        IInteractable nearest = null;

        if (Physics.SphereCast(ray, CastRadius, out RaycastHit hit, InteractRange + StartOffset))
            nearest = hit.collider.GetComponentInParent<IInteractable>();

        if (nearest != _nearestInteractable)
        {
            _nearestInteractable = nearest;
            _holdStartTime = -1f;   // cancel progress when target changes
            if (nearest != null)
                Debug.Log($"[Player] In range: {nearest.GetPromptText(this)}");
            else
                Debug.Log("[Player] Left interact range");
        }
    }

    private void ScanForLoot()
    {
        // Works in ALL states except AtStation — can pick up loot underwater
        if (_state == PlayerState.AtStation) { _nearestLoot = null; return; }

        // Spherecast along camera look direction so the player picks up what they aim at
        const float CastRadius = 0.4f;
        const float CastRange  = 4f;
        Ray ray = new Ray(cameraRoot.position, cameraRoot.forward);

        if (Physics.SphereCast(ray, CastRadius, out RaycastHit hit, CastRange))
            _nearestLoot = hit.collider.GetComponentInParent<LootPickup>();
        else
            _nearestLoot = null;
    }

    private void HandleAtStationState()
    {
        // Helm station: A/D steers, wind drives speed — do NOT release on move input.
        // No gravity or _cc.Move here — platform tracking handles all positioning.
        if (_currentStation is HelmStation helm)
        {
            helm.HandleInput(_moveAction);
            return;
        }

        if (_currentStation is LadderClimbing ladder)
        {
            HandleLadderClimbing(ladder);
            return;
        }

        var moveInput = _moveAction?.ReadValue<Vector2>() ?? Vector2.zero;
        if (moveInput.magnitude > 0.1f) { ReleaseFromStation(); return; }

        if (_currentStation is AirPumpStation pump)
        {
            if (_jumpAction != null && _jumpAction.WasPressedThisFrame())
                pump.OnCrank();

            // Read flow rate from pump and push it to the diver
            float flow = pump.CurrentFlowRate;
            if (Mathf.Abs(flow - _lastSentPumpFlow) > 0.05f)
            {
                _lastSentPumpFlow = flow;
                bool networked = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
                if (networked)
                    SendPumpFlowServerRpc(flow);
                else
                    ApplyPumpFlowLocal(flow);
            }
        }

        if (_currentStation is WinchStation winch)
        {
            // Space = reel in (pull up + shorten rope), Ctrl = pay out (extend rope)
            bool cranking = _jumpAction != null && _jumpAction.IsPressed();
            bool lowering = _crouchAction != null && _crouchAction.IsPressed();
            winch.SetCranking(cranking);
            winch.SetLowering(lowering);

            // Signed speed: positive = reel in, negative = pay out
            float pull  = winch.CurrentPullSpeed;
            float lower = winch.CurrentLowerSpeed;
            float netSpeed = pull > 0f ? pull : -lower;

            if (Mathf.Abs(netSpeed - _lastSentWinchPull) > 0.05f)
            {
                _lastSentWinchPull = netSpeed;
                bool networked = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
                if (networked)
                    SendWinchPullServerRpc(netSpeed);
                else
                    ApplyWinchPullLocal(netSpeed);
            }
        }
    }

    /// <summary>
    /// Called by LadderClimbing.OnInteractStart. Snaps to the ladder face and locks.
    /// Safe to call from the Underwater state.
    /// </summary>
    public void GrabLadder(LadderClimbing ladder)
    {
        // Initialise climb parameter from current player height along the ladder
        _ladderClimbT = ladder.ProjectOntoLadder(transform.position);

        // Snap to ladder face at that height
        _cc.enabled = false;
        transform.position = ladder.GetPositionAtT(_ladderClimbT);
        _cc.enabled = true;

        // Ensure that when we later release, we go to OnDeck (not back to Underwater).
        _preDiveState = PlayerState.OnDeck;

        LockToStation(ladder);   // sets _state = AtStation, zeroes _verticalVelocity
    }

    private void HandleLadderClimbing(LadderClimbing ladder)
    {
        var  moveInput   = _moveAction?.ReadValue<Vector2>() ?? Vector2.zero;
        bool jumpPressed = _jumpAction != null && _jumpAction.WasPressedThisFrame();

        // Jump or horizontal input = release off the ladder
        if (jumpPressed || Mathf.Abs(moveInput.x) > 0.3f)
        {
            ReleaseFromStation();
            _verticalVelocity = jumpPressed ? jumpForce * 0.5f : 0f;
            return;
        }

        float ladderLen = ladder.LadderLength;

        // Advance the climb parameter by input — clamped to [0, ladderLen]
        _ladderClimbT = Mathf.Clamp(
            _ladderClimbT + moveInput.y * ladder.ClimbSpeed * Time.deltaTime,
            0f, ladderLen);

        // Always recompute world position from anchor geometry.
        // ApplyPlatformDelta has already run, but we override it here so that
        // buoyancy / pitch / roll never push the player off the ladder face.
        _cc.enabled = false;
        transform.position = ladder.GetPositionAtT(_ladderClimbT);
        _cc.enabled = true;

        // Auto-exit at top when pressing W and within 5 cm of the top
        if (moveInput.y > 0f && _ladderClimbT >= ladderLen - 0.05f)
        {
            _cc.enabled = false;
            transform.position = ladder.TopExitPosition;
            _cc.enabled = true;
            ReleaseFromStation();
            _verticalVelocity = 0f;
            return;
        }

        // Release at bottom when pressing S and at the base
        if (moveInput.y < 0f && _ladderClimbT <= 0.05f)
        {
            ReleaseFromStation();
            _verticalVelocity = 0f;
        }
    }

    private void OnInteractStarted(InputAction.CallbackContext ctx)
    {
        // Close chest UI on second E press
        if (_openChest != null) { CloseChest(); return; }

        // Loot pickup — quick tap E (works in any non-station state)
        if (_nearestLoot != null && _inventory != null && !_inventory.IsFull)
        {
            bool networked = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
            if (networked)
            {
                var netObj = _nearestLoot.GetComponent<NetworkObject>();
                if (netObj != null) PickupLootServerRpc(netObj.NetworkObjectId);
            }
            else
            {
                Debug.Log($"[Player] Picked up {_nearestLoot.Item.itemName}");
                _inventory.TryAddItem(_nearestLoot.Item);
                Destroy(_nearestLoot.gameObject);
                _nearestLoot = null;
            }
            return;
        }

        Debug.Log($"[Player] Interact STARTED | state={_state} | nearest={_nearestInteractable?.GetPromptText(this) ?? "none"}");
        if (_state == PlayerState.AtStation) { ReleaseFromStation(); return; }

        // Track hold progress for hold-type interactables (display only — rack coroutine is authority)
        if (_nearestInteractable != null && _nearestInteractable.HoldDurationFor(this) > 0f)
        {
            _holdStartTime = Time.time;
            _holdDuration  = _nearestInteractable.HoldDurationFor(this);
        }
        else { _holdStartTime = -1f; }

        _nearestInteractable?.OnInteractStart(this);
    }

    private void OnInteractHeld(InputAction.CallbackContext ctx)
    {
        Debug.Log($"[Player] Interact HELD (performed) | nearest={_nearestInteractable?.GetPromptText(this) ?? "none"}");
        _nearestInteractable?.OnInteractHold(this);
    }

    private void OnInteractCanceled(InputAction.CallbackContext ctx)
    {
        _holdStartTime = -1f;
        Debug.Log("[Player] Interact CANCELED");
        _nearestInteractable?.OnInteractCancel(this);
    }

    public void LockToStation(IInteractable station)
    {
        _state = PlayerState.AtStation;
        _currentStation = station;
        _verticalVelocity = 0f;
        Debug.Log($"[Player] Locked to station: {(station as UnityEngine.Object)?.name ?? station.ToString()}");
    }

    public void ReleaseFromStation()
    {
        Debug.Log("[Player] Released from station");
        if (_currentStation is AirPumpStation pump)
        {
            pump.OnOperatorLeft(this);
            // Tell server to zero the diver's flow rate
            _lastSentPumpFlow = 0f;
            bool networked = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
            if (networked) SendPumpFlowServerRpc(0f);
            else           ApplyPumpFlowLocal(0f);
        }
        if (_currentStation is WinchStation winchStation)
        {
            winchStation.OnOperatorLeft(this);
            _lastSentWinchPull = 0f;
            bool networked = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
            if (networked) SendWinchPullServerRpc(0f);
            else           ApplyWinchPullLocal(0f);
        }
        _currentStation?.Release(this);
        _currentStation = null;
        _state = PlayerState.OnDeck;
    }

    public void EquipSuit(DivingSuitRack rack, bool hasBoots = true)
    {
        _holdStartTime = -1f;
        // Scale oxygen proportionally so the HUD bar doesn't jump when capacity changes
        // (maxBreathSeconds → maxSuitBuffer). E.g. 30/30 = 100% stays 60/60 = 100%.
        _oxygen   = (_oxygen / maxBreathSeconds) * maxSuitBuffer;
        _hasBoots = hasBoots;
        _suitRack = rack;
        _state = PlayerState.WearingSuit;
        if (IsOwner) _networkWearingSuit.Value = true;
        _cableSystem?.ActivateCables();
        Debug.Log("[Player] Suit equipped → WearingSuit (slower movement)");
    }

    public void UnequipSuit()
    {
        _holdStartTime = -1f;
        if (_state != PlayerState.WearingSuit) return;
        _hasBoots      = false;
        _bootKickTimer = 0f;
        SetPumpFlowRate(0f);
        _winchPullSpeed = 0f;
        _suitRack?.ReturnSuit(_hasBoots);
        _suitRack = null;
        _state = PlayerState.OnDeck;
        _oxygen = maxBreathSeconds;   // back to normal breath above water
        if (IsOwner) _networkWearingSuit.Value = false;
        _cableSystem?.ClearAnchor();
        Debug.Log("[Player] Suit removed → OnDeck");
    }

    // True on all clients when this player is wearing a suit (networked) or local state (single-player)
    public bool IsWearingSuit
    {
        get
        {
            bool networked = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
            if (networked) return _networkWearingSuit.Value;
            return _state == PlayerState.WearingSuit
                || (_state == PlayerState.Underwater && _preDiveState == PlayerState.WearingSuit);
        }
    }

    private void UpdateOxygen()
    {
        float headY          = transform.position.y + _cc.height * 0.5f;
        bool  headUnderwater = _state == PlayerState.Underwater && headY < _currentWaveHeight;
        bool  suitOn         = _state == PlayerState.WearingSuit
                            || (_state == PlayerState.Underwater && _preDiveState == PlayerState.WearingSuit);

        if (suitOn)
        {
            if (headUnderwater)
            {
                float depth       = Mathf.Max(0f, _currentWaveHeight - transform.position.y);
                float speed       = new Vector3(_cc.velocity.x, 0f, _cc.velocity.z).magnitude;
                float consumption = 1f + depthDrainMultiplier * depth + movementDrainMultiplier * speed;
                float net         = _pumpFlowRate - consumption;
                _oxygen = Mathf.Clamp(_oxygen + net * Time.deltaTime, 0f, maxSuitBuffer);
            }
            else
            {
                // Above water with suit: drain at base rate; pump counters the drain
                float net = _pumpFlowRate - 1f;
                _oxygen = Mathf.Clamp(_oxygen + net * Time.deltaTime, 0f, maxSuitBuffer);
            }
        }
        else
        {
            if (headUnderwater)
                _oxygen = Mathf.Max(_oxygen - Time.deltaTime, 0f);
            else
                _oxygen = Mathf.Min(_oxygen + Time.deltaTime, maxBreathSeconds);
        }
    }

    private void UpdateHealth()
    {
        float headY = transform.position.y + _cc.height * 0.5f;
        bool  drowning = _state == PlayerState.Underwater && headY < _currentWaveHeight && _oxygen <= 0f;
        if (drowning)
            _health = Mathf.Max(_health - drownDamageRate * Time.deltaTime, 0f);

        if (_health <= 0f && !_hasDied)
        {
            _hasDied = true;

            // Return suit and clear ropes before entering spectator mode
            if (_state == PlayerState.WearingSuit)
                HandleDeathSuitReturn();

            EnterSpectatorMode();
            bool networked = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
            if (!networked || IsServer)
            {
                NetworkIsDead.Value = true;
                bool anyAlive = false;
                foreach (var pc in FindObjectsByType<PlayerController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                    if (!pc.NetworkIsDead.Value) { anyAlive = true; break; }
                if (!anyAlive) QuotaManager.Instance?.TriggerGameOver(1);
            }
            else EnterDeadStateServerRpc();
        }
    }

    /// <summary>Called by the pump station to set the continuous oxygen flow rate (oxygen/s).</summary>
    public void SetPumpFlowRate(float rate)
    {
        Debug.Log($"[Player] SetPumpFlowRate={rate:F2} (IsOwner={IsOwner})");
        _pumpFlowRate = rate;
    }

    // HUD read-only accessors
    public IInteractable NearestInteractable => _nearestInteractable;
    /// <summary>0–1 while holding E on a hold-type interactable; -1 when not applicable.</summary>
    public float InteractHoldProgress
    {
        get
        {
            if (_holdStartTime < 0f || _holdDuration <= 0f) return -1f;
            return Mathf.Clamp01((Time.time - _holdStartTime) / _holdDuration);
        }
    }
    public float Health        => _health;
    public float MaxHealth     => maxHealth;
    public float Oxygen        => _oxygen;
    public float OxygenCapacity => (_state == PlayerState.WearingSuit
                                 || _preDiveState == PlayerState.WearingSuit)
                                 ? maxSuitBuffer : maxBreathSeconds;
    public LootPickup NearestLoot => _nearestLoot;
    public bool  HasBoots         => _hasBoots;

    public void OpenChest(StorageChest chest)
    {
        _openChest = chest;
        _nearestInteractable = null;  // hide interaction prompt while chest UI is open
        OnChestOpened?.Invoke(chest);
    }

    public void CloseChest()
    {
        _openChest = null;
        OnChestClosed?.Invoke();
    }
    public bool  IsUnderwater     => _state == PlayerState.Underwater;
    public bool  IsHeadUnderwater
    {
        get
        {
            float headY = transform.position.y + _cc.height * 0.5f;
            return _state == PlayerState.Underwater && headY < _currentWaveHeight;
        }
    }
    public bool  IsUnderwaterWithSuit => _state == PlayerState.Underwater
                                      && _preDiveState == PlayerState.WearingSuit;
    /// <summary>0–1 while holding Q to kick boots off; -1 when not applicable.</summary>
    public float BootKickProgress => (_hasBoots && IsUnderwaterWithSuit)
        ? _bootKickTimer / bootKickHoldTime : -1f;

    // ── Pump RPCs ─────────────────────────────────────────────────────────────
    // Operator calls SendPumpFlowServerRpc on their OWN PlayerController.
    // Server finds the suited diver and forwards the flow rate via ClientRpc.

    [ServerRpc]
    private void SendPumpFlowServerRpc(float flowRate)
    {
        // Server-side: find the suited player by NetworkVariable (authoritative on server)
        foreach (var pc in FindObjectsByType<PlayerController>(FindObjectsInactive.Exclude))
        {
            if (pc._networkWearingSuit.Value)
            {
                Debug.Log($"[Server] Routing pump flow {flowRate:F2} to diver {pc.name}");
                pc.ReceivePumpFlowClientRpc(flowRate);
                return;
            }
        }
        Debug.Log("[Server] SendPumpFlow: no suited diver found");
    }

    [ClientRpc]
    private void ReceivePumpFlowClientRpc(float flowRate)
    {
        if (IsOwner) SetPumpFlowRate(flowRate);
    }

    /// <summary>Single-player fallback: find the suited player locally.</summary>
    private void ApplyPumpFlowLocal(float flowRate)
    {
        foreach (var pc in FindObjectsByType<PlayerController>(FindObjectsInactive.Exclude))
        {
            if (pc != this && pc.IsWearingSuit)
            {
                pc.SetPumpFlowRate(flowRate);
                return;
            }
        }
    }

    // ── Winch RPCs ──────────────────────────────────────────────────────────
    // Operator calls SendWinchPullServerRpc on their OWN PlayerController.
    // Server finds the suited diver and forwards the pull speed via ClientRpc.

    [ServerRpc]
    private void SendWinchPullServerRpc(float pullSpeed)
    {
        foreach (var pc in FindObjectsByType<PlayerController>(FindObjectsInactive.Exclude))
        {
            if (pc._networkWearingSuit.Value)
            {
                pc.ReceiveWinchPullClientRpc(pullSpeed);
                return;
            }
        }
    }

    [ClientRpc]
    private void ReceiveWinchPullClientRpc(float pullSpeed)
    {
        if (IsOwner) _winchPullSpeed = pullSpeed;
    }

    /// <summary>Single-player fallback: find the suited player locally.</summary>
    private void ApplyWinchPullLocal(float pullSpeed)
    {
        foreach (var pc in FindObjectsByType<PlayerController>(FindObjectsInactive.Exclude))
        {
            if (pc != this && pc.IsWearingSuit)
            {
                pc._winchPullSpeed = pullSpeed;
                return;
            }
        }
    }

    // ── Suit Equip/Unequip RPCs ───────────────────────────────────────────────
    // DivingSuitRack calls these on the PlayerController after the hold timer fires.
    // The server validates exclusivity before confirming to the owner.

    [ServerRpc]
    public void RequestEquipSuitServerRpc(ulong rackNetworkObjectId)
    {
        if (!NetworkManager.SpawnManager.SpawnedObjects
                .TryGetValue(rackNetworkObjectId, out var rackObj)) return;
        var rack = rackObj.GetComponent<DivingSuitRack>();
        if (rack == null || !rack.NetworkSuitAvailable) return;

        rack.ServerTakeSuit(NetworkObject.NetworkObjectId);
        ConfirmEquipSuitClientRpc(rackNetworkObjectId, rack.NetworkSuitHasBoots);
    }

    [ClientRpc]
    private void ConfirmEquipSuitClientRpc(ulong rackNetworkObjectId, bool hasBoots)
    {
        if (!IsOwner) return;
        if (!NetworkManager.SpawnManager.SpawnedObjects
                .TryGetValue(rackNetworkObjectId, out var rackObj)) return;
        var rack = rackObj.GetComponent<DivingSuitRack>();
        if (rack != null) EquipSuit(rack, hasBoots);
    }

    [ServerRpc]
    public void RequestUnequipSuitServerRpc(ulong rackNetworkObjectId)
    {
        if (!NetworkManager.SpawnManager.SpawnedObjects
                .TryGetValue(rackNetworkObjectId, out var rackObj)) return;
        var rack = rackObj.GetComponent<DivingSuitRack>();
        if (rack == null) return;
        if (rack.NetworkSuitWearerObjId != NetworkObject.NetworkObjectId) return;

        rack.ServerReturnSuit(false);   // boots always gone on unequip (matches existing offline behavior)
        ConfirmUnequipSuitClientRpc();
    }

    [ClientRpc]
    private void ConfirmUnequipSuitClientRpc()
    {
        if (!IsOwner) return;
        UnequipSuit();
    }

    /// <summary>Broadcast by DivingSuitRack.ServerForceReset on game reset. Skips ReturnSuit — rack already reset.</summary>
    [ClientRpc]
    public void ForceUnequipSuitClientRpc()
    {
        if (!IsOwner) return;
        ForceUnequipSuitLocal();
    }

    /// <summary>Shared implementation used by both the RPC and the offline reset path.</summary>
    public void ForceUnequipSuitLocal()
    {
        if (_state != PlayerState.WearingSuit) return;
        _holdStartTime = -1f;
        _hasBoots      = false;
        _bootKickTimer = 0f;
        SetPumpFlowRate(0f);
        _winchPullSpeed = 0f;
        _suitRack = null;
        _state    = PlayerState.OnDeck;
        _oxygen   = maxBreathSeconds;
        if (IsOwner) _networkWearingSuit.Value = false;
        _cableSystem?.ClearAnchor();
    }

    private void EnterSpectatorMode()
    {
        if (!IsOwner) return;
        _state = PlayerState.Dead;
        _cc.enabled = false;
        GetComponent<PlayerHUD>()?.HideForDeath();
        if (cameraRoot != null) { var pc = cameraRoot.GetComponent<PlayerCamera>(); if (pc) pc.enabled = false; }
        GetComponent<SpectatorCamera>()?.Activate(); // Activate also calls hud.SetTarget
    }

    [ServerRpc]
    private void EnterDeadStateServerRpc()
    {
        NetworkIsDead.Value = true;
        bool anyAlive = false;
        foreach (var pc in FindObjectsByType<PlayerController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            if (!pc.NetworkIsDead.Value) { anyAlive = true; break; }
        if (!anyAlive) QuotaManager.Instance?.TriggerGameOver(1);
    }

    /// <summary>
    /// Called on the owner when the player dies while wearing a suit.
    /// Clears ropes, returns the suit to the rack (via server RPC in multiplayer).
    /// Must run BEFORE EnterSpectatorMode changes state to Dead.
    /// </summary>
    private void HandleDeathSuitReturn()
    {
        _cableSystem?.ClearAnchor();
        SetPumpFlowRate(0f);
        _winchPullSpeed = 0f;
        if (IsOwner) _networkWearingSuit.Value = false;

        bool networked = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        if (networked && _suitRack != null)
            ReturnSuitOnDeathServerRpc(_suitRack.NetworkObjectId);
        else
            _suitRack?.ReturnSuit(false);

        _suitRack = null;
    }

    [ServerRpc]
    private void ReturnSuitOnDeathServerRpc(ulong rackNetworkObjectId)
    {
        if (!NetworkManager.SpawnManager.SpawnedObjects
                .TryGetValue(rackNetworkObjectId, out var rackObj)) return;
        var rack = rackObj.GetComponent<DivingSuitRack>();
        rack?.ServerReturnSuit(false);
    }

    [ServerRpc]
    private void ClearDeadStateServerRpc() => NetworkIsDead.Value = false;

    [ServerRpc]
    private void SetPlayerNameServerRpc(FixedString64Bytes requestedName)
    {
        // Collect names already in use by other players
        var used = new System.Collections.Generic.HashSet<string>();
        foreach (var pc in FindObjectsByType<PlayerController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            if (pc != this) used.Add(pc.NetworkPlayerName.Value.ToString());

        string wanted = requestedName.ToString();
        if (!used.Contains(wanted)) { NetworkPlayerName.Value = requestedName; return; }

        // Conflict — assign the first unused pirate name
        string[] pool = { "Scurvy Dog", "Bilge Rat", "Salty Pete", "Barnacle Bill", "Captain No-Name" };
        foreach (var n in pool)
            if (!used.Contains(n)) { NetworkPlayerName.Value = new FixedString64Bytes(n); return; }

        // All pool names taken — keep requested name as-is
        NetworkPlayerName.Value = requestedName;
    }

    // ── Loot Pickup/Drop RPCs ─────────────────────────────────────────────────

    [ServerRpc]
    private void PickupLootServerRpc(ulong lootNetObjId, ServerRpcParams rpc = default)
    {
        if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(lootNetObjId, out var netObj)) return;
        var loot = netObj.GetComponent<LootPickup>();
        if (loot == null) return;
        string itemId = loot.ItemId;

        // Use Despawn(false) for in-scene objects so late-joining clients
        // don't see ghost items. OnNetworkDespawn hides renderers/colliders.
        bool isSceneObject = netObj.IsSceneObject.HasValue && netObj.IsSceneObject.Value;
        netObj.Despawn(!isSceneObject);
        if (isSceneObject) netObj.gameObject.SetActive(false);

        var clientParams = new ClientRpcParams
            { Send = new ClientRpcSendParams { TargetClientIds = new[] { rpc.Receive.SenderClientId } } };
        ConfirmPickupClientRpc(itemId, clientParams);
    }

    [ClientRpc]
    private void ConfirmPickupClientRpc(string itemId, ClientRpcParams clientParams = default)
    {
        var item = _lootRegistry?.Find(itemId);
        if (item != null) _inventory?.TryAddItem(item);
        _nearestLoot = null;
    }

    [ServerRpc]
    private void DropItemServerRpc(string itemId, Vector3 dropPos, ServerRpcParams rpc = default)
    {
        var item = _lootRegistry?.Find(itemId);
        if (item?.worldPrefab == null) return;
        var go = Instantiate(item.worldPrefab, dropPos, Quaternion.identity);
        go.GetComponent<NetworkObject>()?.Spawn(true);
        var clientParams = new ClientRpcParams
            { Send = new ClientRpcSendParams { TargetClientIds = new[] { rpc.Receive.SenderClientId } } };
        ConfirmDropClientRpc(clientParams);
    }

    [ClientRpc]
    private void ConfirmDropClientRpc(ClientRpcParams clientParams = default)
    {
        _inventory?.RemoveSelected();
    }
}
