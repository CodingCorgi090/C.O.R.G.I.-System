using System.Collections.Generic;
using _Game.Scripts.Combat;
using _Game.Scripts.Persistence;
using UnityEngine;

namespace _Game.Scripts.Generation
{
    [DisallowMultipleComponent]
    public class RuntimeLevelGenerator : MonoBehaviour
    {
        private struct ReservedSpawnArea
        {
            public Vector2 Position;
            public float Radius;
        }

        [SerializeField] private SpawnCatalog spawnCatalog;
        [SerializeField] private LevelGenerationConfig generationConfig;
        [SerializeField] private PlayerMovementController playerOverride;

        private readonly List<Collider2D> _boundaryColliders = new();
        private readonly List<ReservedSpawnArea> _reservedAreas = new();
        private readonly List<GameObject> _spawnedObjects = new();
        private readonly Collider2D[] _overlapBuffer = new Collider2D[24];

        private PlayerPlaystyleProfile _profile;
        private PlayerMovementController _player;
        private Transform _generatedRoot;
        private System.Random _random;
        private int _currentDifficulty;
        private int _currentEnemyCount;
        private int _currentObjectCount;
        private float _levelStartTime;
        private bool _isGenerating;

        public int CurrentDifficulty => _currentDifficulty;
        public float CurrentRunTime => Time.time - _levelStartTime;

        public void Initialize(SpawnCatalog catalog, LevelGenerationConfig config)
        {
            spawnCatalog = catalog;
            generationConfig = config;
        }

        private void Awake()
        {
            _profile = PlayerPatternMemoryStore.LoadOrCreate();
            EnsureGeneratedRoot();
        }

        private void Start()
        {
            if (spawnCatalog == null || generationConfig == null || !generationConfig.AutoGenerateOnStart)
            {
                return;
            }

            GenerateLevel();
        }

        public void HandleGoalReached(PlayerMovementController player)
        {
            if (_isGenerating || player == null || _player == null || player != _player)
            {
                return;
            }

            var completionTime = Mathf.Max(0f, Time.time - _levelStartTime);
            _profile.RecordLevelCompletion(completionTime, _currentDifficulty);
            PlayerPatternMemoryStore.Save(_profile);

            if (generationConfig != null && generationConfig.RegenerateOnGoalReached)
            {
                GenerateLevel();
            }
        }

        public bool TryGetRandomEnemyRespawnPoint(float radius, out Vector2 respawnPoint)
        {
            respawnPoint = Vector2.zero;

            if (spawnCatalog == null || generationConfig == null)
            {
                return false;
            }

            if (_random == null)
            {
                _random = new System.Random(generationConfig.ResolveSeed());
            }

            if (_player == null)
            {
                _player = playerOverride != null ? playerOverride : FindFirstObjectByType<PlayerMovementController>();
            }

            if (!RefreshBoundaryColliders())
            {
                return false;
            }

            var avoidPosition = _player != null ? (Vector2)_player.transform.position : Vector2.zero;
            var avoidDistance = _player != null ? generationConfig.MinimumPlayerEnemyDistance : 0f;
            return TryFindSpawnPoint(Mathf.Max(radius, generationConfig.MinimumEnemySpacing), avoidPosition, avoidDistance, out respawnPoint);
        }

