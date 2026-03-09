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
    [SerializeField, Range(0f, 1f)] private float inputDeadZone = 0.05f;

    private PlayerInputActions _playerInputActions;
    private Vector2 _moveInput;

    public event Action InteractRequested;

    /// <summary>
    /// Raised when the player performs an attack. Subscribe to this event
    /// to record attacks in any observer system.
    /// </summary>
    public event Action<AttackType> AttackPerformed;

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

        var desiredVelocity = _moveInput * moveSpeed;
        rb.linearVelocity = desiredVelocity;
    }

    public void OnMove(InputAction.CallbackContext context)
    {
        var value = context.ReadValue<Vector2>();
        _moveInput = value.sqrMagnitude < inputDeadZone * inputDeadZone ? Vector2.zero : value.normalized;
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
    }

    public void OnAttack(InputAction.CallbackContext context)
    {
        if (!context.performed)
        {
            return;
        }

        AttackPerformed?.Invoke(AttackType.Primary);
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
    }
}
}
