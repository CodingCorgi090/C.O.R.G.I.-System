using System;
using System.Collections.Generic;
using UnityEngine;

namespace _Game.Scripts.Generation
{
    [CreateAssetMenu(fileName = "SpawnCatalog", menuName = "CORGI/Generation/Spawn Catalog")]
    public class SpawnCatalog : ScriptableObject
    {
        [Serializable]
        public class WeightedPrefabEntry
        {
            [SerializeField] private GameObject prefab;
            [SerializeField, Min(1)] private int weight = 1;
            [SerializeField, Min(0.1f)] private float minimumScale = 1f;
            [SerializeField, Min(0.1f)] private float maximumScale = 1f;
            [SerializeField] private bool addColliderIfMissing = true;

            public GameObject Prefab => prefab;
            public int Weight => Mathf.Max(1, weight);
            public float MinimumScale => minimumScale;
            public float MaximumScale => Mathf.Max(minimumScale, maximumScale);
            public bool AddColliderIfMissing => addColliderIfMissing;

            public float GetRandomScale(System.Random random)
            {
                if (random == null)
                {
                    return MaximumScale;
                }

                var t = (float)random.NextDouble();
                return Mathf.Lerp(MinimumScale, MaximumScale, t);
            }
        }

        [Header("Prefab Assets")]
        [SerializeField] private GameObject playerPrefab;
        [SerializeField] private GameObject endGoalPrefab;
        [SerializeField] private List<WeightedPrefabEntry> environmentPrefabs = new();
        [SerializeField] private List<WeightedPrefabEntry> enemyPrefabs = new();

        [Header("Layer Assets")]
        [SerializeField] private LayerMask boundaryLayer = 1 << 6;
        [SerializeField] private LayerMask placementBlockingLayers = ~0;
        [SerializeField] private int playerLayer;
        [SerializeField] private int enemyLayer;
        [SerializeField] private int environmentLayer;
        [SerializeField] private int goalLayer;

        public GameObject PlayerPrefab => playerPrefab;
        public GameObject EndGoalPrefab => endGoalPrefab;
        public LayerMask BoundaryLayer => boundaryLayer;
        public LayerMask PlacementBlockingLayers => placementBlockingLayers;
        public int PlayerLayer => playerLayer;
        public int EnemyLayer => enemyLayer;
        public int EnvironmentLayer => environmentLayer;
        public int GoalLayer => goalLayer;
        public IReadOnlyList<WeightedPrefabEntry> EnvironmentPrefabs => environmentPrefabs;
        public IReadOnlyList<WeightedPrefabEntry> EnemyPrefabs => enemyPrefabs;

        public bool TryGetRandomEnvironment(System.Random random, out WeightedPrefabEntry entry)
        {
            return TryGetRandomEntry(environmentPrefabs, random, out entry);
        }

        public bool TryGetRandomEnemy(System.Random random, out WeightedPrefabEntry entry)
        {
            return TryGetRandomEntry(enemyPrefabs, random, out entry);
        }

        public bool HasEnvironmentPrefabs()
        {
            return HasValidPrefab(environmentPrefabs);
        }

        public bool HasEnemyPrefabs()
        {
            return HasValidPrefab(enemyPrefabs);
        }

        private static bool TryGetRandomEntry(IReadOnlyList<WeightedPrefabEntry> entries, System.Random random, out WeightedPrefabEntry entry)
        {
            entry = null;
            if (entries == null || entries.Count == 0)
            {
                return false;
            }

            var totalWeight = 0;
            for (var i = 0; i < entries.Count; i++)
            {
                if (entries[i] == null || entries[i].Prefab == null)
                {
                    continue;
                }

                totalWeight += entries[i].Weight;
            }

            if (totalWeight <= 0)
            {
                return false;
            }

            var roll = random != null ? random.Next(0, totalWeight) : UnityEngine.Random.Range(0, totalWeight);
            for (var i = 0; i < entries.Count; i++)
            {
                var candidate = entries[i];
                if (candidate == null || candidate.Prefab == null)
                {
                    continue;
                }

                roll -= candidate.Weight;
                if (roll >= 0)
                {
                    continue;
                }

                entry = candidate;
                return true;
            }

            return false;
        }

        private static bool HasValidPrefab(IReadOnlyList<WeightedPrefabEntry> entries)
        {
            if (entries == null)
            {
                return false;
            }

            for (var i = 0; i < entries.Count; i++)
            {
                if (entries[i] != null && entries[i].Prefab != null)
                {
                    return true;
                }
            }

            return false;
        }
    }
}