        [ContextMenu("Generate Level")]
        public void GenerateLevel()
        {
            if (_isGenerating)
            {
                return;
            }

            if (spawnCatalog == null || generationConfig == null)
            {
                Debug.LogWarning("RuntimeLevelGenerator is missing a SpawnCatalog or LevelGenerationConfig asset.");
                return;
            }

            _isGenerating = true;
            try
            {
                EnsureGeneratedRoot();
                ClearGeneratedLevel();

                if (!RefreshBoundaryColliders())
                {
                    Debug.LogWarning("RuntimeLevelGenerator could not find any valid boundary colliders. Add colliders on the Boundary layer or let the generator create a runtime boundary from the scene.");
                    return;
                }

                _random = new System.Random(generationConfig.ResolveSeed());
                _currentDifficulty = generationConfig.CalculateDifficulty(_profile);
                _currentObjectCount = generationConfig.GetEnvironmentObjectCount(_currentDifficulty);
                _currentEnemyCount = generationConfig.GetEnemyCount(_currentDifficulty);
                _profile.RecordGeneratedLevel(_currentDifficulty);
                PlayerPatternMemoryStore.Save(_profile);

                _player = EnsurePlayer();
                if (_player == null)
                {
                    Debug.LogWarning("RuntimeLevelGenerator could not locate or create a player instance.");
                    return;
                }

                _reservedAreas.Clear();
                var playerSpawnRadius = Mathf.Max(generationConfig.MinimumPlayerEnemyDistance * 0.4f, 0.8f);
                if (!TryFindSpawnPoint(playerSpawnRadius, Vector2.zero, 0f, out var playerSpawnPoint))
                {
                    Debug.LogWarning("RuntimeLevelGenerator failed to place the player within the boundary area.");
                    return;
                }

                PositionPlayer(_player.gameObject, playerSpawnPoint);
                ReserveArea(playerSpawnPoint, playerSpawnRadius);

                SpawnEnvironmentObjects();
                SpawnEnemies(playerSpawnPoint);
                SpawnGoal(playerSpawnPoint);

                _levelStartTime = Time.time;
            }
            finally
            {
                _isGenerating = false;
            }
        }

        private void SpawnEnvironmentObjects()
        {
            for (var i = 0; i < _currentObjectCount; i++)
            {
                if (!spawnCatalog.TryGetRandomEnvironment(_random, out var entry) || entry == null || entry.Prefab == null)
                {
                    SpawnFallbackEnvironmentObject(i);
                    continue;
                }

                var scale = entry.GetRandomScale(_random);
                var spawnRadius = Mathf.Max(generationConfig.MinimumEnvironmentSpacing * scale, 0.75f);
                if (!TryFindSpawnPoint(spawnRadius, Vector2.zero, 0f, out var spawnPoint))
                {
                    continue;
                }

                var instance = InstantiatePrefabObject(entry.Prefab, spawnPoint, Quaternion.identity, _generatedRoot, $"environment prefab '{entry.Prefab.name}'");
                if (instance == null)
                {
                    continue;
                }

                instance.name = $"GeneratedObject_{i}_{entry.Prefab.name}";
                instance.transform.localScale = Vector3.one * scale;
                AssignLayerRecursively(instance, spawnCatalog.EnvironmentLayer);
                EnsureEnvironmentCollider(instance, entry.AddColliderIfMissing);
                ApplyObjectTint(instance, new Color(0.75f, 0.9f, 1f, 1f));
                _spawnedObjects.Add(instance);
                ReserveArea(spawnPoint, Mathf.Max(spawnRadius, GetApproximateRadius(instance)));
            }
        }

        private void SpawnFallbackEnvironmentObject(int index)
        {
            var spawnRadius = Mathf.Max(generationConfig.MinimumEnvironmentSpacing, 0.75f);
            if (!TryFindSpawnPoint(spawnRadius, Vector2.zero, 0f, out var spawnPoint))
            {
                return;
            }

            var instance = new GameObject($"GeneratedObstacle_{index}");
            instance.transform.SetParent(_generatedRoot, false);
            instance.transform.position = spawnPoint;
            var lineRenderer = instance.AddComponent<LineRenderer>();
            ConfigureFallbackSquare(lineRenderer, new Color(0.45f, 0.8f, 1f, 0.9f), 0.12f, 0.6f, 4);
            var obstacleCollider = instance.AddComponent<BoxCollider2D>();
            obstacleCollider.size = Vector2.one * 1.2f;
            AssignLayerRecursively(instance, spawnCatalog.EnvironmentLayer);
            _spawnedObjects.Add(instance);
            ReserveArea(spawnPoint, spawnRadius);
        }

