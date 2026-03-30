using System.Collections.Generic;
using _Game.Scripts.Combat;
using _Game.Scripts.Persistence;
using UnityEngine;

namespace _Game.Scripts
{
    [RequireComponent(typeof(Health2D))]
    [RequireComponent(typeof(Hurtbox2D))]
    [RequireComponent(typeof(HitReaction2D))]
    [RequireComponent(typeof(Shield2D))]
    [RequireComponent(typeof(EnemyRespawnController))]
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
        [SerializeField] private Collider2D movementCollider;
        [SerializeField] private HitReaction2D hitReaction;
        [SerializeField] private Shield2D shield;
        [SerializeField] private PlayerMovementController targetPlayer;

        [Header("Movement")]
        [SerializeField, Min(0f)] private float moveSpeed = 4.5f;
        [SerializeField, Min(0f)] private float counterMoveSpeed = 6f;
        [SerializeField, Min(0f)] private float stoppingDistance = 1.35f;
        [SerializeField, Min(0f)] private float cutOffLeadDistance = 1.75f;
        [SerializeField, Min(0f)] private float predictionTime = 0.4f;
        [SerializeField, Min(0f)] private float dodgeDistance = 2.25f;
        [SerializeField, Min(0f)] private float reacquireDistance = 14f;

        [Header("Obstacle Avoidance")]
        [SerializeField] private LayerMask obstacleLayers = ~0;
        [SerializeField, Min(0.05f)] private float obstacleProbeDistance = 0.8f;
        [SerializeField, Min(0f)] private float obstacleClearance = 0.1f;
        [SerializeField, Range(10f, 85f)] private float avoidanceProbeAngle = 35f;
        [SerializeField, Range(1, 3)] private int avoidanceProbeSteps = 2;
        [SerializeField, Min(0.05f)] private float avoidanceCommitDuration = 0.4f;
        [SerializeField, Min(0.05f)] private float stuckDetectionTime = 0.3f;
        [SerializeField, Min(0f)] private float stuckSpeedThreshold = 0.15f;

        [Header("Pathfinding")]
        [SerializeField] private Grid pathGrid;
        [SerializeField] private Vector2 pathGridOrigin = Vector2.zero;
        [SerializeField] private Vector2 pathCellSize = Vector2.one;
        [SerializeField, Min(0.05f)] private float waypointReachedDistance = 0.25f;
        [SerializeField, Min(0.05f)] private float pathRepathInterval = 0.3f;
        [SerializeField, Min(0.05f)] private float pathGoalChangeThreshold = 0.75f;
        [SerializeField, Min(1)] private int pathSearchPaddingCells = 8;
        [SerializeField, Min(32)] private int pathMaxIterations = 512;
        [SerializeField, Min(1)] private int pathGoalSearchRadius = 3;

        [Header("Defense")]
        [SerializeField, Min(0.25f)] private float shieldRaiseDistance = 2.4f;
        [SerializeField, Range(-1f, 1f)] private float shieldThreatFacingThreshold = 0.15f;
        [SerializeField, Min(0.05f)] private float shieldHoldDuration = 0.45f;
        [SerializeField, Min(0.05f)] private float shieldAttackMemoryWindow = 1.1f;

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
        private readonly List<Vector2> _pathWaypoints = new();
        private readonly List<Vector2> _pathScratchWaypoints = new();
        private readonly Collider2D[] _pathOverlapBuffer = new Collider2D[16];
        private readonly RaycastHit2D[] _obstacleHits = new RaycastHit2D[12];

