using _Game.Scripts.Persistence;
using UnityEngine;

namespace _Game.Scripts.Generation
{
    [CreateAssetMenu(fileName = "LevelGenerationConfig", menuName = "CORGI/Generation/Level Generation Config")]
    public class LevelGenerationConfig : ScriptableObject
    {
        [Header("Generation Flow")]
        [SerializeField] private bool autoGenerateOnStart = true;
        [SerializeField] private bool regenerateOnGoalReached = true;
        [SerializeField] private bool randomizeSeed = true;
        [SerializeField] private int fixedSeed = 12345;

        [Header("Base Counts")]
        [SerializeField, Min(1)] private int baseDifficulty = 1;
        [SerializeField, Min(0)] private int baseEnvironmentObjectCount = 10;
        [SerializeField, Min(1)] private int baseEnemyCount = 2;
        [SerializeField, Min(0f)] private float environmentObjectsPerDifficulty = 2f;
        [SerializeField, Min(0f)] private float enemiesPerDifficulty = 1f;
        [SerializeField, Min(0)] private int maxExtraEnvironmentObjects = 16;
        [SerializeField, Min(0)] private int maxExtraEnemies = 10;

        [Header("Placement")]
        [SerializeField, Min(4)] private int spawnAttemptsPerEntity = 32;
        [SerializeField, Min(0f)] private float boundaryInset = 0.75f;
        [SerializeField, Min(0.1f)] private float minimumPlayerEnemyDistance = 4f;
        [SerializeField, Min(0.1f)] private float minimumPlayerGoalDistance = 8f;
        [SerializeField, Min(0.1f)] private float minimumEnvironmentSpacing = 1.25f;
        [SerializeField, Min(0.1f)] private float minimumEnemySpacing = 1.2f;
        [SerializeField, Min(0.1f)] private float minimumGoalSpacing = 1.5f;

        [Header("Adaptive Difficulty")]
        [SerializeField, Min(0f)] private float difficultyGainPerGeneratedLevel = 0.15f;
        [SerializeField, Min(0f)] private float difficultyGainPerCompletedLevel = 0.25f;
        [SerializeField, Min(0f)] private float completionBaselineSeconds = 45f;
        [SerializeField, Min(0f)] private float fastCompletionDifficultyBonus = 0.9f;
        [SerializeField, Min(0f)] private float dominantAttackDifficultyBonus = 0.45f;
        [SerializeField, Min(0f)] private float orbitDifficultyBonus = 0.25f;

        public bool AutoGenerateOnStart => autoGenerateOnStart;
        public bool RegenerateOnGoalReached => regenerateOnGoalReached;
        public int SpawnAttemptsPerEntity => spawnAttemptsPerEntity;
        public float BoundaryInset => boundaryInset;
        public float MinimumPlayerEnemyDistance => minimumPlayerEnemyDistance;
        public float MinimumPlayerGoalDistance => minimumPlayerGoalDistance;
        public float MinimumEnvironmentSpacing => minimumEnvironmentSpacing;
        public float MinimumEnemySpacing => minimumEnemySpacing;
        public float MinimumGoalSpacing => minimumGoalSpacing;

        public int ResolveSeed()
        {
            return randomizeSeed ? Random.Range(int.MinValue, int.MaxValue) : fixedSeed;
        }

        public int CalculateDifficulty(PlayerPlaystyleProfile profile)
        {
            var difficultyScore = (float)baseDifficulty;
            if (profile == null)
            {
                return Mathf.Max(1, Mathf.RoundToInt(difficultyScore));
            }

            difficultyScore += profile.TotalGeneratedLevels * difficultyGainPerGeneratedLevel;
            difficultyScore += profile.TotalCompletedLevels * difficultyGainPerCompletedLevel;

            if (profile.AverageCompletionTime > 0.001f && completionBaselineSeconds > 0.001f)
            {
                var fastCompletionRatio = Mathf.Clamp01((completionBaselineSeconds - profile.AverageCompletionTime) / completionBaselineSeconds);
                difficultyScore += fastCompletionRatio * fastCompletionDifficultyBonus;
            }

            difficultyScore += profile.GetDominantAttackPatternRatio() * dominantAttackDifficultyBonus;
            difficultyScore += Mathf.Abs(profile.PersistentOrbitBias) * orbitDifficultyBonus;

            return Mathf.Max(1, Mathf.RoundToInt(difficultyScore));
        }

        public int GetEnvironmentObjectCount(int difficulty)
        {
            var extraObjects = Mathf.Min(maxExtraEnvironmentObjects, Mathf.RoundToInt(Mathf.Max(0, difficulty - baseDifficulty) * environmentObjectsPerDifficulty));
            return Mathf.Max(0, baseEnvironmentObjectCount + extraObjects);
        }

        public int GetEnemyCount(int difficulty)
        {
            var extraEnemies = Mathf.Min(maxExtraEnemies, Mathf.RoundToInt(Mathf.Max(0, difficulty - baseDifficulty) * enemiesPerDifficulty));
            return Mathf.Max(1, baseEnemyCount + extraEnemies);
        }
    }
}


