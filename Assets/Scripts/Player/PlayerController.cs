using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class PlayerController : NetworkBehaviour
{
    private enum PlayerState { OnDeck, AtStation, WearingSuit, Underwater }

    [Header("Movement")]
    [SerializeField] private float walkSpeed = 4f;
    [SerializeField] private float suitWalkSpeed = 2f;
    [SerializeField] private float swimSpeed = 2.5f;
    [SerializeField] private float swimVerticalSpeed = 2f;
    [SerializeField] private float gravity = -15f;
    [SerializeField] private float jumpForce = 6f;
    [SerializeField] private float surfaceFloatDepth = 1.2f;
    [SerializeField] private float buoyancySpring = 6f;

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
    private IInteractable _nearestInteractable;
    private InteractableStation _currentStation;
    private DivingSuitRack _suitRack;
    private const float InteractRange = 2.5f;

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
                _interactAction.started   += OnInteractStarted;
                _interactAction.performed += OnInteractHeld;
                _interactAction.canceled  += OnInteractCanceled;
            }

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
            // Carry downward momentum so the player dives in naturally.
            // Clamp: never upward on entry, never faster than 2× swimVerticalSpeed.
            _verticalVelocity = Mathf.Clamp(_verticalVelocity, -swimVerticalSpeed * 2f, 0f);
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

        float speed = _state == PlayerState.WearingSuit ? suitWalkSpeed : walkSpeed;
        Vector3 move = transform.TransformDirection(new Vector3(moveInput.x, 0f, moveInput.y));
        _cc.Move((move * speed + Vector3.up * _verticalVelocity) * Time.deltaTime);
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

        float targetVertical;
        float lerpSpeed;
        if (pushingUp)
        {
            targetVertical = swimVerticalSpeed * 2.5f;  // noticeably faster than natural buoyancy
            lerpSpeed = 10f;
        }
        else if (pushingDown)
        {
            targetVertical = -swimVerticalSpeed * 1.5f;  // slightly easier to dive
            lerpSpeed = 10f;
        }
        else
        {
            float targetY  = _currentWaveHeight - surfaceFloatDepth;
            float error    = targetY - transform.position.y;
            targetVertical = Mathf.Clamp(error * buoyancySpring, -swimVerticalSpeed, swimVerticalSpeed);
            lerpSpeed = 6f;
        }

        _verticalVelocity = Mathf.Lerp(_verticalVelocity, targetVertical, Time.deltaTime * lerpSpeed);
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
            var interactable = hit.GetComponent<IInteractable>();
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
        if (moveInput.magnitude > 0.1f)
            ReleaseFromStation();
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

    public void LockToStation(InteractableStation station)
    {
        _state = PlayerState.AtStation;
        _currentStation = station;
        _verticalVelocity = 0f;
        Debug.Log($"[Player] Locked to station: {station.name}");
    }

    public void ReleaseFromStation()
    {
        Debug.Log("[Player] Released from station");
        _currentStation?.Release(this);
        _currentStation = null;
        _state = PlayerState.OnDeck;
    }

    public void EquipSuit(DivingSuitRack rack)
    {
        _suitRack = rack;
        _state = PlayerState.WearingSuit;
        Debug.Log("[Player] Suit equipped → WearingSuit (slower movement)");
    }

    public void UnequipSuit()
    {
        if (_state != PlayerState.WearingSuit) return;
        _suitRack?.ReturnSuit();
        _suitRack = null;
        _state = PlayerState.OnDeck;
        Debug.Log("[Player] Suit removed → OnDeck");
    }
}
