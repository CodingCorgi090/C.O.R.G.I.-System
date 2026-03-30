using UnityEngine;

namespace _Game.Scripts.Combat
{
    [DisallowMultipleComponent]
    public class Shield2D : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private HitReaction2D hitReaction;

        [Header("Blocking")]
        [SerializeField, Range(10f, 180f)] private float blockArcDegrees = 140f;
        [SerializeField, Range(0f, 1f)] private float blockedDamageMultiplier;
        [SerializeField, Range(0f, 1f)] private float blockedKnockbackMultiplier = 0.15f;
        [SerializeField, Range(0f, 1f)] private float blockedStunMultiplier;
        [SerializeField, Range(0.05f, 1f)] private float movementMultiplierWhileShielding = 0.45f;

        [Header("Stamina")]
        [SerializeField, Min(1f)] private float maxStamina = 100f;
        [SerializeField, Min(0f)] private float minimumStaminaToRaise = 15f;
        [SerializeField, Min(0f)] private float staminaDrainPerSecond = 12f;
        [SerializeField, Min(0f)] private float staminaRecoveryPerSecond = 22f;
        [SerializeField, Min(0f)] private float flatBlockedHitStaminaCost = 8f;
        [SerializeField, Min(0f)] private float blockedDamageCostMultiplier = 1f;
        [SerializeField, Min(0f)] private float blockedKnockbackCostMultiplier = 2.5f;
        [SerializeField, Min(0f)] private float blockedStunCostMultiplier = 35f;

        [Header("Guard Break")]
        [SerializeField, Min(0f)] private float guardBreakDuration = 1.1f;
        [SerializeField, Min(0f)] private float guardBreakKnockbackForce = 2.5f;
        [SerializeField, Min(0f)] private float guardBreakStunDuration = 0.25f;
        [SerializeField] private bool showGizmos = true;

        private Vector2 _facingDirection = Vector2.right;
        private float _currentStamina;
        private float _guardBrokenUntilTime;
        private bool _isShielding;

        public bool IsShielding => _isShielding;
        public bool IsGuardBroken => Time.time < _guardBrokenUntilTime;
        public bool CanShield => !IsGuardBroken && _currentStamina >= minimumStaminaToRaise;
        public float BlockArcDegrees => blockArcDegrees;
        public Vector2 FacingDirection => _facingDirection;
        public float CurrentStamina => _currentStamina;
        public float MaxStamina => maxStamina;
        public float StaminaNormalized => maxStamina > 0.001f ? _currentStamina / maxStamina : 0f;
        public float MovementMultiplier => _isShielding ? movementMultiplierWhileShielding : 1f;

        private void Awake()
        {
            if (hitReaction == null)
            {
                hitReaction = GetComponent<HitReaction2D>();
            }

            if (GetComponent<ShieldVisual2D>() == null)
            {
                gameObject.AddComponent<ShieldVisual2D>();
            }

            ResetShield();
        }

        private void OnEnable()
        {
            ResetShield();
        }

        private void Update()
        {
            if (_isShielding)
            {
                if (!CanShield)
                {
                    _isShielding = false;
                    return;
                }

                ConsumeStamina(staminaDrainPerSecond * Time.deltaTime);
                return;
            }

            if (IsGuardBroken && Time.time < _guardBrokenUntilTime)
            {
                return;
            }

            RecoverStamina(staminaRecoveryPerSecond * Time.deltaTime);
        }

        public void SetShielding(bool isShielding)
        {
            _isShielding = isShielding && CanShield;
        }

        public void SetFacing(Vector2 facingDirection)
        {
            if (facingDirection.sqrMagnitude <= 0.001f)
            {
                return;
            }

            _facingDirection = facingDirection.normalized;
        }

