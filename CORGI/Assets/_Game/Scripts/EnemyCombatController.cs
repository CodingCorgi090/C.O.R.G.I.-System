using System.Collections.Generic;
using _Game.Scripts.Combat;
using UnityEngine;

namespace _Game.Scripts
{
    [RequireComponent(typeof(EnemyController))]
    public class EnemyCombatController : MonoBehaviour
    {
        [SerializeField] private EnemyController enemyController;
        [SerializeField] private Transform attackOrigin;
        [SerializeField] private LayerMask hittableLayers = ~0;
        [SerializeField, Min(0f)] private float attackDamage = 10f;
        [SerializeField, Min(1f)] private float counterDamageMultiplier = 1.75f;
        [SerializeField, Min(0f)] private float attackRange = 1.1f;
        [SerializeField, Min(0f)] private float attackRadius = 0.4f;
        [SerializeField, Min(0f)] private float attackOriginOffset = 0.25f;
        [SerializeField, Min(0.05f)] private float attackCooldown = 0.8f;
        [SerializeField] private bool showAttackGizmos = true;

        private readonly HashSet<Health2D> _hitHealthTargets = new();
        private readonly RaycastHit2D[] _hitBuffer = new RaycastHit2D[16];
        private ContactFilter2D _contactFilter;
        private Vector2 _lastAttackOrigin;
        private Vector2 _lastAttackDirection = Vector2.left;
        private float _lastAttackTime = float.NegativeInfinity;

        private void Awake()
        {
            if (enemyController == null)
            {
                enemyController = GetComponent<EnemyController>();
            }

            ConfigureContactFilter();
        }

        private void OnValidate()
        {
            ConfigureContactFilter();
        }

        private void FixedUpdate()
        {
            if (enemyController == null || enemyController.TargetPlayer == null)
            {
                return;
            }

            if (Time.time < _lastAttackTime + attackCooldown)
            {
                return;
            }

            var baseOrigin = attackOrigin != null ? (Vector2)attackOrigin.position : (Vector2)transform.position;
            var toPlayer = (Vector2)enemyController.TargetPlayer.transform.position - baseOrigin;
            if (toPlayer.sqrMagnitude > Mathf.Pow(attackRange + attackOriginOffset, 2f))
            {
                return;
            }

            PerformAttack(toPlayer.sqrMagnitude > 0.001f ? toPlayer.normalized : Vector2.left);
        }

        private void PerformAttack(Vector2 direction)
        {
            var origin = attackOrigin != null ? (Vector2)attackOrigin.position : (Vector2)transform.position;
            origin += direction * attackOriginOffset;

            _lastAttackOrigin = origin;
            _lastAttackDirection = direction;
            _lastAttackTime = Time.time;
            _hitHealthTargets.Clear();

            var damageAmount = attackDamage * (enemyController.IsCountering ? counterDamageMultiplier : 1f);
            var hitCount = Physics2D.CircleCast(origin, attackRadius, direction, _contactFilter, _hitBuffer, attackRange);
            for (var i = 0; i < hitCount; i++)
            {
                var hit = _hitBuffer[i];
                var hurtbox = hit.collider.GetComponent<Hurtbox2D>() ?? hit.collider.GetComponentInParent<Hurtbox2D>();
                if (hurtbox == null || hurtbox.Health == null || !_hitHealthTargets.Add(hurtbox.Health))
                {
                    continue;
                }

                var attackSignature = enemyController.IsCountering
                    ? $"EnemyCounter:{enemyController.LearnedAttackSignature}"
                    : $"EnemyAttack:{enemyController.DebugTactic}";
                var damageInfo = new DamageInfo(gameObject, -1, damageAmount, direction, hit.point, attackSignature, enemyController.IsCountering);
                hurtbox.ApplyHit(damageInfo);
            }
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
            Gizmos.DrawWireSphere(_lastAttackOrigin, attackRadius);
            Gizmos.DrawLine(_lastAttackOrigin, _lastAttackOrigin + _lastAttackDirection * attackRange);
            Gizmos.DrawWireSphere(_lastAttackOrigin + _lastAttackDirection * attackRange, attackRadius);
        }
    }
}
