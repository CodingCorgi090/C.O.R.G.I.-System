using System.Collections.Generic;
using _Game.Scripts.Combat;
using _Game.Scripts.Persistence;
using UnityEngine;

namespace _Game.Scripts
{
    [RequireComponent(typeof(Health2D))]
    [RequireComponent(typeof(Hurtbox2D))]
    public class EnemyController : MonoBehaviour
    {
        private enum EnemyTactic
        {
            Hold,
            Pressure,
            CutOffClockwise,
            CutOffCounterClockwise,
            CounterAttack
        }

        private struct PlayerSample
        {
            public float Time;
            public Vector2 RelativePosition;
            public Vector2 MoveDirection;
            public float OrbitContribution;
        }

        [Header("References")]
        [SerializeField] private Rigidbody2D rb;
        [SerializeField] private PlayerMovementController targetPlayer;

        [Header("Movement")]
        [SerializeField, Min(0f)] private float moveSpeed = 4.5f;
        [SerializeField, Min(0f)] private float counterMoveSpeed = 6f;
        [SerializeField, Min(0f)] private float stoppingDistance = 1.35f;
        [SerializeField, Min(0f)] private float cutOffLeadDistance = 1.75f;
        [SerializeField, Min(0f)] private float predictionTime = 0.4f;
        [SerializeField, Min(0f)] private float dodgeDistance = 2.25f;
        [SerializeField, Min(0f)] private float reacquireDistance = 14f;

        [Header("Learning")]
        [SerializeField, Min(0.05f)] private float sampleInterval = 0.12f;
        [SerializeField, Min(0.25f)] private float memoryDuration = 4f;
        [SerializeField, Range(0f, 1f)] private float orbitDetectionThreshold = 0.45f;
        [SerializeField, Min(1)] private int attacksBeforeCounter = 3;
        [SerializeField, Min(0.1f)] private float attackMemoryDuration = 10f;
        [SerializeField, Min(0.1f)] private float counterCommitDuration = 1.25f;
        [SerializeField, Min(0.1f)] private float pressureRefreshRate = 0.1f;

        [Header("Persistent Memory")]
        [SerializeField] private bool usePersistentPatternMemory = true;
        [SerializeField, Range(0f, 1f)] private float persistentMemoryWeight = 0.35f;
        [SerializeField, Min(0.1f)] private float persistentOrbitRecordInterval = 1.5f;
        [SerializeField, Range(0f, 1f)] private float persistentOrbitRecordThreshold = 0.55f;

        [Header("Debug")]
        [SerializeField] private bool showGizmos = true;

        private readonly Queue<PlayerSample> _samples = new();
        private readonly Dictionary<string, int> _attackPatternCounts = new();
        private readonly Dictionary<string, float> _attackPatternLastSeen = new();

        private EnemyTactic _currentTactic = EnemyTactic.Hold;
        private Vector2 _desiredVelocity;
        private Vector2 _desiredPosition;
        private Vector2 _lastKnownPlayerPosition;
        private PlayerAttackData _lastObservedAttack;
        private PlayerPlaystyleProfile _persistentProfile;
        private string _learnedAttackSignature = string.Empty;
        private float _lastSampleTime;
        private float _counterUntilTime;
        private float _lastSteeringRefreshTime;
        private float _lastPersistentOrbitRecordTime;
        private float _orbitBias;
        private float _combinedOrbitBias;
        private bool _hasObservedAttack;

        public string DebugTactic => _currentTactic.ToString();
        public float OrbitBias => _orbitBias;
        public float CombinedOrbitBias => _combinedOrbitBias;
        public string LearnedAttackSignature => _learnedAttackSignature;
        public PlayerMovementController TargetPlayer => targetPlayer;
        public bool IsCountering => Time.time < _counterUntilTime;

        private void Awake()
        {
            if (rb == null)
            {
                rb = GetComponent<Rigidbody2D>();
            }

            if (targetPlayer == null)
            {
                targetPlayer = FindFirstObjectByType<PlayerMovementController>();
            }

            _persistentProfile = usePersistentPatternMemory ? PlayerPatternMemoryStore.LoadOrCreate() : new PlayerPlaystyleProfile();
            SeedLearnedPatternsFromMemory();
        }

        private void OnEnable()
        {
            BindPlayerEvents();
        }

        private void OnDisable()
        {
            UnbindPlayerEvents();
            _desiredVelocity = Vector2.zero;
            _desiredPosition = transform.position;
            SavePersistentMemory();

            if (rb != null)
            {
                rb.linearVelocity = Vector2.zero;
            }
        }