        private void SpawnEnemies(Vector2 playerSpawnPoint)
        {
            for (var i = 0; i < _currentEnemyCount; i++)
            {
                if (!spawnCatalog.TryGetRandomEnemy(_random, out var entry) || entry == null || entry.Prefab == null)
                {
                    continue;
                }

                var spawnRadius = Mathf.Max(generationConfig.MinimumEnemySpacing, 1f);
                if (!TryFindSpawnPoint(spawnRadius, playerSpawnPoint, generationConfig.MinimumPlayerEnemyDistance, out var spawnPoint))
                {
                    continue;
                }

                var enemyObject = InstantiatePrefabObject(entry.Prefab, spawnPoint, Quaternion.identity, _generatedRoot, $"enemy prefab '{entry.Prefab.name}'");
                if (enemyObject == null)
                {
                    continue;
                }

                enemyObject.name = $"GeneratedEnemy_{i}_{entry.Prefab.name}";
                enemyObject.transform.localScale = Vector3.one * entry.GetRandomScale(_random);
                PrepareEnemy(enemyObject);
                _spawnedObjects.Add(enemyObject);
                ReserveArea(spawnPoint, Mathf.Max(spawnRadius, GetApproximateRadius(enemyObject)));
            }
        }

        private void SpawnGoal(Vector2 playerSpawnPoint)
        {
            var goalRadius = Mathf.Max(generationConfig.MinimumGoalSpacing, 1f);
            if (!TryFindSpawnPoint(goalRadius, playerSpawnPoint, generationConfig.MinimumPlayerGoalDistance, out var goalPoint))
            {
                goalPoint = playerSpawnPoint + Vector2.right * generationConfig.MinimumPlayerGoalDistance;
            }

            GameObject goalObject;
            if (spawnCatalog.EndGoalPrefab != null)
            {
                goalObject = InstantiatePrefabObject(spawnCatalog.EndGoalPrefab, goalPoint, Quaternion.identity, _generatedRoot, $"goal prefab '{spawnCatalog.EndGoalPrefab.name}'");
                if (goalObject == null)
                {
                    goalObject = new GameObject("GeneratedLevelGoal");
                    goalObject.transform.SetParent(_generatedRoot, false);
                    goalObject.transform.position = goalPoint;
                }
            }
            else
            {
                goalObject = new GameObject("GeneratedLevelGoal");
                goalObject.transform.SetParent(_generatedRoot, false);
                goalObject.transform.position = goalPoint;
            }

            goalObject.name = "GeneratedLevelGoal";
            AssignLayerRecursively(goalObject, spawnCatalog.GoalLayer);
            var completionTrigger = goalObject.GetComponent<LevelCompletionTrigger>() ?? goalObject.AddComponent<LevelCompletionTrigger>();
            completionTrigger.Initialize(this);
            _spawnedObjects.Add(goalObject);
            ReserveArea(goalPoint, goalRadius);
        }

        private PlayerMovementController EnsurePlayer()
        {
            if (playerOverride != null)
            {
                PreparePlayer(playerOverride.gameObject);
                return playerOverride;
            }

            var existingPlayer = FindFirstObjectByType<PlayerMovementController>();
            if (existingPlayer != null)
            {
                PreparePlayer(existingPlayer.gameObject);
                return existingPlayer;
            }

            if (spawnCatalog.PlayerPrefab == null)
            {
                return null;
            }

            var playerObject = InstantiatePrefabObject(spawnCatalog.PlayerPrefab, Vector3.zero, Quaternion.identity, null, $"player prefab '{spawnCatalog.PlayerPrefab.name}'");
            if (playerObject == null)
            {
                return null;
            }

            playerObject.name = "GeneratedPlayer";
            PreparePlayer(playerObject);
            return playerObject.GetComponent<PlayerMovementController>();
        }

        private void PreparePlayer(GameObject playerObject)
        {
            if (playerObject == null)
            {
                return;
            }

            EnsureGameplayBody(playerObject);
            if (playerObject.GetComponent<PlayerMovementController>() == null)
            {
                playerObject.AddComponent<PlayerMovementController>();
            }

            if (playerObject.GetComponent<PlayerCombatController>() == null)
            {
                playerObject.AddComponent<PlayerCombatController>();
            }

            ResetCombatState(playerObject);
            AssignLayerRecursively(playerObject, spawnCatalog.PlayerLayer);
        }

        private void PrepareEnemy(GameObject enemyObject)
        {
            if (enemyObject == null)
            {
                return;
            }

            EnsureGameplayBody(enemyObject);
            if (enemyObject.GetComponent<EnemyController>() == null)
            {
                enemyObject.AddComponent<EnemyController>();
            }

            if (enemyObject.GetComponent<EnemyCombatController>() == null)
            {
                enemyObject.AddComponent<EnemyCombatController>();
            }

            if (enemyObject.GetComponent<EnemyRespawnController>() == null)
            {
                enemyObject.AddComponent<EnemyRespawnController>();
            }

            ResetCombatState(enemyObject);
            AssignLayerRecursively(enemyObject, spawnCatalog.EnemyLayer);
        }

