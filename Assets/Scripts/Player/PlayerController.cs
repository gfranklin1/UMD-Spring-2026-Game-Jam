using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class PlayerController : NetworkBehaviour
{
    private enum PlayerState { OnDeck, AtStation, WearingSuit, Underwater }

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
    [SerializeField] private Renderer bodyRenderer;
    [SerializeField] private PlayerInput playerInput;

    private CharacterController _cc;
    private PlayerState _state = PlayerState.OnDeck;
    private PlayerState _preDiveState = PlayerState.OnDeck;
    private float _verticalVelocity;
    private float _currentWaveHeight;
    private InputAction _moveAction;
    private InputAction _jumpAction;
    private InputAction _crouchAction;
    private InputAction _interactAction;
    private InputAction _sprintAction;
    private IInteractable _nearestInteractable;
    private IInteractable _currentStation;
    private DivingSuitRack _suitRack;
    private const float InteractRange = 2.5f;
    private float _health;
    private float _oxygen;
    private float _currentSpeed;
    private float _pumpFlowRate;   // oxygen/s currently delivered by the pump (0 when not pumping)
    private float _lastSentPumpFlow;

    // Synced so the server can find the suited player when routing pump RPCs
    private NetworkVariable<bool> _networkWearingSuit = new NetworkVariable<bool>(false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);

    private void Awake() => _cc = GetComponent<CharacterController>();

    private void Start()
    {
        if (oceanWaves == null)
            oceanWaves = FindObjectOfType<OceanWaves>();
    }

    public override void OnNetworkSpawn()
    {
        if (IsOwner)
        {
            if (bodyRenderer != null) bodyRenderer.enabled = false;

            if (playerInput != null)
            {
                playerInput.enabled = true;
                _moveAction   = playerInput.actions["Move"];
                _jumpAction   = playerInput.actions["Jump"];
                _crouchAction = playerInput.actions["Crouch"];
                _interactAction = playerInput.actions["Interact"];
                _sprintAction   = playerInput.actions["Sprint"];
                _interactAction.started   += OnInteractStarted;
                _interactAction.performed += OnInteractHeld;
                _interactAction.canceled  += OnInteractCanceled;
            }

            _health = maxHealth;
            _oxygen = maxBreathSeconds;
            _currentSpeed = walkSpeed;
            enabled = true;
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

        enabled = false;
    }

    public override void OnNetworkDespawn()
    {
        if (IsOwner && _interactAction != null)
        {
            _interactAction.started   -= OnInteractStarted;
            _interactAction.performed -= OnInteractHeld;
            _interactAction.canceled  -= OnInteractCanceled;
        }
    }

    private void Update()
    {
        ScanForInteractables();
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
                HandleUnderwaterMovement();
                break;
        }
        // Run after UpdateWaterState so _state reflects this frame
        UpdateOxygen();
        UpdateHealth();
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
            // Carry full downward momentum through so the player dives naturally.
            // Only strip upward velocity; don't cap how fast they can sink on entry.
            if (_verticalVelocity > 0f) _verticalVelocity = 0f;
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

    private void HandleDeckMovement()
    {
        if (_moveAction == null) return;
        var moveInput = _moveAction.ReadValue<Vector2>();

        if (_cc.isGrounded && _verticalVelocity < 0f)
            _verticalVelocity = -2f;

        if (_cc.isGrounded && _jumpAction != null && _jumpAction.WasPressedThisFrame())
            _verticalVelocity = jumpForce;

        _verticalVelocity += gravity * Time.deltaTime;

        bool sprinting = _state == PlayerState.OnDeck && _sprintAction != null && _sprintAction.IsPressed();
        float targetSpeed = _state == PlayerState.WearingSuit ? suitWalkSpeed
                          : sprinting                         ? sprintSpeed
                          :                                    walkSpeed;
        _currentSpeed = Mathf.MoveTowards(_currentSpeed, targetSpeed, speedChangeRate * Time.deltaTime);
        Vector3 move = transform.TransformDirection(new Vector3(moveInput.x, 0f, moveInput.y));
        _cc.Move((move * _currentSpeed + Vector3.up * _verticalVelocity) * Time.deltaTime);
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

        _cc.Move((horizontal + Vector3.up * _verticalVelocity) * Time.deltaTime);
    }

    private void ScanForInteractables()
    {
        if (_state == PlayerState.AtStation || _state == PlayerState.Underwater)
        {
            _nearestInteractable = null;
            return;
        }
        Collider[] hits = Physics.OverlapSphere(transform.position, InteractRange);
        IInteractable nearest = null;
        float nearestDist = float.MaxValue;
        foreach (var hit in hits)
        {
            var interactable = hit.GetComponentInParent<IInteractable>();
            if (interactable == null) continue;
            float dist = (hit.transform.position - transform.position).sqrMagnitude;
            if (dist < nearestDist) { nearestDist = dist; nearest = interactable; }
        }
        if (nearest != _nearestInteractable)
        {
            _nearestInteractable = nearest;
            if (nearest != null)
                Debug.Log($"[Player] In range: {nearest.GetPromptText()}");
            else
                Debug.Log("[Player] Left interact range");
        }
    }

    private void HandleAtStationState()
    {
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
    }

    private void OnInteractStarted(InputAction.CallbackContext ctx)
    {
        Debug.Log($"[Player] Interact STARTED | state={_state} | nearest={_nearestInteractable?.GetPromptText() ?? "none"}");
        if (_state == PlayerState.AtStation) { ReleaseFromStation(); return; }
        _nearestInteractable?.OnInteractStart(this);
    }

    private void OnInteractHeld(InputAction.CallbackContext ctx)
    {
        Debug.Log($"[Player] Interact HELD (performed) | nearest={_nearestInteractable?.GetPromptText() ?? "none"}");
        _nearestInteractable?.OnInteractHold(this);
    }

    private void OnInteractCanceled(InputAction.CallbackContext ctx)
    {
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
        _currentStation?.Release(this);
        _currentStation = null;
        _state = PlayerState.OnDeck;
    }

    public void EquipSuit(DivingSuitRack rack)
    {
        _oxygen = 0f;   // line is empty until pumped
        _suitRack = rack;
        _state = PlayerState.WearingSuit;
        if (IsOwner) _networkWearingSuit.Value = true;
        Debug.Log("[Player] Suit equipped → WearingSuit (slower movement)");
    }

    public void UnequipSuit()
    {
        if (_state != PlayerState.WearingSuit) return;
        SetPumpFlowRate(0f);
        _suitRack?.ReturnSuit();
        _suitRack = null;
        _state = PlayerState.OnDeck;
        _oxygen = maxBreathSeconds;   // back to normal breath above water
        if (IsOwner) _networkWearingSuit.Value = false;
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
                // Above water with suit: pump pre-fills the line buffer
                if (_pumpFlowRate > 0f)
                    _oxygen = Mathf.Min(_oxygen + _pumpFlowRate * Time.deltaTime, maxSuitBuffer);
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
    }

    /// <summary>Called by the pump station to set the continuous oxygen flow rate (oxygen/s).</summary>
    public void SetPumpFlowRate(float rate)
    {
        Debug.Log($"[Player] SetPumpFlowRate={rate:F2} (IsOwner={IsOwner})");
        _pumpFlowRate = rate;
    }

    // HUD read-only accessors
    public float Health        => _health;
    public float MaxHealth     => maxHealth;
    public float Oxygen        => _oxygen;
    public float OxygenCapacity => (_state == PlayerState.WearingSuit
                                 || _preDiveState == PlayerState.WearingSuit)
                                 ? maxSuitBuffer : maxBreathSeconds;

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
}
