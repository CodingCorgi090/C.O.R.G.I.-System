using System;
using _Game.Scripts.Combat;
using UnityEngine;
using UnityEngine.InputSystem;

namespace _Game.Scripts
{
[RequireComponent(typeof(Shield2D))]
[RequireComponent(typeof(HitReaction2D))]
public class PlayerMovementController : MonoBehaviour, PlayerInputActions.IPlayerActions
{
    [Header("Movement")]
    [SerializeField] private Camera aimCamera;
    [SerializeField] private Rigidbody2D rb;
    [SerializeField] private HitReaction2D hitReaction;
    [SerializeField] private Shield2D shield;
    [SerializeField, Min(0f)] private float moveSpeed = 5f;
    [SerializeField, Min(1f)] private float sprintMultiplier = 1.5f;
    [SerializeField, Range(0f, 1f)] private float inputDeadZone = 0.05f;

    [Header("Shield")]
    [SerializeField, Min(0.05f)] private float shieldHoldThreshold = 0.2f;

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
    private bool _isMouseAttackHeld;
    private bool _shieldActivatedFromMouseAttack;
    private PlayerAttackData _lastAttackData;
    private float _mouseAttackPressedTime;
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
    public bool IsShielding => shield != null && shield.IsShielding;
    public PlayerAttackData LastAttackData => _lastAttackData;
    public bool CanAct => hitReaction == null || !hitReaction.IsActionLocked;

    private void Awake()
    {
        _playerInputActions = new PlayerInputActions();

        if (rb == null)
        {
            rb = GetComponent<Rigidbody2D>();
        }

        if (hitReaction == null)
        {
            hitReaction = GetComponent<HitReaction2D>();
        }

        if (shield == null)
        {
            shield = GetComponent<Shield2D>();
        }

        if (aimCamera == null)
        {
            aimCamera = Camera.main;
        }

        shield?.SetFacing(_facingDirection);
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
        _isMouseAttackHeld = false;
        _shieldActivatedFromMouseAttack = false;
        SetShielding(false);

        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
        }
    }

    private void OnDestroy()
    {
        _playerInputActions?.Dispose();
    }

    private void Update()
    {
        SyncShieldFacing();
    }

    private void FixedUpdate()
    {
        if (rb == null)
        {
            return;
        }

        UpdateMouseShieldState();

        var desiredVelocity = _moveInput * CurrentMoveSpeed;
        if (IsShielding && shield != null)
        {
            desiredVelocity *= shield.MovementMultiplier;
        }

        if (hitReaction != null)
        {
            desiredVelocity = hitReaction.GetModifiedVelocity(desiredVelocity);
        }

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
        if (!context.performed || !CanAct)
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
        if (IsMouseAttackContext(context))
        {
            HandleMouseAttackContext(context);
            return;
        }

        if (!context.performed || !CanAct || IsShielding)
        {
            return;
        }

        PerformAttack(false);
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
        if (!CanAct || IsShielding)
        {
            if (context.canceled)
            {
                _isSprinting = false;
            }

            return;
        }

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

    private void PerformAttack(bool preferMouseAim)
    {
        var attackDirection = ResolveAttackDirection(preferMouseAim);
        var attackStyle = ResolveAttackStyle(attackDirection);
        var damage = attackStyle == PlayerAttackStyle.Rush ? rushAttackDamage : baseAttackDamage;
        var range = attackStyle == PlayerAttackStyle.Rush ? rushAttackRange : attackRange;
        var radius = attackStyle == PlayerAttackStyle.Rush ? rushAttackRadius : attackRadius;
        _lastAttackData = new PlayerAttackData(++_attackSequence, attackStyle, attackDirection, transform.position, Time.time, _isSprinting, damage, range, radius);
        AttackPerformed?.Invoke(_lastAttackData);
    }

    private void HandleMouseAttackContext(InputAction.CallbackContext context)
    {
        if (context.started)
        {
            _isMouseAttackHeld = CanAct;
            _mouseAttackPressedTime = Time.time;
            _shieldActivatedFromMouseAttack = false;
            return;
        }

        if (!context.canceled)
        {
            return;
        }

        var usedShield = _shieldActivatedFromMouseAttack || IsShielding;
        _isMouseAttackHeld = false;
        _shieldActivatedFromMouseAttack = false;
        SetShielding(false);

        if (!usedShield && CanAct)
        {
            PerformAttack(true);
        }
    }

    private void UpdateMouseShieldState()
    {
        if (!CanAct)
        {
            if (_isMouseAttackHeld)
            {
                _shieldActivatedFromMouseAttack = true;
            }

            SetShielding(false);
            return;
        }

        if (!_isMouseAttackHeld)
        {
            return;
        }

        if (_shieldActivatedFromMouseAttack)
        {
            UpdateMouseFacingDirection();
            SetShielding(true);
            return;
        }

        if (Time.time < _mouseAttackPressedTime + shieldHoldThreshold)
        {
            return;
        }

        _shieldActivatedFromMouseAttack = true;
        UpdateMouseFacingDirection();
        SetShielding(true);
    }

    private void SetShielding(bool isShielding)
    {
        if (shield == null)
        {
            return;
        }

        SyncShieldFacing();
        shield.SetShielding(isShielding);
    }

    private void SyncShieldFacing()
    {
        if (shield == null)
        {
            return;
        }

        var useMouseShieldFacing = IsShielding || _isMouseAttackHeld || _shieldActivatedFromMouseAttack;
        if (useMouseShieldFacing && TryGetMouseAimDirection(out var mouseAimDirection))
        {
            shield.SetFacing(mouseAimDirection);
            return;
        }

        shield.SetFacing(_facingDirection);
    }

    private static bool IsMouseAttackContext(InputAction.CallbackContext context)
    {
        return context.control?.device is Mouse;
    }

    private Vector2 ResolveAttackDirection(bool preferMouseAim)
    {
        if (preferMouseAim && TryGetMouseAimDirection(out var mouseAimDirection))
        {
            UpdateFacingDirection(mouseAimDirection);
            return mouseAimDirection;
        }

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

    private void UpdateMouseFacingDirection()
    {
        if (!TryGetMouseAimDirection(out var mouseAimDirection))
        {
            return;
        }

        UpdateFacingDirection(mouseAimDirection);
    }

    private bool TryGetMouseAimDirection(out Vector2 direction)
    {
        direction = Vector2.zero;

        if (Mouse.current == null)
        {
            return false;
        }

        if (aimCamera == null)
        {
            aimCamera = Camera.main;
        }

        if (aimCamera == null)
        {
            return false;
        }

        var screenPosition = Mouse.current.position.ReadValue();
        var cameraToPlaneDistance = Mathf.Abs(transform.position.z - aimCamera.transform.position.z);
        var worldPosition = aimCamera.ScreenToWorldPoint(new Vector3(screenPosition.x, screenPosition.y, cameraToPlaneDistance));
        var toMouse = (Vector2)worldPosition - (Vector2)transform.position;
        if (toMouse.sqrMagnitude <= 0.001f)
        {
            return false;
        }

        direction = toMouse.normalized;
        return true;
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
        SyncShieldFacing();
    }
}
}