        private EnemyTactic _currentTactic = EnemyTactic.Hold;
        private ContactFilter2D _obstacleContactFilter;
        private Vector2 _desiredVelocity;
        private Vector2 _desiredPosition;
        private Vector2 _lastKnownPlayerPosition;
        private Vector2 _lastPosition;
        private Vector2 _lastResolvedMoveDirection = Vector2.left;
        private Vector2 _lastObstacleNormal;
        private Vector2 _lastObstaclePoint;
        private PlayerAttackData _lastObservedAttack;
        private PlayerPlaystyleProfile _persistentProfile;
        private string _learnedAttackSignature = string.Empty;
        private float _lastSampleTime;
        private float _counterUntilTime;
        private float _lastSteeringRefreshTime;
        private float _lastPersistentOrbitRecordTime;
        private float _lastPathRequestTime = float.NegativeInfinity;
        private float _orbitBias;
        private float _combinedOrbitBias;
        private float _shieldUntilTime;
        private float _stuckTimer;
        private float _avoidanceSideLockUntilTime;
        private bool _hasObservedAttack;
        private bool _isAvoidingObstacle;
        private bool _hasPath;
        private int _preferredAvoidanceSide = 1;
        private int _currentPathWaypointIndex;
        private Vector2 _lastPathGoal;

        public string DebugTactic => _currentTactic.ToString();
        public float OrbitBias => _orbitBias;
        public float CombinedOrbitBias => _combinedOrbitBias;
        public string LearnedAttackSignature => _learnedAttackSignature;
        public PlayerMovementController TargetPlayer => targetPlayer;
        public bool IsCountering => Time.time < _counterUntilTime;
        public bool CanAct => hitReaction == null || !hitReaction.IsActionLocked;
        public bool IsShielding => shield != null && shield.IsShielding;

        private void Awake()
        {
            if (rb == null)
            {
                rb = GetComponent<Rigidbody2D>();
            }

            if (movementCollider == null)
            {
                movementCollider = GetComponent<Collider2D>();
            }

            if (hitReaction == null)
            {
                hitReaction = GetComponent<HitReaction2D>();
            }

            if (shield == null)
            {
                shield = GetComponent<Shield2D>();
            }

            if (GetComponent<EnemyRespawnController>() == null)
            {
                gameObject.AddComponent<EnemyRespawnController>();
            }

            if (targetPlayer == null)
            {
                targetPlayer = FindFirstObjectByType<PlayerMovementController>();
            }

            ResolvePathGridReference();
            ConfigureObstacleContactFilter();
            _persistentProfile = usePersistentPatternMemory ? PlayerPatternMemoryStore.LoadOrCreate() : new PlayerPlaystyleProfile();
            SeedLearnedPatternsFromMemory();
            _lastPosition = transform.position;
        }

        private void OnValidate()
        {
            ResolvePathGridReference();
            ConfigureObstacleContactFilter();
        }

        private void OnEnable()
        {
            BindPlayerEvents();
            _lastPosition = transform.position;
        }

