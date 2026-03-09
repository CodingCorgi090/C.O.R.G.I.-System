using System.Collections.Generic;
using UnityEngine;

namespace _Game.Scripts
{
    /// <summary>
    /// States the enemy can be in at any given moment.
    /// </summary>
    public enum EnemyState
    {
        /// <summary>Player is out of detection range; enemy stands still.</summary>
        Idle,

        /// <summary>Enemy moves directly toward the player.</summary>
        Chase,

        /// <summary>Enemy predicts the player's movement route and moves to cut them off.</summary>
        Intercept,

        /// <summary>Enemy is within melee/attack range and executes an attack.</summary>
        Attack,

        /// <summary>Enemy has learned the player's favourite attack and performs a counter.</summary>
        CounterAttack,

        /// <summary>Enemy is briefly staggered and cannot act.</summary>
        Stagger
    }

    /// <summary>
    /// Intelligent 2D enemy AI that observes player behaviour through
    /// <see cref="PlayerBehaviorTracker"/> and adapts its strategy over time.
    ///
    /// Learning behaviours:
    /// - <b>Orbit / flanking</b>: once the player completes enough loops around this
    ///   enemy the AI switches to <see cref="EnemyState.Intercept"/> and moves to
    ///   cut the player off instead of blindly chasing.
    /// - <b>Attack patterns</b>: each time the player uses an attack the enemy's
    ///   counter-weight for that attack increases. Once the weight crosses the
    ///   configured threshold the enemy enters <see cref="EnemyState.CounterAttack"/>
    ///   and executes a type-specific counter response.
    ///
    /// Movement is driven entirely by <see cref="Rigidbody2D"/> — no NavMesh is used.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public class EnemyController : MonoBehaviour
    {
        // ── Inspector ─────────────────────────────────────────────────────────

        [Header("References")]
        [Tooltip("Rigidbody2D used for all movement. Auto-resolved if left empty.")]
        [SerializeField] private Rigidbody2D rb;

        [Tooltip("PlayerBehaviorTracker attached to the player GameObject.")]
        [SerializeField] private PlayerBehaviorTracker playerTracker;

        [Header("Movement")]
        [Tooltip("Maximum movement speed in units per second.")]
        [SerializeField, Min(0f)] private float moveSpeed = 3f;

        [Tooltip("Distance at which the enemy begins chasing the player.")]
        [SerializeField, Min(0f)] private float detectionRange = 8f;

        [Tooltip("Distance at which the enemy switches to its attack state.")]
        [SerializeField, Min(0f)] private float attackRange = 1.5f;

        [Tooltip("How many seconds ahead the AI predicts the player's position when computing an intercept point.")]
        [SerializeField, Min(0f)] private float interceptLookAheadTime = 1.2f;

        [Header("Learning — Orbit Detection")]
        [Tooltip("Number of full orbits (360 °) the player must complete around this enemy before the AI tries to intercept.")]
        [SerializeField, Min(1)] private int orbitDetectionThreshold = 2;

        [Header("Learning — Attack Countering")]
        [Tooltip("Number of times the player must use an attack before the enemy actively counters it.")]
        [SerializeField, Min(1)] private int attackPatternThreshold = 3;

        [Tooltip("How quickly counter-weights decay per second when the player stops using that attack. Prevents the enemy from fixating forever.")]
        [SerializeField, Range(0f, 10f)] private float counterWeightDecayRate = 0.5f;

        // ── Runtime state ─────────────────────────────────────────────────────

        // Per-attack-type confidence that the player will use that attack again
        private readonly Dictionary<AttackType, float> _counterWeights =
            new Dictionary<AttackType, float>();

        // Cumulative orbit tracking (in degrees) - positive = CCW, negative = CW
        private float _lastTrackedOrbitAngle;
        private int _ccwOrbitCount;
        private int _cwOrbitCount;

        private EnemyState _currentState = EnemyState.Idle;
        private Vector2 _interceptTarget;
        private AttackType _pendingCounter;

        private int _instanceId;

        // ── Unity lifecycle ───────────────────────────────────────────────────

        private void Awake()
        {
            _instanceId = GetInstanceID();

            if (rb == null)
            {
                rb = GetComponent<Rigidbody2D>();
            }

            foreach (AttackType t in System.Enum.GetValues(typeof(AttackType)))
            {
                _counterWeights[t] = 0f;
            }
        }

        private void OnEnable()
        {
            if (playerTracker != null)
            {
                playerTracker.AttackPerformed += OnPlayerAttacked;
            }
        }

        private void OnDisable()
        {
            if (playerTracker != null)
            {
                playerTracker.AttackPerformed -= OnPlayerAttacked;
            }
        }

        private void Update()
        {
            if (playerTracker == null)
            {
                return;
            }

            DecayCounterWeights();
            UpdateOrbitLearning();
            UpdateState();
        }

        private void FixedUpdate()
        {
            if (rb == null || playerTracker == null)
            {
                return;
            }

            ExecuteMovement();
        }

        // ── Learning ──────────────────────────────────────────────────────────

        /// <summary>
        /// Called whenever the player performs an attack.
        /// Increases the counter-weight for that attack type.
        /// </summary>
        private void OnPlayerAttacked(AttackType attackType)
        {
            _counterWeights[attackType] += 1f;
        }

        /// <summary>
        /// Slowly reduces all counter-weights over time so that if the player
        /// switches strategy the enemy gradually stops expecting the old one.
        /// </summary>
        private void DecayCounterWeights()
        {
            foreach (var key in new List<AttackType>(_counterWeights.Keys))
            {
                _counterWeights[key] = Mathf.Max(0f,
                    _counterWeights[key] - counterWeightDecayRate * Time.deltaTime);
            }
        }

        /// <summary>
        /// Asks <see cref="PlayerBehaviorTracker"/> for the latest orbit angle and
        /// increments orbit counters whenever a full 360 ° loop is completed.
        /// </summary>
        private void UpdateOrbitLearning()
        {
            var currentOrbit = playerTracker.UpdateOrbitAngle(_instanceId, transform.position);

            var gained = currentOrbit - _lastTrackedOrbitAngle;

            if (gained >= 360f)
            {
                _ccwOrbitCount++;
                _lastTrackedOrbitAngle = currentOrbit;
            }
            else if (gained <= -360f)
            {
                _cwOrbitCount++;
                _lastTrackedOrbitAngle = currentOrbit;
            }
        }

        // ── State machine ─────────────────────────────────────────────────────

        private void UpdateState()
        {
            var distanceToPlayer = Vector2.Distance(
                transform.position, playerTracker.GetPosition());

            // Out of range — go idle
            if (distanceToPlayer > detectionRange)
            {
                TransitionTo(EnemyState.Idle);
                return;
            }

            // Counter a known attack pattern (highest priority while in range)
            if (ShouldCounterAttack(out _pendingCounter))
            {
                TransitionTo(EnemyState.CounterAttack);
                return;
            }

            // Intercept if player has been orbiting
            if (ShouldIntercept())
            {
                _interceptTarget = ComputeInterceptPoint();
                TransitionTo(EnemyState.Intercept);
                return;
            }

            // Close enough to attack
            if (distanceToPlayer <= attackRange)
            {
                TransitionTo(EnemyState.Attack);
                return;
            }

            // Default: chase
            TransitionTo(EnemyState.Chase);
        }

        /// <summary>
        /// Returns true if the player has used some attack enough times
        /// AND the enemy's confidence weight for it is still above zero.
        /// Outputs the attack type to counter.
        /// </summary>
        private bool ShouldCounterAttack(out AttackType target)
        {
            target = AttackType.Primary;
            var bestWeight = 0f;

            foreach (var kvp in _counterWeights)
            {
                if (kvp.Value > bestWeight &&
                    playerTracker.GetAttackCount(kvp.Key) >= attackPatternThreshold)
                {
                    bestWeight = kvp.Value;
                    target = kvp.Key;
                }
            }

            return bestWeight > 0f;
        }

        /// <summary>Returns true once the player has completed enough orbits.</summary>
        private bool ShouldIntercept()
        {
            return _ccwOrbitCount >= orbitDetectionThreshold
                || _cwOrbitCount >= orbitDetectionThreshold;
        }

        /// <summary>
        /// Computes the world-space point the enemy should move toward in order to
        /// intercept the player's predicted trajectory.
        ///
        /// Strategy:
        /// 1. Linearly predict the player's future position based on current velocity.
        /// 2. Offset the target perpendicular to the player–enemy axis in the same
        ///    direction as the player's dominant orbit, so the enemy steps in front of
        ///    the player's circular path rather than chasing its tail.
        /// </summary>
        private Vector2 ComputeInterceptPoint()
        {
            var playerPos = playerTracker.GetPosition();
            var playerVel = playerTracker.GetVelocity();

            // Linear prediction of where the player will be
            var predictedPos = playerPos + playerVel * interceptLookAheadTime;

            // Determine dominant orbit direction (CCW = +1, CW = -1)
            var orbitSign = _ccwOrbitCount >= _cwOrbitCount ? 1f : -1f;

            // Perpendicular direction relative to the player–enemy vector
            var toPlayer = (playerPos - (Vector2)transform.position).normalized;
            var perpendicular = new Vector2(-toPlayer.y, toPlayer.x) * orbitSign;

            // Blend linear prediction with the perpendicular intercept position
            return Vector2.Lerp(
                predictedPos,
                (Vector2)transform.position + perpendicular * attackRange,
                0.5f);
        }

        private void TransitionTo(EnemyState newState)
        {
            _currentState = newState;
        }

        // ── Movement execution ────────────────────────────────────────────────

        private void ExecuteMovement()
        {
            switch (_currentState)
            {
                case EnemyState.Idle:
                    rb.linearVelocity = Vector2.zero;
                    break;

                case EnemyState.Chase:
                    MoveToward(playerTracker.GetPosition());
                    break;

                case EnemyState.Intercept:
                    MoveToward(_interceptTarget);
                    // Once the intercept point is reached, reset orbit data and resume chase
                    if (Vector2.Distance(transform.position, _interceptTarget) < 0.3f)
                    {
                        ResetOrbitCounts();
                    }
                    break;

                case EnemyState.Attack:
                    rb.linearVelocity = Vector2.zero;
                    PerformAttack();
                    break;

                case EnemyState.CounterAttack:
                    rb.linearVelocity = Vector2.zero;
                    PerformCounter(_pendingCounter);
                    break;

                case EnemyState.Stagger:
                    rb.linearVelocity = Vector2.zero;
                    break;
            }
        }

        /// <summary>Steers the enemy toward <paramref name="target"/> at full move speed.</summary>
        private void MoveToward(Vector2 target)
        {
            var direction = (target - (Vector2)transform.position).normalized;
            rb.linearVelocity = direction * moveSpeed;
        }

        /// <summary>Resets orbit counters and clears per-enemy orbit data on the tracker.</summary>
        private void ResetOrbitCounts()
        {
            _ccwOrbitCount = 0;
            _cwOrbitCount = 0;
            _lastTrackedOrbitAngle = playerTracker.GetCumulativeOrbitAngle(_instanceId);
            playerTracker.ResetOrbitTracking(_instanceId);
        }

        // ── Overridable attack hooks ───────────────────────────────────────────

        /// <summary>
        /// Called when the enemy enters <see cref="EnemyState.Attack"/>.
        /// Override in a subclass to trigger animations, hitboxes, or damage logic.
        /// </summary>
        protected virtual void PerformAttack()
        {
        }

        /// <summary>
        /// Called when the enemy enters <see cref="EnemyState.CounterAttack"/>.
        /// <paramref name="counterTo"/> indicates which player attack is being countered.
        /// Override in a subclass to trigger the appropriate counter animation or response.
        /// </summary>
        protected virtual void PerformCounter(AttackType counterTo)
        {
        }

        /// <summary>
        /// Puts the enemy into the stagger state for <paramref name="duration"/> seconds.
        /// Call this from external hit-detection code when the enemy is struck.
        /// </summary>
        public void ApplyStagger(float duration)
        {
            TransitionTo(EnemyState.Stagger);
            CancelInvoke(nameof(RecoverFromStagger));
            Invoke(nameof(RecoverFromStagger), duration);
        }

        private void RecoverFromStagger()
        {
            if (_currentState == EnemyState.Stagger)
            {
                TransitionTo(EnemyState.Chase);
            }
        }

        // ── Public state accessors ────────────────────────────────────────────

        /// <summary>Returns the enemy's current AI state.</summary>
        public EnemyState CurrentState => _currentState;

        /// <summary>Returns how many counter-clockwise orbits the player has completed around this enemy.</summary>
        public int CCWOrbitCount => _ccwOrbitCount;

        /// <summary>Returns how many clockwise orbits the player has completed around this enemy.</summary>
        public int CWOrbitCount => _cwOrbitCount;

        /// <summary>Returns the current counter-weight for <paramref name="attackType"/>.</summary>
        public float GetCounterWeight(AttackType attackType) =>
            _counterWeights.TryGetValue(attackType, out var w) ? w : 0f;

        // ── Editor debug ──────────────────────────────────────────────────────

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            // Detection range
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, detectionRange);

            // Attack range
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, attackRange);

            // Intercept target
            if (_currentState == EnemyState.Intercept)
            {
                Gizmos.color = Color.cyan;
                Gizmos.DrawSphere(_interceptTarget, 0.2f);
                Gizmos.DrawLine(transform.position, _interceptTarget);
            }

            // State and orbit debug label
            UnityEditor.Handles.Label(
                transform.position + Vector3.up * 1.8f,
                $"State: {_currentState}\nCCW orbits: {_ccwOrbitCount}  CW orbits: {_cwOrbitCount}");
        }
#endif
    }
}