        private void OnApplicationQuit()
        {
            SavePersistentMemory();
        }

        private void FixedUpdate()
        {
            if (!EnsurePlayerReference())
            {
                ApplyVelocity(Vector2.zero);
                _currentTactic = EnemyTactic.Hold;
                return;
            }

            var enemyPosition = (Vector2)transform.position;
            var playerPosition = (Vector2)targetPlayer.transform.position;
            _lastKnownPlayerPosition = playerPosition;

            if (Vector2.Distance(enemyPosition, playerPosition) > reacquireDistance)
            {
                ApplyVelocity(Vector2.zero);
                _currentTactic = EnemyTactic.Hold;
                return;
            }

            RecordPlayerSample(enemyPosition, playerPosition);
            UpdateLearningState();

            if (Time.time >= _lastSteeringRefreshTime + pressureRefreshRate)
            {
                EvaluateTactic(enemyPosition, playerPosition);
                _lastSteeringRefreshTime = Time.time;
            }

            MoveTowardsDesiredPosition(enemyPosition);
        }

        [ContextMenu("Clear Persistent Player Memory")]
        private void ClearPersistentPlayerMemory()
        {
            PlayerPatternMemoryStore.Clear();
            _persistentProfile = PlayerPatternMemoryStore.LoadOrCreate();
            _learnedAttackSignature = string.Empty;
            _combinedOrbitBias = _orbitBias;
        }

        private bool EnsurePlayerReference()
        {
            if (targetPlayer != null)
            {
                return true;
            }

            targetPlayer = FindFirstObjectByType<PlayerMovementController>();
            BindPlayerEvents();
            return targetPlayer != null;
        }

        private void BindPlayerEvents()
        {
            if (targetPlayer == null)
            {
                return;
            }

            targetPlayer.AttackPerformed -= HandlePlayerAttackPerformed;
            targetPlayer.AttackPerformed += HandlePlayerAttackPerformed;
        }

        private void UnbindPlayerEvents()
        {
            if (targetPlayer == null)
            {
                return;
            }

            targetPlayer.AttackPerformed -= HandlePlayerAttackPerformed;
        }

        private void RecordPlayerSample(Vector2 enemyPosition, Vector2 playerPosition)
        {
            if (Time.time < _lastSampleTime + sampleInterval)
            {
                return;
            }

            var relativePosition = playerPosition - enemyPosition;
            var moveDirection = targetPlayer.MoveInput;
            var orbitContribution = relativePosition.sqrMagnitude > 0.001f && moveDirection.sqrMagnitude > 0.001f
                ? Vector3.Cross(relativePosition.normalized, moveDirection.normalized).z
                : 0f;

            _samples.Enqueue(new PlayerSample
            {
                Time = Time.time,
                RelativePosition = relativePosition,
                MoveDirection = moveDirection,
                OrbitContribution = orbitContribution
            });

            _lastSampleTime = Time.time;
            TrimExpiredSamples();
        }

        private void TrimExpiredSamples()
        {
            while (_samples.Count > 0 && Time.time - _samples.Peek().Time > memoryDuration)
            {
                _samples.Dequeue();
            }
        }

        private void UpdateLearningState()
        {
            TrimExpiredSamples();

            if (_samples.Count == 0)
            {
                _orbitBias = 0f;
                _combinedOrbitBias = GetCombinedOrbitBias();
                DecayAttackMemory();
                return;
            }

            var orbitAccumulator = 0f;
            var weightAccumulator = 0f;

            foreach (var sample in _samples)
            {
                var sampleAgeWeight = 1f - Mathf.Clamp01((Time.time - sample.Time) / memoryDuration);
                var tangentialStrength = Mathf.Abs(sample.OrbitContribution);
                var radiusWeight = Mathf.Clamp01(sample.RelativePosition.magnitude / Mathf.Max(stoppingDistance, 0.001f));
                var intentWeight = Mathf.Max(sample.MoveDirection.magnitude, 0.15f);
                var sampleWeight = tangentialStrength * sampleAgeWeight * radiusWeight * intentWeight;
                orbitAccumulator += sample.OrbitContribution * sampleWeight;
                weightAccumulator += sampleWeight;
            }

            _orbitBias = weightAccumulator > 0.001f ? orbitAccumulator / weightAccumulator : 0f;
            _combinedOrbitBias = GetCombinedOrbitBias();
            RememberPersistentOrbitBias();
            DecayAttackMemory();
        }