        public bool TryHandleHit(DamageInfo damageInfo, out DamageInfo resolvedDamageInfo, out bool fullyBlocked)
        {
            resolvedDamageInfo = damageInfo;
            fullyBlocked = false;

            if (!_isShielding)
            {
                return false;
            }

            var threatDirection = ResolveThreatDirection(damageInfo);
            if (threatDirection.sqrMagnitude <= 0.001f)
            {
                return false;
            }

            var facingDot = Vector2.Dot(_facingDirection, threatDirection);
            var minimumDot = Mathf.Cos(blockArcDegrees * 0.5f * Mathf.Deg2Rad);
            if (facingDot < minimumDot)
            {
                return false;
            }

            var blockedDamage = damageInfo.Amount * blockedDamageMultiplier;
            var blockedKnockback = damageInfo.KnockbackForce * blockedKnockbackMultiplier;
            var blockedStun = damageInfo.StunDuration * blockedStunMultiplier;
            var staminaCost = flatBlockedHitStaminaCost
                + damageInfo.Amount * blockedDamageCostMultiplier
                + damageInfo.KnockbackForce * blockedKnockbackCostMultiplier
                + damageInfo.StunDuration * blockedStunCostMultiplier;

            if (staminaCost > 0f)
            {
                ConsumeStamina(staminaCost, -threatDirection, true);
            }

            resolvedDamageInfo = new DamageInfo(
                damageInfo.Source,
                damageInfo.AttackId,
                blockedDamage,
                damageInfo.Direction,
                damageInfo.Point,
                damageInfo.AttackSignature,
                damageInfo.IsCounterAttack,
                blockedKnockback,
                blockedStun);

            fullyBlocked = blockedDamage <= 0.001f && blockedKnockback <= 0.001f && blockedStun <= 0.001f;
            return true;
        }

        [ContextMenu("Reset Shield")]
        public void ResetShield()
        {
            _currentStamina = maxStamina;
            _guardBrokenUntilTime = 0f;
            _isShielding = false;
        }

        private void RecoverStamina(float amount)
        {
            if (amount <= 0f)
            {
                return;
            }

            _currentStamina = Mathf.Min(maxStamina, _currentStamina + amount);
        }

        private void ConsumeStamina(float amount, Vector2 guardBreakDirection = default, bool applyGuardBreakReaction = false)
        {
            if (amount <= 0f)
            {
                return;
            }

            _currentStamina = Mathf.Max(0f, _currentStamina - amount);
            if (_currentStamina > 0f || IsGuardBroken)
            {
                return;
            }

            BreakGuard(guardBreakDirection, applyGuardBreakReaction);
        }

        private void BreakGuard(Vector2 guardBreakDirection, bool applyGuardBreakReaction)
        {
            _isShielding = false;
            _guardBrokenUntilTime = Time.time + guardBreakDuration;

            if (!applyGuardBreakReaction || hitReaction == null)
            {
                return;
            }

            hitReaction.ApplyReaction(guardBreakDirection, guardBreakKnockbackForce, guardBreakStunDuration);
        }

        private Vector2 ResolveThreatDirection(DamageInfo damageInfo)
        {
            var selfPosition = (Vector2)transform.position;

            if (damageInfo.Source != null)
            {
                var sourceDelta = (Vector2)damageInfo.Source.transform.position - selfPosition;
                if (sourceDelta.sqrMagnitude > 0.001f)
                {
                    return sourceDelta.normalized;
                }
            }

            var pointDelta = damageInfo.Point - selfPosition;
            if (pointDelta.sqrMagnitude > 0.001f)
            {
                return pointDelta.normalized;
            }

            return damageInfo.Direction.sqrMagnitude > 0.001f
                ? (-damageInfo.Direction).normalized
                : Vector2.zero;
        }

        private void OnDrawGizmosSelected()
        {
            if (!showGizmos)
            {
                return;
            }

            Gizmos.color = IsGuardBroken ? Color.red : _isShielding ? Color.cyan : Color.gray;
            var origin = transform.position;
            Gizmos.DrawLine(origin, origin + (Vector3)(_facingDirection * 0.9f));

            var halfArc = blockArcDegrees * 0.5f;
            var left = Rotate(_facingDirection, -halfArc);
            var right = Rotate(_facingDirection, halfArc);
            Gizmos.DrawLine(origin, origin + (Vector3)(left * 0.8f));
            Gizmos.DrawLine(origin, origin + (Vector3)(right * 0.8f));
        }

        private static Vector2 Rotate(Vector2 vector, float degrees)
        {
            var radians = degrees * Mathf.Deg2Rad;
            var sin = Mathf.Sin(radians);
            var cos = Mathf.Cos(radians);
            return new Vector2(vector.x * cos - vector.y * sin, vector.x * sin + vector.y * cos);
        }
    }
}

