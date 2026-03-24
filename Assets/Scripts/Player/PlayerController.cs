using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// First-person player controller with two movement states:
///   OnDeck    — normal WASD + gravity via CharacterController
///   Underwater — full 3D swimming (WASD horizontal, Space = up, Ctrl/C = down)
///
/// Reads input directly from Keyboard.current / Mouse.current — no PlayerInput
/// component callbacks needed. Each machine controls exactly one player, so
/// global device polling is correct for online co-op.
///
/// Only the owning client runs input + movement. Remote players are driven by
/// NetworkTransform sync only.
/// </summary>
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

    private CharacterController _cc;
    private PlayerState _state = PlayerState.OnDeck;
    private float _verticalVelocity;

    private void Awake()
    {
        _cc = GetComponent<CharacterController>();
    }

    private void Start()
    {
        if (oceanWaves == null)
            oceanWaves = FindObjectOfType<OceanWaves>();
    }

    public override void OnNetworkSpawn()
    {
        if (IsOwner) return;

        // Disable camera for remote players
        if (cameraRoot != null)
        {
            var cam = cameraRoot.GetComponent<Camera>();
            if (cam != null) cam.enabled = false;

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
        var kb = Keyboard.current;
        if (kb == null) return;

        float h = (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f);
        float v = (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f);
        var moveInput = new Vector2(h, v);

        if (_cc.isGrounded && _verticalVelocity < 0f)
            _verticalVelocity = -2f;

        _verticalVelocity += gravity * Time.deltaTime;

        Vector3 move = transform.TransformDirection(new Vector3(moveInput.x, 0f, moveInput.y));
        _cc.Move((move * walkSpeed + Vector3.up * _verticalVelocity) * Time.deltaTime);
    }

    private void HandleUnderwaterMovement()
    {
        var kb = Keyboard.current;
        if (kb == null) return;

        float h = (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f);
        float v = (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f);

        Transform cam = cameraRoot != null ? cameraRoot : transform;
        Vector3 forward = Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized;
        Vector3 right   = Vector3.ProjectOnPlane(cam.right,   Vector3.up).normalized;

        Vector3 horizontal = (forward * v + right * h) * swimSpeed;

        float targetVertical = 0f;
        if (kb.spaceKey.isPressed)   targetVertical =  swimVerticalSpeed;
        if (kb.leftCtrlKey.isPressed || kb.cKey.isPressed) targetVertical = -swimVerticalSpeed;

        _verticalVelocity = Mathf.Lerp(_verticalVelocity, targetVertical, Time.deltaTime * 4f);

        _cc.Move((horizontal + Vector3.up * _verticalVelocity) * Time.deltaTime);
    }
}
