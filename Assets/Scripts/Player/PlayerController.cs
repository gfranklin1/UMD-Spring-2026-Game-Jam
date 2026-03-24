using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class PlayerController : NetworkBehaviour
{
    private enum PlayerState { OnDeck, Underwater }

    [Header("Movement")]
    [SerializeField] private float walkSpeed = 4f;
    [SerializeField] private float swimSpeed = 2.5f;
    [SerializeField] private float swimVerticalSpeed = 2f;
    [SerializeField] private float gravity = -15f;

    [Header("References")]
    [SerializeField] private OceanWaves oceanWaves;
    [SerializeField] private Transform cameraRoot;
    [SerializeField] private Renderer bodyRenderer;
    [SerializeField] private PlayerInput playerInput;

    private CharacterController _cc;
    private PlayerState _state = PlayerState.OnDeck;
    private float _verticalVelocity;
    private InputAction _moveAction;
    private InputAction _jumpAction;
    private InputAction _crouchAction;

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
                _moveAction  = playerInput.actions["Move"];
                _jumpAction  = playerInput.actions["Jump"];
                _crouchAction = playerInput.actions["Crouch"];
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

    private void Update()
    {
        UpdateWaterState();

        if (_state == PlayerState.OnDeck)
            HandleDeckMovement();
        else
            HandleUnderwaterMovement();
    }

    private void UpdateWaterState()
    {
        if (oceanWaves == null) return;

        float waveH = oceanWaves.GetWaveHeight(transform.position);
        bool inWater = transform.position.y < waveH;

        if (inWater && _state == PlayerState.OnDeck)
        {
            _state = PlayerState.Underwater;
            _verticalVelocity = 0f;
        }
        else if (!inWater && _state == PlayerState.Underwater)
        {
            _state = PlayerState.OnDeck;
            _verticalVelocity = 0f;
        }
    }

    private void HandleDeckMovement()
    {
        if (_moveAction == null) return;
        var moveInput = _moveAction.ReadValue<Vector2>();

        if (_cc.isGrounded && _verticalVelocity < 0f)
            _verticalVelocity = -2f;

        _verticalVelocity += gravity * Time.deltaTime;

        Vector3 move = transform.TransformDirection(new Vector3(moveInput.x, 0f, moveInput.y));
        _cc.Move((move * walkSpeed + Vector3.up * _verticalVelocity) * Time.deltaTime);
    }

    private void HandleUnderwaterMovement()
    {
        if (_moveAction == null) return;
        var moveInput = _moveAction.ReadValue<Vector2>();

        Transform cam = cameraRoot != null ? cameraRoot : transform;
        Vector3 forward = Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized;
        Vector3 right   = Vector3.ProjectOnPlane(cam.right,   Vector3.up).normalized;
        Vector3 horizontal = (forward * moveInput.y + right * moveInput.x) * swimSpeed;

        float targetVertical = 0f;
        if (_jumpAction  != null && _jumpAction.IsPressed())  targetVertical =  swimVerticalSpeed;
        if (_crouchAction != null && _crouchAction.IsPressed()) targetVertical = -swimVerticalSpeed;

        _verticalVelocity = Mathf.Lerp(_verticalVelocity, targetVertical, Time.deltaTime * 4f);
        _cc.Move((horizontal + Vector3.up * _verticalVelocity) * Time.deltaTime);
    }
}
