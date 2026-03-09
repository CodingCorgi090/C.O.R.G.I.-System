using System;
using System.Collections.Generic;
using UnityEngine;

namespace _Game.Scripts
{
    /// <summary>
    /// Snapshot of player state recorded at a point in time.
    /// </summary>
    public readonly struct BehaviorSample
    {
        public readonly Vector2 Position;
        public readonly Vector2 Velocity;
        public readonly float Timestamp;

        public BehaviorSample(Vector2 position, Vector2 velocity, float timestamp)
        {
            Position = position;
            Velocity = velocity;
            Timestamp = timestamp;
        }
    }

    /// <summary>
    /// Attached to the Player. Continuously records movement samples and attack events,
    /// and exposes a query API that EnemyController uses to learn and adapt.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public class PlayerBehaviorTracker : MonoBehaviour
    {
        [Header("Tracking Settings")]
        [Tooltip("Maximum number of position samples kept in the rolling history.")]
        [SerializeField, Min(1)] private int maxPositionHistory = 120;

        [Tooltip("How often (in seconds) a new position sample is recorded.")]
        [SerializeField, Min(0f)] private float sampleInterval = 0.1f;

        private Rigidbody2D _rb;
        private float _nextSampleTime;

        // Rolling position/velocity history (oldest sample dequeued when full)
        private readonly Queue<BehaviorSample> _positionHistory = new Queue<BehaviorSample>();

        // How many times each attack type has been used this encounter
        private readonly Dictionary<AttackType, int> _attackCounts = new Dictionary<AttackType, int>();

        // Per-enemy orbit tracking (key = enemy GetInstanceID())
        // Stores the last known polar angle so we can compute the signed delta each frame
        private readonly Dictionary<int, float> _lastOrbitAngles = new Dictionary<int, float>();
        // Accumulates signed angle change — positive = CCW, negative = CW
        private readonly Dictionary<int, float> _cumulativeOrbitAngles = new Dictionary<int, float>();

        /// <summary>Raised whenever the player performs any attack.</summary>
        public event Action<AttackType> AttackPerformed;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();

            // Initialize attack counters for every defined type
            foreach (AttackType t in Enum.GetValues(typeof(AttackType)))
            {
                _attackCounts[t] = 0;
            }

            // Wire up to PlayerMovementController on the same GameObject
            var controller = GetComponent<PlayerMovementController>();
            if (controller != null)
            {
                controller.AttackPerformed += RecordAttack;
            }
        }

        private void OnDestroy()
        {
            var controller = GetComponent<PlayerMovementController>();
            if (controller != null)
            {
                controller.AttackPerformed -= RecordAttack;
            }
        }

        private void FixedUpdate()
        {
            if (Time.time < _nextSampleTime)
            {
                return;
            }

            _nextSampleTime = Time.time + sampleInterval;
            RecordSample();
        }

        // ── Recording ────────────────────────────────────────────────────────

        private void RecordSample()
        {
            var sample = new BehaviorSample(
                (Vector2)transform.position,
                _rb.linearVelocity,
                Time.time);

            _positionHistory.Enqueue(sample);

            while (_positionHistory.Count > maxPositionHistory)
            {
                _positionHistory.Dequeue();
            }
        }

        /// <summary>
        /// Records that the player used <paramref name="attackType"/> and fires the
        /// <see cref="AttackPerformed"/> event so enemies can react immediately.
        /// </summary>
        public void RecordAttack(AttackType attackType)
        {
            _attackCounts[attackType]++;
            AttackPerformed?.Invoke(attackType);
        }

        // ── Query API (used by EnemyController) ──────────────────────────────

        /// <summary>Returns the current world position of the player.</summary>
        public Vector2 GetPosition() => (Vector2)transform.position;

        /// <summary>Returns the current velocity of the player.</summary>
        public Vector2 GetVelocity() => _rb.linearVelocity;

        /// <summary>Returns the attack that has been used most often this encounter.</summary>
        public AttackType GetMostUsedAttack()
        {
            var best = AttackType.Primary;
            var bestCount = -1;
            foreach (var kvp in _attackCounts)
            {
                if (kvp.Value > bestCount)
                {
                    bestCount = kvp.Value;
                    best = kvp.Key;
                }
            }
            return best;
        }

        /// <summary>Returns how many times <paramref name="attackType"/> has been used.</summary>
        public int GetAttackCount(AttackType attackType) => _attackCounts[attackType];

        /// <summary>Returns a read-only view of the rolling position history.</summary>
        public IReadOnlyCollection<BehaviorSample> GetPositionHistory() => _positionHistory;

        // ── Orbit tracking ───────────────────────────────────────────────────

        /// <summary>
        /// Should be called by an <see cref="EnemyController"/> every frame.
        /// Accumulates the signed polar-angle change of the player around
        /// <paramref name="enemyPosition"/> and returns the total so far.
        /// Positive values = counter-clockwise orbiting; negative = clockwise.
        /// </summary>
        public float UpdateOrbitAngle(int enemyInstanceId, Vector2 enemyPosition)
        {
            var playerPos = (Vector2)transform.position;
            var currentAngle = Mathf.Atan2(
                playerPos.y - enemyPosition.y,
                playerPos.x - enemyPosition.x) * Mathf.Rad2Deg;

            if (!_lastOrbitAngles.ContainsKey(enemyInstanceId))
            {
                _lastOrbitAngles[enemyInstanceId] = currentAngle;
                _cumulativeOrbitAngles[enemyInstanceId] = 0f;
                return 0f;
            }

            var delta = Mathf.DeltaAngle(_lastOrbitAngles[enemyInstanceId], currentAngle);
            _cumulativeOrbitAngles[enemyInstanceId] += delta;
            _lastOrbitAngles[enemyInstanceId] = currentAngle;

            return _cumulativeOrbitAngles[enemyInstanceId];
        }

        /// <summary>Returns the accumulated orbit angle around <paramref name="enemyInstanceId"/>.</summary>
        public float GetCumulativeOrbitAngle(int enemyInstanceId)
        {
            return _cumulativeOrbitAngles.TryGetValue(enemyInstanceId, out var angle) ? angle : 0f;
        }

        /// <summary>
        /// Resets orbit tracking for a specific enemy.
        /// Call this when the enemy resets its intercept attempt or is destroyed.
        /// </summary>
        public void ResetOrbitTracking(int enemyInstanceId)
        {
            _cumulativeOrbitAngles.Remove(enemyInstanceId);
            _lastOrbitAngles.Remove(enemyInstanceId);
        }

        /// <summary>Resets all attack counts and orbit data (e.g., on new encounter).</summary>
        public void ResetAllTracking()
        {
            foreach (AttackType t in Enum.GetValues(typeof(AttackType)))
            {
                _attackCounts[t] = 0;
            }
            _cumulativeOrbitAngles.Clear();
            _lastOrbitAngles.Clear();
            _positionHistory.Clear();
        }
    }
}