        private static void EnsureGameplayBody(GameObject target)
        {
            if (target.GetComponent<Rigidbody2D>() == null)
            {
                var body = target.AddComponent<Rigidbody2D>();
                body.gravityScale = 0f;
                body.freezeRotation = true;
            }
            else
            {
                var body = target.GetComponent<Rigidbody2D>();
                body.gravityScale = 0f;
                body.freezeRotation = true;
            }

            if (target.GetComponent<Collider2D>() == null)
            {
                var collider = target.AddComponent<CircleCollider2D>();
                collider.radius = 0.3f;
            }

            if (target.GetComponent<Health2D>() == null)
            {
                target.AddComponent<Health2D>();
            }

            if (target.GetComponent<Hurtbox2D>() == null)
            {
                target.AddComponent<Hurtbox2D>();
            }

            if (target.GetComponent<HitReaction2D>() == null)
            {
                target.AddComponent<HitReaction2D>();
            }

            if (target.GetComponent<Shield2D>() == null)
            {
                target.AddComponent<Shield2D>();
            }
        }

        private static void ResetCombatState(GameObject target)
        {
            var health = target.GetComponent<Health2D>();
            health?.ResetHealth();
            var hitReaction = target.GetComponent<HitReaction2D>();
            hitReaction?.ClearReaction();
            var shield = target.GetComponent<Shield2D>();
            shield?.ResetShield();
        }

        private void PositionPlayer(GameObject playerObject, Vector2 position)
        {
            playerObject.transform.SetPositionAndRotation(position, Quaternion.identity);

            var body = playerObject.GetComponent<Rigidbody2D>();
            if (body != null)
            {
                body.linearVelocity = Vector2.zero;
                body.angularVelocity = 0f;
            }

            ResetCombatState(playerObject);
        }

        private static GameObject InstantiatePrefabObject(UnityEngine.Object prefabObject, Vector3 position, Quaternion rotation, Transform parent, string context)
        {
            if (prefabObject == null)
            {
                return null;
            }

            UnityEngine.Object instance;
            try
            {
                instance = parent != null
                    ? UnityEngine.Object.Instantiate(prefabObject, position, rotation, parent)
                    : UnityEngine.Object.Instantiate(prefabObject, position, rotation);
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning($"RuntimeLevelGenerator failed to instantiate {context}. {exception.GetType().Name}: {exception.Message}");
                return null;
            }

            if (instance is GameObject gameObject)
            {
                return gameObject;
            }

            if (instance is Component component)
            {
                return component.gameObject;
            }

            Debug.LogWarning($"RuntimeLevelGenerator instantiated {context}, but the result was not a GameObject or Component. Actual type: {instance.GetType().Name}");
            if (instance != null)
            {
                UnityEngine.Object.Destroy(instance);
            }

            return null;
        }

        private void ClearGeneratedLevel()
        {
            for (var i = _spawnedObjects.Count - 1; i >= 0; i--)
            {
                var spawnedObject = _spawnedObjects[i];
                if (spawnedObject != null)
                {
                    Destroy(spawnedObject);
                }
            }

            _spawnedObjects.Clear();

            var existingEnemies = FindObjectsByType<EnemyController>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (var i = 0; i < existingEnemies.Length; i++)
            {
                if (existingEnemies[i] == null)
                {
                    continue;
                }

                if (_generatedRoot != null && existingEnemies[i].transform.IsChildOf(_generatedRoot))
                {
                    continue;
                }

                Destroy(existingEnemies[i].gameObject);
            }

            var existingGoals = FindObjectsByType<LevelCompletionTrigger>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (var i = 0; i < existingGoals.Length; i++)
            {
                if (existingGoals[i] == null)
                {
                    continue;
                }

                if (_generatedRoot != null && existingGoals[i].transform.IsChildOf(_generatedRoot))
                {
                    continue;
                }

                Destroy(existingGoals[i].gameObject);
            }
        }