        private void EvaluateTactic(Vector2 enemyPosition, Vector2 playerPosition)
        {
            var toPlayer = playerPosition - enemyPosition;
            var distanceToPlayer = toPlayer.magnitude;
            var playerVelocity = targetPlayer.CurrentVelocity;
            var playerMoveDirection = targetPlayer.MoveInput;
            var playerForward = targetPlayer.FacingDirection.sqrMagnitude > 0.001f ? targetPlayer.FacingDirection : toPlayer.normalized;
            var predictedPlayerPosition = playerPosition + playerVelocity * predictionTime;

            if (Time.time < _counterUntilTime)
            {
                _currentTactic = EnemyTactic.CounterAttack;
                var counterDirection = ResolveCounterDirection(enemyPosition, playerPosition, playerForward);
                _desiredPosition = playerPosition + counterDirection * dodgeDistance - playerForward * (stoppingDistance * 0.5f);
                return;
            }

            if (Mathf.Abs(_combinedOrbitBias) >= orbitDetectionThreshold && playerMoveDirection.sqrMagnitude > 0.04f)
            {
                var tangent = _combinedOrbitBias > 0f
                    ? new Vector2(-toPlayer.y, toPlayer.x).normalized
                    : new Vector2(toPlayer.y, -toPlayer.x).normalized;
                var forwardLead = playerMoveDirection.sqrMagnitude > 0.001f ? playerMoveDirection.normalized * cutOffLeadDistance : tangent * cutOffLeadDistance;
                _desiredPosition = predictedPlayerPosition + tangent * cutOffLeadDistance + forwardLead;
                _currentTactic = _combinedOrbitBias > 0f ? EnemyTactic.CutOffCounterClockwise : EnemyTactic.CutOffClockwise;
                return;
            }

            if (distanceToPlayer <= stoppingDistance)
            {
                _desiredPosition = enemyPosition;
                _currentTactic = EnemyTactic.Hold;
                return;
            }

            _desiredPosition = predictedPlayerPosition - playerForward * stoppingDistance;
            _currentTactic = EnemyTactic.Pressure;
        }

        private void MoveTowardsDesiredPosition(Vector2 enemyPosition)
        {
            var toTarget = _desiredPosition - enemyPosition;
            var speed = _currentTactic == EnemyTactic.CounterAttack ? counterMoveSpeed : moveSpeed;

            if (toTarget.magnitude <= 0.05f)
            {
                ApplyVelocity(Vector2.zero);
                return;
            }

            var desiredDirection = toTarget.normalized;
            var desiredVelocity = desiredDirection * speed;

            if (_currentTactic == EnemyTactic.Hold && toTarget.magnitude <= stoppingDistance)
            {
                desiredVelocity = Vector2.zero;
            }

            ApplyVelocity(desiredVelocity);
        }

        private void ApplyVelocity(Vector2 velocity)
        {
            _desiredVelocity = velocity;

            if (rb != null)
            {
                rb.linearVelocity = velocity;
                return;
            }

            transform.position += (Vector3)(velocity * Time.fixedDeltaTime);
        }

        private void HandlePlayerAttackPerformed(PlayerAttackData attackData)
        {
            _lastObservedAttack = attackData;
            _hasObservedAttack = true;

            var signature = attackData.Signature;
            _attackPatternLastSeen[signature] = Time.time;
            _attackPatternCounts.TryGetValue(signature, out var runtimeCount);
            runtimeCount++;
            _attackPatternCounts[signature] = runtimeCount;

            var persistentCount = 0;
            if (usePersistentPatternMemory && _persistentProfile != null)
            {
                _persistentProfile.RecordAttackSignature(signature, Time.time);
                persistentCount = _persistentProfile.GetAttackUsageCount(signature);
                SavePersistentMemory();
            }

            if (runtimeCount >= attacksBeforeCounter || persistentCount >= attacksBeforeCounter)
            {
                _learnedAttackSignature = signature;
            }

            if (signature == _learnedAttackSignature)
            {
                _counterUntilTime = Time.time + counterCommitDuration;
            }
        }

        private float GetCombinedOrbitBias()
        {
            if (!usePersistentPatternMemory || _persistentProfile == null)
            {
                return _orbitBias;
            }

            return Mathf.Clamp(_orbitBias + _persistentProfile.PersistentOrbitBias * persistentMemoryWeight, -1f, 1f);
        }

        private void RememberPersistentOrbitBias()
        {
            if (!usePersistentPatternMemory || _persistentProfile == null)
            {
                return;
            }

            if (Mathf.Abs(_orbitBias) < persistentOrbitRecordThreshold)
            {
                return;
            }

            if (Time.time < _lastPersistentOrbitRecordTime + persistentOrbitRecordInterval)
            {
                return;
            }

            _persistentProfile.RecordOrbitBias(_orbitBias);
            _lastPersistentOrbitRecordTime = Time.time;
            SavePersistentMemory();
        }

