using System.Collections.Generic;
using _Game.Scripts.Combat;
using UnityEngine;

namespace _Game.Scripts
{
    [RequireComponent(typeof(PlayerMovementController))]
    [RequireComponent(typeof(Health2D))]
    [RequireComponent(typeof(Hurtbox2D))]
    public class PlayerCombatController : MonoBehaviour
    {
        [SerializeField] private PlayerMovementController playerMovement;
        [SerializeField] private Transform attackOrigin;
        [SerializeField] private LayerMask hittableLayers = ~0;
        [SerializeField, Min(0f)] private float attackOriginOffset = 0.25f;
        [SerializeField, Min(0f)] private float baseKnockbackForce = 4.5f;
        [SerializeField, Min(0f)] private float rushKnockbackForce = 7.25f;
        [SerializeField, Min(0f)] private float baseStunDuration = 0.1f;
        [SerializeField, Min(0f)] private float rushStunDuration = 0.2f;
        [Header("Projectile")]
        [SerializeField] private bool useVisibleProjectiles = true;
        [SerializeField, Min(0.1f)] private float projectileSpeed = 12f;
        [SerializeField] private Color projectileColor = new(0.15f, 1f, 1f, 0.95f);
        [SerializeField] private int projectileSortingOrder = 15;
        [SerializeField] private bool showAttackGizmos = true;

        private readonly HashSet<Health2D> _hitHealthTargets = new();
        private readonly RaycastHit2D[] _hitBuffer = new RaycastHit2D[16];
        private ContactFilter2D _contactFilter;
        private Vector2 _lastAttackOrigin;
        private Vector2 _lastAttackDirection = Vector2.right;
        private float _lastAttackRange = 1f;
        private float _lastAttackRadius = 0.35f;

        private void Awake()
        {
            if (playerMovement == null)
            {
                playerMovement = GetComponent<PlayerMovementController>();
            }

            ConfigureContactFilter();
        }

        private void OnValidate()
        {
            ConfigureContactFilter();
        }

        private void OnEnable()
        {
            if (playerMovement == null)
            {
                playerMovement = GetComponent<PlayerMovementController>();
            }

            if (playerMovement == null)
            {
                return;
            }

            playerMovement.AttackPerformed -= HandleAttackPerformed;
            playerMovement.AttackPerformed += HandleAttackPerformed;
        }

        private void OnDisable()
        {
            if (playerMovement == null)
            {
                return;
            }

            playerMovement.AttackPerformed -= HandleAttackPerformed;
        }

        private void HandleAttackPerformed(PlayerAttackData attackData)
        {
            if (playerMovement == null || playerMovement.IsShielding)
            {
                return;
            }

            var attackDirection = attackData.Direction.sqrMagnitude > 0.001f
                ? attackData.Direction.normalized
                : playerMovement != null && playerMovement.FacingDirection.sqrMagnitude > 0.001f
                    ? playerMovement.FacingDirection.normalized
                    : Vector2.right;

            var origin = attackOrigin != null ? (Vector2)attackOrigin.position : (Vector2)transform.position;
            origin += attackDirection * attackOriginOffset;

            _lastAttackOrigin = origin;
            _lastAttackDirection = attackDirection;
            _lastAttackRange = attackData.Range;
            _lastAttackRadius = attackData.Radius;
            _hitHealthTargets.Clear();

            var knockbackForce = attackData.Style == PlayerAttackStyle.Rush ? rushKnockbackForce : baseKnockbackForce;
            var stunDuration = attackData.Style == PlayerAttackStyle.Rush ? rushStunDuration : baseStunDuration;
            var damageInfo = new DamageInfo(gameObject, attackData.AttackId, attackData.Damage, attackDirection, origin, attackData.Signature, false, knockbackForce, stunDuration);

            if (useVisibleProjectiles)
            {
                SpawnProjectile(damageInfo, origin, attackDirection, attackData.Range, attackData.Radius);
                return;
            }

            var hitCount = Physics2D.CircleCast(origin, attackData.Radius, attackDirection, _contactFilter, _hitBuffer, attackData.Range);
            for (var i = 0; i < hitCount; i++)
            {
                var hit = _hitBuffer[i];
                var hurtbox = hit.collider.GetComponent<Hurtbox2D>() ?? hit.collider.GetComponentInParent<Hurtbox2D>();
                if (hurtbox == null || hurtbox.Health == null || !_hitHealthTargets.Add(hurtbox.Health))
                {
                    continue;
                }

                var hitDamageInfo = new DamageInfo(gameObject, attackData.AttackId, attackData.Damage, attackDirection, hit.point, attackData.Signature, false, knockbackForce, stunDuration);
                hurtbox.ApplyHit(hitDamageInfo);
            }
        }

        private void SpawnProjectile(DamageInfo damageInfo, Vector2 origin, Vector2 direction, float range, float radius)
        {
            var projectileObject = new GameObject($"PlayerProjectile_{damageInfo.AttackId}");
            projectileObject.transform.SetPositionAndRotation(origin, Quaternion.identity);
            var projectile = projectileObject.AddComponent<CombatProjectile2D>();
            projectile.Launch(
                damageInfo,
                origin,
                direction,
                projectileSpeed,
                range,
                radius,
                hittableLayers,
                transform,
                playerMovement != null ? playerMovement.GetComponent<Rigidbody2D>() : null,
                projectileColor,
                projectileSortingOrder);
        }

        private void ConfigureContactFilter()
        {
            _contactFilter.useLayerMask = true;
            _contactFilter.layerMask = hittableLayers;
            _contactFilter.useTriggers = true;
        }

        private void OnDrawGizmosSelected()
        {
            if (!showAttackGizmos)
            {
                return;
            }

            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(_lastAttackOrigin, _lastAttackRadius);
            Gizmos.DrawLine(_lastAttackOrigin, _lastAttackOrigin + _lastAttackDirection * _lastAttackRange);
            Gizmos.DrawWireSphere(_lastAttackOrigin + _lastAttackDirection * _lastAttackRange, _lastAttackRadius);
        }
    }
}