        private bool RefreshBoundaryColliders()
        {
            _boundaryColliders.Clear();
            var colliders = FindObjectsByType<Collider2D>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (var i = 0; i < colliders.Length; i++)
            {
                var boundaryCollider = colliders[i];
                if (boundaryCollider == null || !boundaryCollider.enabled)
                {
                    continue;
                }

                if (((1 << boundaryCollider.gameObject.layer) & spawnCatalog.BoundaryLayer.value) == 0)
                {
                    continue;
                }

                _boundaryColliders.Add(boundaryCollider);
            }

            if (_boundaryColliders.Count > 0)
            {
                return true;
            }

            CreateRuntimeBoundaryIfMissing();

            colliders = FindObjectsByType<Collider2D>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (var i = 0; i < colliders.Length; i++)
            {
                var boundaryCollider = colliders[i];
                if (boundaryCollider == null || !boundaryCollider.enabled)
                {
                    continue;
                }

                if (((1 << boundaryCollider.gameObject.layer) & spawnCatalog.BoundaryLayer.value) == 0)
                {
                    continue;
                }

                _boundaryColliders.Add(boundaryCollider);
            }

            return _boundaryColliders.Count > 0;
        }

        private void CreateRuntimeBoundaryIfMissing()
        {
            var renderers = FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            if (renderers.Length == 0)
            {
                return;
            }

            var bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++)
            {
                if (renderers[i] == null)
                {
                    continue;
                }

                bounds.Encapsulate(renderers[i].bounds);
            }

            bounds.Expand(new Vector3(-generationConfig.BoundaryInset * 2f, -generationConfig.BoundaryInset * 2f, 0f));
            var boundaryObject = new GameObject("GeneratedBoundary");
            boundaryObject.transform.SetParent(transform, false);
            boundaryObject.transform.position = bounds.center;
            boundaryObject.layer = GetPrimaryLayerIndex(spawnCatalog.BoundaryLayer);
            var boundaryBoxCollider = boundaryObject.AddComponent<BoxCollider2D>();
            boundaryBoxCollider.isTrigger = true;
            boundaryBoxCollider.size = new Vector2(Mathf.Max(bounds.size.x, 4f), Mathf.Max(bounds.size.y, 4f));
        }

        private bool TryFindSpawnPoint(float radius, Vector2 avoidPosition, float avoidDistance, out Vector2 result)
        {
            result = Vector2.zero;
            if (_boundaryColliders.Count == 0)
            {
                return false;
            }

            for (var attempt = 0; attempt < generationConfig.SpawnAttemptsPerEntity * Mathf.Max(1, _boundaryColliders.Count); attempt++)
            {
                var boundary = _boundaryColliders[_random.Next(0, _boundaryColliders.Count)];
                if (boundary == null)
                {
                    continue;
                }

                var bounds = boundary.bounds;
                var inset = generationConfig.BoundaryInset + radius;
                var minX = bounds.min.x + inset;
                var maxX = bounds.max.x - inset;
                var minY = bounds.min.y + inset;
                var maxY = bounds.max.y - inset;
                if (minX >= maxX || minY >= maxY)
                {
                    continue;
                }

                var candidate = new Vector2(
                    Mathf.Lerp(minX, maxX, (float)_random.NextDouble()),
                    Mathf.Lerp(minY, maxY, (float)_random.NextDouble()));

                if (!boundary.OverlapPoint(candidate))
                {
                    continue;
                }

                if (avoidDistance > 0f && Vector2.Distance(candidate, avoidPosition) < avoidDistance)
                {
                    continue;
                }

                if (!IsAreaFree(candidate, radius))
                {
                    continue;
                }

                result = candidate;
                return true;
            }

            return false;
        }

        private bool IsAreaFree(Vector2 position, float radius)
        {
            for (var i = 0; i < _reservedAreas.Count; i++)
            {
                var reservedArea = _reservedAreas[i];
                if (Vector2.Distance(position, reservedArea.Position) < radius + reservedArea.Radius)
                {
                    return false;
                }
            }

            var hitCount = Physics2D.OverlapCircle(position, radius, new ContactFilter2D
            {
                useLayerMask = true,
                layerMask = spawnCatalog.PlacementBlockingLayers,
                useTriggers = false
            }, _overlapBuffer);

            for (var i = 0; i < hitCount; i++)
            {
                var hitCollider = _overlapBuffer[i];
                if (hitCollider == null)
                {
                    continue;
                }

                if (((1 << hitCollider.gameObject.layer) & spawnCatalog.BoundaryLayer.value) != 0)
                {
                    continue;
                }

                if (_player != null && hitCollider.transform.IsChildOf(_player.transform))
                {
                    continue;
                }

                return false;
            }

            return true;
        }

