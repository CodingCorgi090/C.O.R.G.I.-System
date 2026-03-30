using System.Collections;
using _Game.Scripts.Combat;
using _Game.Scripts.Generation;
using UnityEngine;

namespace _Game.Scripts
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Health2D))]
    public class EnemyRespawnController : MonoBehaviour
    {
        [SerializeField] private Health2D health;
        [SerializeField] private Rigidbody2D rb;
        [SerializeField] private Collider2D respawnCollider;
        [SerializeField] private HitReaction2D hitReaction;
        [SerializeField] private Shield2D shield;
        [SerializeField, Min(0f)] private float respawnDelay = 3f;
        [SerializeField] private bool respawnOnDeath = true;
        [SerializeField] private bool randomizeRespawnPosition = true;
        [SerializeField] private bool useInitialTransformAsSpawnPoint = true;

        private Vector3 _spawnPosition;
        private Quaternion _spawnRotation;
        private bool _respawnQueued;

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

            if (respawnCollider == null)
            {
                respawnCollider = GetComponent<Collider2D>();
            }

            if (hitReaction == null)
            {
                hitReaction = GetComponent<HitReaction2D>();
            }

            if (shield == null)
            {
                shield = GetComponent<Shield2D>();
            }

            CacheSpawnTransform();
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

            health.Died -= HandleDied;
            health.Died += HandleDied;
        }

        private void OnDisable()
        {
            if (health == null)
            {
                return;
            }

            health.Died -= HandleDied;
        }

        [ContextMenu("Set Current Transform As Spawn Point")]
        public void CacheSpawnTransform()
        {
            _spawnPosition = transform.position;
            _spawnRotation = transform.rotation;
        }

        private void HandleDied(DamageInfo damageInfo)
        {
            if (!respawnOnDeath || _respawnQueued)
            {
                return;
            }

            if (useInitialTransformAsSpawnPoint && _spawnRotation == default)
            {
                CacheSpawnTransform();
            }

            _respawnQueued = true;
            RespawnDispatcher.Run(RespawnAfterDelay());
        }

        private IEnumerator RespawnAfterDelay()
        {
            if (respawnDelay > 0f)
            {
                yield return new WaitForSeconds(respawnDelay);
            }

            if (this == null || gameObject == null)
            {
                yield break;
            }

            if (!gameObject.scene.IsValid() || !gameObject.scene.isLoaded)
            {
                yield break;
            }

            var respawnPosition = ResolveRespawnPosition();
            transform.SetPositionAndRotation(respawnPosition, _spawnRotation);

            if (rb != null)
            {
                rb.linearVelocity = Vector2.zero;
                rb.angularVelocity = 0f;
            }

            health?.ResetHealth();
            _respawnQueued = false;

            if (!gameObject.activeSelf)
            {
                gameObject.SetActive(true);
            }

            hitReaction?.ClearReaction();
            shield?.ResetShield();
        }

        private Vector3 ResolveRespawnPosition()
        {
            if (!randomizeRespawnPosition)
            {
                return _spawnPosition;
            }

            var levelGenerator = FindFirstObjectByType<RuntimeLevelGenerator>();
            if (levelGenerator == null)
            {
                return _spawnPosition;
            }

            var respawnRadius = 1f;
            if (respawnCollider != null)
            {
                var bounds = respawnCollider.bounds;
                respawnRadius = Mathf.Max(bounds.extents.x, bounds.extents.y, 0.6f);
            }

            return levelGenerator.TryGetRandomEnemyRespawnPoint(respawnRadius, out var randomRespawnPoint)
                ? (Vector3)randomRespawnPoint
                : _spawnPosition;
        }

        private sealed class RespawnDispatcher : MonoBehaviour
        {
            private static RespawnDispatcher _instance;

            public static void Run(IEnumerator routine)
            {
                if (routine == null)
                {
                    return;
                }

                EnsureInstance().StartCoroutine(routine);
            }

            private static RespawnDispatcher EnsureInstance()
            {
                if (_instance != null)
                {
                    return _instance;
                }

                var hostObject = new GameObject("EnemyRespawnDispatcher")
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                DontDestroyOnLoad(hostObject);
                _instance = hostObject.AddComponent<RespawnDispatcher>();
                return _instance;
            }
        }
    }
}

