using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace _Game.Scripts
{
public class PlayerMovementController : MonoBehaviour, PlayerInputActions.IPlayerActions
{
    [Header("Movement")]
    [SerializeField] private Rigidbody2D rb;
    [SerializeField, Min(0f)] private float moveSpeed = 5f;
    [SerializeField, Min(1f)] private float sprintMultiplier = 1.5f;
    [SerializeField, Range(0f, 1f)] private float inputDeadZone = 0.05f;

    [Header("Attack")]
    [SerializeField, Min(0f)] private float baseAttackDamage = 15f;
    [SerializeField, Min(0f)] private float rushAttackDamage = 22f;
    [SerializeField, Min(0f)] private float attackRange = 1.2f;
    [SerializeField, Min(0f)] private float rushAttackRange = 1.5f;
    [SerializeField, Min(0f)] private float attackRadius = 0.35f;
    [SerializeField, Min(0f)] private float rushAttackRadius = 0.45f;

    private PlayerInputActions _playerInputActions;
    private Vector2 _moveInput;
    private Vector2 _rawMoveInput;
    private Vector2 _lookInput;
    private Vector2 _facingDirection = Vector2.right;
    private bool _isSprinting;
    private PlayerAttackData _lastAttackData;
    private int _attackSequence;

    public event Action InteractRequested;
    public event Action<PlayerAttackData> AttackPerformed;

    public Vector2 MoveInput => _moveInput;
    public Vector2 RawMoveInput => _rawMoveInput;
    public Vector2 LookInput => _lookInput;
    public Vector2 FacingDirection => _facingDirection;
    public Vector2 CurrentVelocity => rb != null ? rb.linearVelocity : _moveInput * CurrentMoveSpeed;
    public float CurrentMoveSpeed => moveSpeed * (_isSprinting ? sprintMultiplier : 1f);
    public bool IsSprinting => _isSprinting;
    public PlayerAttackData LastAttackData => _lastAttackData;

    private void Awake()
    {
        _playerInputActions = new PlayerInputActions();

        if (rb == null)
        {
            rb = GetComponent<Rigidbody2D>();
        }
    }

    private void OnEnable()
    {
        _playerInputActions.Player.SetCallbacks(this);
        _playerInputActions.Player.Enable();
    }

    private void OnDisable()
    {
        _playerInputActions.Player.SetCallbacks(null);
        _playerInputActions.Player.Disable();
        _moveInput = Vector2.zero;
        _rawMoveInput = Vector2.zero;
        _lookInput = Vector2.zero;
        _isSprinting = false;

        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
        }
    }

    private void OnDestroy()
    {
        _playerInputActions?.Dispose();
    }

    private void FixedUpdate()
    {
        if (rb == null)
        {
            return;
        }

        var desiredVelocity = _moveInput * CurrentMoveSpeed;
        rb.linearVelocity = desiredVelocity;
    }

    public void OnMove(InputAction.CallbackContext context)
    {
        _rawMoveInput = context.ReadValue<Vector2>();
        _moveInput = _rawMoveInput.sqrMagnitude < inputDeadZone * inputDeadZone ? Vector2.zero : _rawMoveInput.normalized;
        UpdateFacingDirection(_moveInput);
    }

    public void OnInteract(InputAction.CallbackContext context)
    {
        if (!context.performed)
        {
            return;
        }

        InteractRequested?.Invoke();
        HandleInteract();
    }

    protected virtual void HandleInteract()
    {
    }

    public void OnLook(InputAction.CallbackContext context)
    {
        var value = context.ReadValue<Vector2>();
        _lookInput = value.sqrMagnitude < inputDeadZone * inputDeadZone ? Vector2.zero : value.normalized;
        UpdateFacingDirection(_lookInput);
    }

    public void OnAttack(InputAction.CallbackContext context)
    {
        if (!context.performed)
        {
            return;
        }

        var attackDirection = ResolveAttackDirection();
        var attackStyle = ResolveAttackStyle(attackDirection);
        var damage = attackStyle == PlayerAttackStyle.Rush ? rushAttackDamage : baseAttackDamage;
        var range = attackStyle == PlayerAttackStyle.Rush ? rushAttackRange : attackRange;
        var radius = attackStyle == PlayerAttackStyle.Rush ? rushAttackRadius : attackRadius;
        _lastAttackData = new PlayerAttackData(++_attackSequence, attackStyle, attackDirection, transform.position, Time.time, _isSprinting, damage, range, radius);
        AttackPerformed?.Invoke(_lastAttackData);
    }

    public void OnCrouch(InputAction.CallbackContext context)
    {
    }

    public void OnJump(InputAction.CallbackContext context)
    {
    }

    public void OnPrevious(InputAction.CallbackContext context)
    {
    }

    public void OnNext(InputAction.CallbackContext context)
    {
    }

    public void OnSprint(InputAction.CallbackContext context)
    {
        if (context.canceled)
        {
            _isSprinting = false;
            return;
        }

        if (context.started || context.performed)
        {
            _isSprinting = true;
        }
    }

    private Vector2 ResolveAttackDirection()
    {
        if (_lookInput.sqrMagnitude > 0f)
        {
            return _lookInput.normalized;
        }

        if (_moveInput.sqrMagnitude > 0f)
        {
            return _moveInput.normalized;
        }

        return _facingDirection;
    }

    private PlayerAttackStyle ResolveAttackStyle(Vector2 attackDirection)
    {
        if (_isSprinting && _moveInput.sqrMagnitude > 0.25f)
        {
            return PlayerAttackStyle.Rush;
        }

        var absX = Mathf.Abs(attackDirection.x);
        var absY = Mathf.Abs(attackDirection.y);

        if (absX > 0.8f && absY < 0.35f)
        {
            return PlayerAttackStyle.Horizontal;
        }

        if (absY > 0.8f && absX < 0.35f)
        {
            return PlayerAttackStyle.Vertical;
        }

        if (attackDirection.sqrMagnitude > 0f)
        {
            return PlayerAttackStyle.Diagonal;
        }

        return PlayerAttackStyle.Neutral;
    }

    private void UpdateFacingDirection(Vector2 candidateDirection)
    {
        if (candidateDirection.sqrMagnitude <= 0f)
        {
            return;
        }

        _facingDirection = candidateDirection.normalized;
    }
}
}
