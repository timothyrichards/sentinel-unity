using UnityEngine;
using UnityEngine.InputSystem;
using SpacetimeDB.Types;
using QFSW.QC;

[RequireComponent(typeof(CharacterController))]
public class ThirdPersonController : MonoBehaviour
{
    [Header("Runtime")]
    [SerializeField] private PlayerEntity playerEntity;
    [SerializeField] private CharacterController controller;
    [SerializeField] private MovementReconciliation reconciliation;
    public Transform cameraTransform;
    [SerializeField] private bool movingPlayer = false;

    [Header("Movement Settings")]
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float rotationSpeed = 10f;
    [SerializeField] private float jumpHeight = 2f;
    [SerializeField] private float gravity = -9.81f;
    [SerializeField] private float acceleration = 10f;
    [SerializeField] private float deceleration = 8f;
    [SerializeField]
    private AnimationCurve accelerationCurve = new(
        new Keyframe(0f, 0f, 0f, 0.5f),         // Start slow
        new Keyframe(0.3f, 0.15f, 0.8f, 0.8f),  // Initial push
        new Keyframe(0.7f, 0.85f, 0.8f, 0.8f),  // Building momentum
        new Keyframe(1f, 1f, 0.5f, 0f)          // Final push to top speed
    );
    [SerializeField]
    private AnimationCurve decelerationCurve = new(
        new Keyframe(0f, 0f, 0f, 2f),           // Quick initial slowdown
        new Keyframe(0.3f, 0.7f, 1f, 1f),       // Rapid deceleration
        new Keyframe(0.7f, 0.9f, 0.5f, 0.5f),   // Starting to coast
        new Keyframe(1f, 1f, 0.2f, 0f)          // Gentle stop
    );

    private PlayerInputActions inputActions;
    private Vector2 moveInput;
    private Vector3 velocity;
    private Vector3 currentMovement;
    private bool jumpQueued;
    private bool IsMoving => moveInput.sqrMagnitude > 0.01f;
    public bool IsGrounded => controller.isGrounded;
    private float currentLerpTime = 0f;
    private bool wasMoving = false;

    private void Awake()
    {
        playerEntity = GetComponent<PlayerEntity>();
        controller = GetComponent<CharacterController>();
        reconciliation = GetComponent<MovementReconciliation>();
        inputActions = new PlayerInputActions();
    }

    private void OnEnable()
    {
        inputActions.Player.Move.performed += OnMove;
        inputActions.Player.Move.canceled += OnMove;
        inputActions.Player.Jump.performed += OnJump;
        inputActions.Player.Attack.performed += OnAttack;
        inputActions.Player.Look.performed += OnLook;
        inputActions.Enable();
    }

    private void OnDisable()
    {
        inputActions.Player.Move.performed -= OnMove;
        inputActions.Player.Move.canceled -= OnMove;
        inputActions.Player.Jump.performed -= OnJump;
        inputActions.Player.Attack.performed -= OnAttack;
        inputActions.Player.Look.performed -= OnLook;
        inputActions.Disable();
    }

    /// <summary>
    /// Set the movement speed from the server (server-authoritative)
    /// </summary>
    public void SetMovementSpeed(float speed)
    {
        moveSpeed = speed;
    }

    private void OnMove(InputAction.CallbackContext context)
    {
        if (!playerEntity.InputEnabled) return;

        moveInput = context.ReadValue<Vector2>();
    }

    private void OnJump(InputAction.CallbackContext context)
    {
        if (!playerEntity.InputEnabled) return;

        if (context.ReadValue<float>() > 0.5f && IsGrounded)
        {
            jumpQueued = true;
        }
    }

    private void OnAttack(InputAction.CallbackContext context)
    {
        if (!playerEntity.InputEnabled) return;
        if (Cursor.lockState != CursorLockMode.Locked) return;
        if (BuildingSystem.Instance.IsEnabled) return;

        if (context.ReadValue<float>() > 0.5f)
        {
            playerEntity.animController.TriggerAttack();
        }
    }

    private void OnLook(InputAction.CallbackContext context)
    {
        // Skip camera input if cursor is unlocked
        if (playerEntity.CameraFreeForm != null)
            playerEntity.CameraFreeForm.enabled = Cursor.lockState == CursorLockMode.Locked;
    }