        private void ReserveArea(Vector2 position, float radius)
        {
            _reservedAreas.Add(new ReservedSpawnArea
            {
                Position = position,
                Radius = Mathf.Max(radius, 0.1f)
            });
        }

        private void EnsureGeneratedRoot()
        {
            if (_generatedRoot != null)
            {
                return;
            }

            var existingRoot = transform.Find("GeneratedLevelRoot");
            if (existingRoot != null)
            {
                _generatedRoot = existingRoot;
                return;
            }

            var rootObject = new GameObject("GeneratedLevelRoot");
            rootObject.transform.SetParent(transform, false);
            _generatedRoot = rootObject.transform;
        }

        private static void AssignLayerRecursively(GameObject gameObject, int layer)
        {
            if (gameObject == null || layer < 0 || layer > 31)
            {
                return;
            }

            gameObject.layer = layer;
            foreach (Transform child in gameObject.transform)
            {
                AssignLayerRecursively(child.gameObject, layer);
            }
        }

        private static int GetPrimaryLayerIndex(LayerMask mask)
        {
            var bits = mask.value;
            for (var i = 0; i < 32; i++)
            {
                if ((bits & (1 << i)) != 0)
                {
                    return i;
                }
            }

            return 0;
        }

        private static float GetApproximateRadius(GameObject gameObject)
        {
            if (gameObject == null)
            {
                return 0.5f;
            }

            var collider = gameObject.GetComponent<Collider2D>();
            if (collider != null)
            {
                return Mathf.Max(collider.bounds.extents.x, collider.bounds.extents.y);
            }

            var renderer = gameObject.GetComponentInChildren<Renderer>();
            return renderer != null ? Mathf.Max(renderer.bounds.extents.x, renderer.bounds.extents.y) : 0.5f;
        }

        private static void EnsureEnvironmentCollider(GameObject gameObject, bool addColliderIfMissing)
        {
            if (gameObject == null || !addColliderIfMissing || gameObject.GetComponent<Collider2D>() != null)
            {
                return;
            }

            var renderer = gameObject.GetComponentInChildren<Renderer>();
            var collider = gameObject.AddComponent<BoxCollider2D>();
            if (renderer != null)
            {
                collider.size = new Vector2(
                    Mathf.Max(renderer.bounds.size.x / Mathf.Max(gameObject.transform.lossyScale.x, 0.001f), 0.5f),
                    Mathf.Max(renderer.bounds.size.y / Mathf.Max(gameObject.transform.lossyScale.y, 0.001f), 0.5f));
            }
        }

        private static void ApplyObjectTint(GameObject gameObject, Color tint)
        {
            var spriteRenderer = gameObject.GetComponent<SpriteRenderer>() ?? gameObject.GetComponentInChildren<SpriteRenderer>();
            if (spriteRenderer == null)
            {
                return;
            }

            spriteRenderer.color *= tint;
        }

        private static void ConfigureFallbackSquare(LineRenderer renderer, Color color, float width, float size, int sortingOrder)
        {
            renderer.material = GetSharedMaterial();
            renderer.loop = true;
            renderer.useWorldSpace = false;
            renderer.positionCount = 4;
            renderer.widthMultiplier = width;
            renderer.startColor = color;
            renderer.endColor = color;
            renderer.sortingOrder = sortingOrder;
            renderer.SetPosition(0, new Vector3(-size, -size, 0f));
            renderer.SetPosition(1, new Vector3(-size, size, 0f));
            renderer.SetPosition(2, new Vector3(size, size, 0f));
            renderer.SetPosition(3, new Vector3(size, -size, 0f));
        }

        private static Material GetSharedMaterial()
        {
            var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/2D/Sprite-Lit-Default");
            return shader != null ? new Material(shader) : new Material(Shader.Find("Standard"));
        }
    }
}