        private void SeedLearnedPatternsFromMemory()
        {
            if (!usePersistentPatternMemory || _persistentProfile == null)
            {
                _combinedOrbitBias = _orbitBias;
                return;
            }

            _persistentProfile.EnsureVersion();
            _learnedAttackSignature = _persistentProfile.GetMostUsedAttackSignature(attacksBeforeCounter);
            _combinedOrbitBias = GetCombinedOrbitBias();
        }

        private void SavePersistentMemory()
        {
            if (!usePersistentPatternMemory || _persistentProfile == null)
            {
                return;
            }

            PlayerPatternMemoryStore.Save(_persistentProfile);
        }

        private Vector2 ResolveCounterDirection(Vector2 enemyPosition, Vector2 playerPosition, Vector2 fallbackForward)
        {
            var attackDirection = fallbackForward;

            if (_hasObservedAttack && Time.time - _lastObservedAttack.Time <= attackMemoryDuration)
            {
                if (_lastObservedAttack.Direction.sqrMagnitude > 0.001f)
                {
                    attackDirection = _lastObservedAttack.Direction.normalized;
                }
                else if ((playerPosition - _lastObservedAttack.PlayerPosition).sqrMagnitude > 0.001f)
                {
                    attackDirection = (playerPosition - _lastObservedAttack.PlayerPosition).normalized;
                }
            }

            var dodgeDirection = new Vector2(-attackDirection.y, attackDirection.x);
            var toEnemyFromAttack = enemyPosition - playerPosition;
            if (Vector2.Dot(dodgeDirection, toEnemyFromAttack) < 0f)
            {
                dodgeDirection *= -1f;
            }

            return dodgeDirection.sqrMagnitude > 0.001f ? dodgeDirection.normalized : new Vector2(-fallbackForward.y, fallbackForward.x).normalized;
        }

        private void DecayAttackMemory()
        {
            if (_attackPatternLastSeen.Count == 0)
            {
                return;
            }

            var expiredSignatures = ListPool<string>.Get();

            foreach (var pair in _attackPatternLastSeen)
            {
                if (Time.time - pair.Value <= attackMemoryDuration)
                {
                    continue;
                }

                expiredSignatures.Add(pair.Key);
            }

            foreach (var signature in expiredSignatures)
            {
                _attackPatternLastSeen.Remove(signature);
                _attackPatternCounts.Remove(signature);
            }

            if (_hasObservedAttack && Time.time - _lastObservedAttack.Time > attackMemoryDuration)
            {
                _hasObservedAttack = false;
            }

            if (string.IsNullOrEmpty(_learnedAttackSignature))
            {
                ListPool<string>.Release(expiredSignatures);
                return;
            }

            var runtimeCount = _attackPatternCounts.TryGetValue(_learnedAttackSignature, out var rememberedRuntimeCount)
                ? rememberedRuntimeCount
                : 0;
            var persistentCount = usePersistentPatternMemory && _persistentProfile != null
                ? _persistentProfile.GetAttackUsageCount(_learnedAttackSignature)
                : 0;

            if (runtimeCount < attacksBeforeCounter && persistentCount < attacksBeforeCounter)
            {
                _learnedAttackSignature = string.Empty;
            }

            ListPool<string>.Release(expiredSignatures);
        }

        private void OnDrawGizmosSelected()
        {
            if (!showGizmos)
            {
                return;
            }

            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(_desiredPosition, 0.2f);
            Gizmos.DrawLine(transform.position, _desiredPosition);

            Gizmos.color = _currentTactic == EnemyTactic.CounterAttack ? Color.red : Color.yellow;
            Gizmos.DrawLine(transform.position, (Vector2)transform.position + _desiredVelocity.normalized * 1.2f);

            if (targetPlayer == null)
            {
                return;
            }

            Gizmos.color = _combinedOrbitBias >= 0f ? Color.green : Color.magenta;
            Gizmos.DrawLine(transform.position, _lastKnownPlayerPosition);
        }

        private static class ListPool<T>
        {
            private static readonly Stack<List<T>> Pool = new();

            public static List<T> Get()
            {
                return Pool.Count > 0 ? Pool.Pop() : new List<T>();
            }

            public static void Release(List<T> list)
            {
                list.Clear();
                Pool.Push(list);
            }
        }
    }
}
