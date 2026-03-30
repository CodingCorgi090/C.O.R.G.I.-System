using UnityEngine;

namespace _Game.Scripts.Combat
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Health2D))]
    public class HitReaction2D : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Health2D health;
        [SerializeField] private Rigidbody2D rb;

        [Header("Reaction")]
        [SerializeField, Min(0f)] private float defaultKnockbackForce = 4f;
        [SerializeField, Min(0f)] private float counterKnockbackMultiplier = 1.35f;
        [SerializeField, Min(0f)] private float defaultStunDuration = 0.12f;
        [SerializeField, Min(0f)] private float counterStunMultiplier = 1.5f;
        [SerializeField, Min(0f)] private float knockbackDecay = 24f;
        [SerializeField, Range(0f, 1f)] private float movementWhileStunnedMultiplier;
        [SerializeField] private bool clearVelocityOnDeath = true;

        private Vector2 _reactionVelocity;
        private float _stunnedUntilTime;

        public bool IsMovementLocked => Time.time < _stunnedUntilTime && movementWhileStunnedMultiplier <= 0.001f;
        public bool IsActionLocked => Time.time < _stunnedUntilTime;
        public bool IsReacting => _reactionVelocity.sqrMagnitude > 0.001f || IsActionLocked;
        public Vector2 ReactionVelocity => _reactionVelocity;
        public float StunRemaining => Mathf.Max(0f, _stunnedUntilTime - Time.time);

        private void Awake()
        {
            if (health == null)
            {
                health = GetComponent<Health2D>();
            }

            if (rb == null)
            {
                rb = GetComponent<Rigidbody2D>();
            }
        }

        private void OnEnable()
        {
            if (health == null)
            {
                health = GetComponent<Health2D>();
            }

            if (health == null)
            {
                return;
            }

            health.Damaged -= HandleDamaged;
            health.Damaged += HandleDamaged;
            health.Died -= HandleDied;
            health.Died += HandleDied;
        }

        private void OnDisable()
        {
            if (health != null)
            {
                health.Damaged -= HandleDamaged;
                health.Died -= HandleDied;
            }

            _reactionVelocity = Vector2.zero;
            _stunnedUntilTime = 0f;
        }

        private void FixedUpdate()
        {
            if (_reactionVelocity.sqrMagnitude <= 0.0001f)
            {
                _reactionVelocity = Vector2.zero;
                return;
            }

            _reactionVelocity = Vector2.MoveTowards(_reactionVelocity, Vector2.zero, knockbackDecay * Time.fixedDeltaTime);
        }

        public Vector2 GetModifiedVelocity(Vector2 desiredVelocity)
        {
            if (Time.time < _stunnedUntilTime)
            {
                desiredVelocity *= movementWhileStunnedMultiplier;
            }

            return desiredVelocity + _reactionVelocity;
        }

        public void ClearReaction()
        {
            _reactionVelocity = Vector2.zero;
            _stunnedUntilTime = 0f;

            if (rb != null)
            {
                rb.linearVelocity = Vector2.zero;
            }
        }

        public void ApplyReaction(Vector2 direction, float knockbackForce, float stunDuration)
        {
            if (direction.sqrMagnitude > 0.001f && knockbackForce > 0f)
            {
                _reactionVelocity = direction.normalized * knockbackForce;
            }

            if (stunDuration > 0f)
            {
                _stunnedUntilTime = Mathf.Max(_stunnedUntilTime, Time.time + stunDuration);
            }
        }

        private void HandleDamaged(DamageInfo damageInfo, float previousHealth, float currentHealth)
        {
            var hitDirection = ResolveHitDirection(damageInfo);
            var knockback = damageInfo.KnockbackForce > 0f ? damageInfo.KnockbackForce : defaultKnockbackForce;
            var stunDuration = damageInfo.StunDuration > 0f ? damageInfo.StunDuration : defaultStunDuration;

            if (damageInfo.IsCounterAttack)
            {
                knockback *= counterKnockbackMultiplier;
                stunDuration *= counterStunMultiplier;
            }

            ApplyReaction(hitDirection, knockback, stunDuration);
        }

        private void HandleDied(DamageInfo damageInfo)
        {
            if (!clearVelocityOnDeath)
            {
                return;
            }

            ClearReaction();
        }

        private Vector2 ResolveHitDirection(DamageInfo damageInfo)
        {
            if (damageInfo.Direction.sqrMagnitude > 0.001f)
            {
                return damageInfo.Direction.normalized;
            }

            if (damageInfo.Source != null)
            {
                var delta = (Vector2)transform.position - (Vector2)damageInfo.Source.transform.position;
                if (delta.sqrMagnitude > 0.001f)
                {
                    return delta.normalized;
                }
            }

            var pointDelta = (Vector2)transform.position - damageInfo.Point;
            return pointDelta.sqrMagnitude > 0.001f ? pointDelta.normalized : Vector2.zero;
        }
    }
}