    private void Update()
    {
        if (!playerEntity.InputEnabled)
        {
            moveInput = Vector2.zero;
            currentMovement = Vector3.zero;
            playerEntity.animController.SetMovementAnimation(Vector2.zero, false, IsGrounded);
            playerEntity.animController.UpdateCombatLayerWeight(false, IsGrounded);
            controller.Move(new Vector3(0, velocity.y, 0) * Time.deltaTime);
            return;
        }

        HandleLook();
        HandleMove();

        playerEntity.animController.SetMovementAnimation(moveInput, IsMoving, IsGrounded);
        playerEntity.animController.UpdateCombatLayerWeight(IsMoving, IsGrounded);

        if (movingPlayer) return;
        if (!SpacetimeManager.IsConnected() || !playerEntity.IsLocalPlayer()) return;

        // Don't send position updates while reconciling (prevents feedback loop)
        if (reconciliation != null && reconciliation.IsReconciling) return;

        var position = new DbVector3(transform.position.x, transform.position.y, transform.position.z);
        var rotation = new DbVector3(transform.eulerAngles.x, transform.eulerAngles.y, transform.eulerAngles.z);
        var animState = new DbAnimationState(
            moveInput.x,
            moveInput.y,
            (uint)playerEntity.animController.ComboCount,
            IsMoving,
            IsGrounded,
            playerEntity.animController.IsJumping,
            playerEntity.animController.IsAttacking
        );

        // Record the move for reconciliation
        if (reconciliation != null)
        {
            reconciliation.RecordMove(transform.position);
        }

        ReducerMiddleware.Instance.CallReducer<object[]>(
            "PlayerUpdate",
            _ => SpacetimeManager.Conn.Reducers.PlayerUpdate(position, rotation, animState),
            position, rotation, animState
        );
    }

    private void LateUpdate()
    {
        // Apply gravity regardless of input state
        if (!IsGrounded)
        {
            velocity.y += gravity * Time.deltaTime;
        }
    }

    private float CalculateYawDelta()
    {
        if (playerEntity.CameraFreeForm == null) return 0f;

        float cameraYaw = playerEntity.CameraFreeForm.transform.eulerAngles.y;
        float characterYaw = transform.eulerAngles.y;

        return Mathf.DeltaAngle(characterYaw, cameraYaw);
    }

    private void HandleLook()
    {
        if (playerEntity.CameraFreeForm == null || Cursor.lockState != CursorLockMode.Locked) return;

        // Smoothly rotate to face the camera direction with a slight lazy follow
        if (IsMoving || !IsGrounded)
        {
            float targetYaw = playerEntity.CameraFreeForm.transform.eulerAngles.y;
            Quaternion targetRotation = Quaternion.Euler(0, targetYaw, 0);
            transform.rotation = Quaternion.Lerp(
                transform.rotation,
                targetRotation,
                rotationSpeed * Time.deltaTime
            );
        }
    }

    private void HandleMove()
    {
        if (IsGrounded && velocity.y < 0)
            velocity.y = -2f;

        Vector3 targetMovement = new(moveInput.x, 0, moveInput.y);
        if (targetMovement.magnitude > 0.01f)
        {
            targetMovement = Quaternion.Euler(0, cameraTransform ? cameraTransform.eulerAngles.y : transform.eulerAngles.y, 0) * targetMovement;
            targetMovement *= moveSpeed;
        }

        bool isMovingNow = targetMovement.magnitude > 0.01f;

        // Reset lerp time when changing between moving and not moving
        if (wasMoving != isMovingNow)
        {
            currentLerpTime = 0f;
            wasMoving = isMovingNow;
        }

        // Increment the lerp time
        currentLerpTime += Time.deltaTime * (isMovingNow ? acceleration : deceleration);
        currentLerpTime = Mathf.Clamp01(currentLerpTime);

        // Apply the appropriate curve
        float lerpFactor = isMovingNow
            ? accelerationCurve.Evaluate(currentLerpTime)
            : decelerationCurve.Evaluate(currentLerpTime);

        currentMovement = Vector3.Lerp(currentMovement, targetMovement, lerpFactor);

        if (jumpQueued)
        {
            velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
            playerEntity.animController.TriggerJump();
            jumpQueued = false;
        }

        // Combine horizontal movement and vertical velocity into a single movement vector
        Vector3 finalMovement = currentMovement + velocity;
        controller.Move(finalMovement * Time.deltaTime);
    }

    [Command]
    public void ForceMove(float x, float y, float z)
    {
        movingPlayer = true;
        controller.enabled = false;
        transform.position = new Vector3(x, y, z);
        controller.enabled = true;
        movingPlayer = false;
    }
}