        private void OnDisable()
        {
            UnbindPlayerEvents();
            _desiredVelocity = Vector2.zero;
            _desiredPosition = transform.position;
            _isAvoidingObstacle = false;
            _stuckTimer = 0f;
            SetShielding(false);
            ClearPath();
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
            var enemyPosition = (Vector2)transform.position;
            UpdateStuckState(enemyPosition);

            if (!EnsurePlayerReference())
            {
                ApplyVelocity(Vector2.zero);
                _currentTactic = EnemyTactic.Hold;
                _lastPosition = enemyPosition;
                return;
            }

            var playerPosition = (Vector2)targetPlayer.transform.position;
            _lastKnownPlayerPosition = playerPosition;
            UpdateShieldState(enemyPosition, playerPosition);

            if (Vector2.Distance(enemyPosition, playerPosition) > reacquireDistance)
            {
                ApplyVelocity(Vector2.zero);
                _currentTactic = EnemyTactic.Hold;
                _lastPosition = enemyPosition;
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
            _lastPosition = enemyPosition;
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
            var movementTarget = ResolveMovementTarget(enemyPosition);
            var toTarget = movementTarget - enemyPosition;
            var speed = _currentTactic == EnemyTactic.CounterAttack ? counterMoveSpeed : moveSpeed;

            if (toTarget.magnitude <= 0.05f)
            {
                ApplyVelocity(Vector2.zero);
                return;
            }

            var desiredDirection = toTarget.normalized;
            desiredDirection = ResolveMovementDirection(desiredDirection, speed);
            var desiredVelocity = desiredDirection * speed;

            if (IsShielding && shield != null)
            {
                desiredVelocity *= shield.MovementMultiplier;
            }

            if (_currentTactic == EnemyTactic.Hold && toTarget.magnitude <= stoppingDistance)
            {
                desiredVelocity = Vector2.zero;
            }

            ApplyVelocity(desiredVelocity);
        }

        private Vector2 ResolveMovementTarget(Vector2 enemyPosition)
        {
            AdvancePathWaypoint(enemyPosition);

            if (HasDirectPath(enemyPosition, _desiredPosition))
            {
                ClearPath();
                return _desiredPosition;
            }

            if (ShouldRepath())
            {
                RebuildPath(enemyPosition, _desiredPosition);
                AdvancePathWaypoint(enemyPosition);
            }

            if (_hasPath && _currentPathWaypointIndex < _pathWaypoints.Count)
            {
                return _pathWaypoints[_currentPathWaypointIndex];
            }

            return _desiredPosition;
        }

        private void RebuildPath(Vector2 startPosition, Vector2 goalPosition)
        {
            ResolvePathGridReference();
            _lastPathRequestTime = Time.time;
            _lastPathGoal = goalPosition;

            if (movementCollider == null || obstacleLayers.value == 0)
            {
                ClearPath();
                return;
            }

            _pathScratchWaypoints.Clear();
            var settings = new GridPathfinder2D.Settings
            {
                Grid = pathGrid,
                FallbackOrigin = pathGridOrigin,
                FallbackCellSize = pathCellSize,
                ObstacleLayers = obstacleLayers,
                AgentSize = movementCollider.bounds.size,
                Clearance = obstacleClearance,
                SearchPaddingCells = pathSearchPaddingCells,
                MaxIterations = pathMaxIterations,
                GoalSearchRadius = pathGoalSearchRadius,
                IgnoredCollider = movementCollider,
                IgnoredRigidbody = rb,
                IgnoredRoot = transform,
                IgnoredSecondaryRoot = targetPlayer != null ? targetPlayer.transform : null,
                OverlapBuffer = _pathOverlapBuffer
            };

            if (!GridPathfinder2D.TryFindPath(startPosition, goalPosition, settings, _pathScratchWaypoints))
            {
                ClearPath();
                return;
            }

            _pathWaypoints.Clear();
            _pathWaypoints.AddRange(_pathScratchWaypoints);
            _currentPathWaypointIndex = 0;
            _hasPath = _pathWaypoints.Count > 0;
        }

        private bool ShouldRepath()
        {
            if (!_hasPath)
            {
                return Time.time >= _lastPathRequestTime + pathRepathInterval;
            }

            if (Vector2.SqrMagnitude(_desiredPosition - _lastPathGoal) >= pathGoalChangeThreshold * pathGoalChangeThreshold)
            {
                return Time.time >= _lastPathRequestTime + pathRepathInterval;
            }

            return _currentPathWaypointIndex >= _pathWaypoints.Count && Time.time >= _lastPathRequestTime + pathRepathInterval;
        }

        private void AdvancePathWaypoint(Vector2 enemyPosition)
        {
            if (!_hasPath || _pathWaypoints.Count == 0)
            {
                _currentPathWaypointIndex = 0;
                return;
            }

            while (_currentPathWaypointIndex < _pathWaypoints.Count && Vector2.Distance(enemyPosition, _pathWaypoints[_currentPathWaypointIndex]) <= waypointReachedDistance)
            {
                _currentPathWaypointIndex++;
            }

            if (_currentPathWaypointIndex >= _pathWaypoints.Count)
            {
                ClearPath();
                return;
            }

            for (var i = _pathWaypoints.Count - 1; i > _currentPathWaypointIndex; i--)
            {
                if (!HasDirectPath(enemyPosition, _pathWaypoints[i]))
                {
                    continue;
                }

                _currentPathWaypointIndex = i;
                break;
            }

            while (_currentPathWaypointIndex < _pathWaypoints.Count && Vector2.Distance(enemyPosition, _pathWaypoints[_currentPathWaypointIndex]) <= waypointReachedDistance)
            {
                _currentPathWaypointIndex++;
            }

            if (_currentPathWaypointIndex >= _pathWaypoints.Count)
            {
                ClearPath();
            }
        }

        private bool HasDirectPath(Vector2 fromPosition, Vector2 toPosition)
        {
            var direction = toPosition - fromPosition;
            var distance = direction.magnitude;
            if (distance <= waypointReachedDistance)
            {
                return true;
            }

            return !TryGetBlockingHit(direction / distance, distance + obstacleClearance, out _);
        }

        private void ClearPath()
        {
            _hasPath = false;
            _currentPathWaypointIndex = 0;
            _pathWaypoints.Clear();
            _pathScratchWaypoints.Clear();
        }

        private void ResolvePathGridReference()
        {
            if (pathGrid == null)
            {
                pathGrid = FindFirstObjectByType<Grid>();
            }

            if (Mathf.Abs(pathCellSize.x) <= 0.001f)
            {
                pathCellSize.x = 1f;
            }

            if (Mathf.Abs(pathCellSize.y) <= 0.001f)
            {
                pathCellSize.y = 1f;
            }
        }

        private void UpdateShieldState(Vector2 enemyPosition, Vector2 playerPosition)
        {
            if (shield == null)
            {
                return;
            }

            var toPlayer = playerPosition - enemyPosition;
            var facingDirection = toPlayer.sqrMagnitude > 0.001f ? toPlayer.normalized : _lastResolvedMoveDirection;
            shield.SetFacing(facingDirection);

            if (!CanAct)
            {
                SetShielding(false);
                return;
            }

            var distanceToPlayer = toPlayer.magnitude;
            var playerForward = targetPlayer != null && targetPlayer.FacingDirection.sqrMagnitude > 0.001f
                ? targetPlayer.FacingDirection.normalized
                : Vector2.zero;
            var playerToEnemy = distanceToPlayer > 0.001f ? (enemyPosition - playerPosition).normalized : Vector2.zero;
            var playerIsThreatening = distanceToPlayer <= shieldRaiseDistance
                && playerForward.sqrMagnitude > 0.001f
                && Vector2.Dot(playerForward, playerToEnemy) >= shieldThreatFacingThreshold;
            var recentlyObservedAttack = _hasObservedAttack && Time.time - _lastObservedAttack.Time <= shieldAttackMemoryWindow;
            var learnedThreat = recentlyObservedAttack
                && !string.IsNullOrEmpty(_learnedAttackSignature)
                && _lastObservedAttack.Signature == _learnedAttackSignature;

            if (playerIsThreatening || learnedThreat)
            {
                _shieldUntilTime = Mathf.Max(_shieldUntilTime, Time.time + shieldHoldDuration);
            }

            var shouldShield = Time.time < _shieldUntilTime && !IsCountering;
            SetShielding(shouldShield);
        }

        private void SetShielding(bool isShielding)
        {
            if (shield == null)
            {
                return;
            }

            if (targetPlayer != null)
            {
                var facingDirection = (Vector2)targetPlayer.transform.position - (Vector2)transform.position;
                if (facingDirection.sqrMagnitude > 0.001f)
                {
                    shield.SetFacing(facingDirection.normalized);
                }
            }

            shield.SetShielding(isShielding);
        }

        private Vector2 ResolveMovementDirection(Vector2 desiredDirection, float speed)
        {
            _isAvoidingObstacle = false;
            _lastObstacleNormal = Vector2.zero;
            _lastObstaclePoint = Vector2.zero;

            if (movementCollider == null || desiredDirection.sqrMagnitude <= 0.001f || obstacleLayers.value == 0)
            {
                _lastResolvedMoveDirection = desiredDirection;
                return desiredDirection;
            }

            var castDistance = Mathf.Max(obstacleProbeDistance, speed * Time.fixedDeltaTime) + obstacleClearance;
            if (!TryGetBlockingHit(desiredDirection, castDistance, out var blockingHit))
            {
                _lastResolvedMoveDirection = desiredDirection;
                return desiredDirection;
            }

            _isAvoidingObstacle = true;
            _lastObstacleNormal = blockingHit.normal;
            _lastObstaclePoint = blockingHit.point;

            if (Time.time >= _avoidanceSideLockUntilTime)
            {
                _preferredAvoidanceSide = DeterminePreferredAvoidanceSide(desiredDirection, blockingHit.normal);
            }

            var bestDirection = Vector2.zero;
            var bestScore = float.NegativeInfinity;
            var foundDirection = false;

            EvaluateAvoidanceCandidate(GetSurfaceTangent(blockingHit.normal, _preferredAvoidanceSide), desiredDirection, castDistance, ref bestDirection, ref bestScore, ref foundDirection);
            EvaluateAvoidanceCandidate(GetSurfaceTangent(blockingHit.normal, -_preferredAvoidanceSide), desiredDirection, castDistance, ref bestDirection, ref bestScore, ref foundDirection);

            for (var step = 1; step <= avoidanceProbeSteps; step++)
            {
                var angle = avoidanceProbeAngle * step;
                EvaluateAvoidanceCandidate(Rotate(desiredDirection, angle * _preferredAvoidanceSide), desiredDirection, castDistance, ref bestDirection, ref bestScore, ref foundDirection);
                EvaluateAvoidanceCandidate(Rotate(desiredDirection, -angle * _preferredAvoidanceSide), desiredDirection, castDistance, ref bestDirection, ref bestScore, ref foundDirection);
            }

            if (foundDirection)
            {
                _lastResolvedMoveDirection = bestDirection;
                _avoidanceSideLockUntilTime = Time.time + avoidanceCommitDuration;
                return bestDirection;
            }

            var fallbackDirection = GetSurfaceTangent(blockingHit.normal, _preferredAvoidanceSide);
            _lastResolvedMoveDirection = fallbackDirection;
            _avoidanceSideLockUntilTime = Time.time + avoidanceCommitDuration;
            return fallbackDirection;
        }

        private void EvaluateAvoidanceCandidate(
            Vector2 candidateDirection,
            Vector2 desiredDirection,
            float castDistance,
            ref Vector2 bestDirection,
            ref float bestScore,
            ref bool foundDirection)
        {
            if (candidateDirection.sqrMagnitude <= 0.001f)
            {
                return;
            }

            candidateDirection = candidateDirection.normalized;
            if (TryGetBlockingHit(candidateDirection, castDistance, out _))
            {
                return;
            }

            var score = Vector2.Dot(candidateDirection, desiredDirection);
            var side = GetSignedSide(desiredDirection, candidateDirection);
            if (Time.time < _avoidanceSideLockUntilTime)
            {
                if (side == _preferredAvoidanceSide)
                {
                    score += 0.2f;
                }
                else if (side == -_preferredAvoidanceSide)
                {
                    score -= 0.05f;
                }
            }

            if (score <= bestScore)
            {
                return;
            }

            bestScore = score;
            bestDirection = candidateDirection;
            foundDirection = true;
        }

        private bool TryGetBlockingHit(Vector2 direction, float distance, out RaycastHit2D blockingHit)
        {
            blockingHit = default;

            if (movementCollider == null || direction.sqrMagnitude <= 0.001f || obstacleLayers.value == 0)
            {
                return false;
            }

            var hitCount = movementCollider.Cast(direction.normalized, _obstacleContactFilter, _obstacleHits, distance);
            var closestDistance = float.PositiveInfinity;

            for (var i = 0; i < hitCount; i++)
            {
                var hit = _obstacleHits[i];
                if (hit.collider == null || IsIgnoredObstacle(hit.collider))
                {
                    continue;
                }

                if (hit.distance >= closestDistance)
                {
                    continue;
                }

                closestDistance = hit.distance;
                blockingHit = hit;
            }

            return blockingHit.collider != null;
        }

        private bool IsIgnoredObstacle(Collider2D collider)
        {
            if (collider == null)
            {
                return true;
            }

            var colliderTransform = collider.transform;
            if (colliderTransform == transform || colliderTransform.IsChildOf(transform))
            {
                return true;
            }

            if (rb != null && collider.attachedRigidbody == rb)
            {
                return true;
            }

            if (targetPlayer != null)
            {
                var playerTransform = targetPlayer.transform;
                if (colliderTransform == playerTransform || colliderTransform.IsChildOf(playerTransform))
                {
                    return true;
                }
            }

            return false;
        }

        private void ConfigureObstacleContactFilter()
        {
            _obstacleContactFilter.useLayerMask = true;
            _obstacleContactFilter.layerMask = obstacleLayers;
            _obstacleContactFilter.useTriggers = false;
        }

        private void UpdateStuckState(Vector2 currentPosition)
        {
            var commandedSpeed = _desiredVelocity.magnitude;
            if (commandedSpeed <= stuckSpeedThreshold)
            {
                _stuckTimer = 0f;
                return;
            }

            var actualSpeed = Time.fixedDeltaTime > 0f
                ? (currentPosition - _lastPosition).magnitude / Time.fixedDeltaTime
                : 0f;

            if (actualSpeed > stuckSpeedThreshold)
            {
                _stuckTimer = 0f;
                return;
            }

            _stuckTimer += Time.fixedDeltaTime;
            if (_stuckTimer < stuckDetectionTime)
            {
                return;
            }

            _preferredAvoidanceSide *= -1;
            _avoidanceSideLockUntilTime = Time.time + avoidanceCommitDuration;
            _stuckTimer = 0f;
        }

        private int DeterminePreferredAvoidanceSide(Vector2 desiredDirection, Vector2 obstacleNormal)
        {
            var positiveTangent = GetSurfaceTangent(obstacleNormal, 1);
            var negativeTangent = GetSurfaceTangent(obstacleNormal, -1);
            return Vector2.Dot(positiveTangent, desiredDirection) >= Vector2.Dot(negativeTangent, desiredDirection) ? 1 : -1;
        }

        private static Vector2 GetSurfaceTangent(Vector2 obstacleNormal, int side)
        {
            var tangent = new Vector2(-obstacleNormal.y, obstacleNormal.x).normalized;
            return side >= 0 ? tangent : -tangent;
        }

        private static int GetSignedSide(Vector2 forward, Vector2 candidate)
        {
            return Vector3.Cross(forward, candidate).z >= 0f ? 1 : -1;
        }

        private static Vector2 Rotate(Vector2 vector, float degrees)
        {
            var radians = degrees * Mathf.Deg2Rad;
            var sin = Mathf.Sin(radians);
            var cos = Mathf.Cos(radians);
            return new Vector2(vector.x * cos - vector.y * sin, vector.x * sin + vector.y * cos);
        }

        private void ApplyVelocity(Vector2 velocity)
        {
            if (hitReaction != null)
            {
                velocity = hitReaction.GetModifiedVelocity(velocity);
            }

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

            Gizmos.color = _isAvoidingObstacle ? new Color(1f, 0.55f, 0f) : Color.white;
            Gizmos.DrawLine(transform.position, (Vector2)transform.position + _lastResolvedMoveDirection.normalized * 1.2f);

            if (_isAvoidingObstacle)
            {
                Gizmos.color = Color.blue;
                Gizmos.DrawWireSphere(_lastObstaclePoint, 0.08f);
                Gizmos.DrawLine(_lastObstaclePoint, _lastObstaclePoint + _lastObstacleNormal * 0.6f);
            }

            if (_hasPath && _currentPathWaypointIndex < _pathWaypoints.Count)
            {
                Gizmos.color = Color.Lerp(Color.cyan, Color.white, 0.35f);
                var previousPoint = (Vector2)transform.position;
                for (var i = _currentPathWaypointIndex; i < _pathWaypoints.Count; i++)
                {
                    var waypoint = _pathWaypoints[i];
                    Gizmos.DrawWireSphere(waypoint, 0.12f);
                    Gizmos.DrawLine(previousPoint, waypoint);
                    previousPoint = waypoint;
                }
            }

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
