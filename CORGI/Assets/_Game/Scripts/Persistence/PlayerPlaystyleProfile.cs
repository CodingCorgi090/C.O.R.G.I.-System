using System;
using System.Collections.Generic;
using UnityEngine;

namespace _Game.Scripts.Persistence
{
    [Serializable]
    public class PlayerPlaystyleProfile
    {
        public const int CurrentVersion = 2;
        private const int MaxTrackedPatterns = 32;

        [SerializeField] private int version = CurrentVersion;
        [SerializeField] private float clockwiseOrbitWeight;
        [SerializeField] private float counterClockwiseOrbitWeight;
        [SerializeField] private List<AttackPatternEntry> attackPatterns = new();
        [SerializeField] private int totalGeneratedLevels;
        [SerializeField] private int totalCompletedLevels;
        [SerializeField] private float totalCompletionTime;
        [SerializeField] private float lastCompletionTime;
        [SerializeField] private float bestCompletionTime = -1f;
        [SerializeField] private int lastGeneratedDifficulty = 1;
        [SerializeField] private int highestGeneratedDifficulty = 1;

        public int Version => version;
        public float PersistentOrbitBias
        {
            get
            {
                var total = clockwiseOrbitWeight + counterClockwiseOrbitWeight;
                return total > 0.001f ? (counterClockwiseOrbitWeight - clockwiseOrbitWeight) / total : 0f;
            }
        }

        public IReadOnlyList<AttackPatternEntry> AttackPatterns => attackPatterns;
        public int TotalGeneratedLevels => totalGeneratedLevels;
        public int TotalCompletedLevels => totalCompletedLevels;
        public float TotalCompletionTime => totalCompletionTime;
        public float LastCompletionTime => lastCompletionTime;
        public float BestCompletionTime => bestCompletionTime > 0f ? bestCompletionTime : 0f;
        public float AverageCompletionTime => totalCompletedLevels > 0 ? totalCompletionTime / totalCompletedLevels : 0f;
        public int LastGeneratedDifficulty => Mathf.Max(1, lastGeneratedDifficulty);
        public int HighestGeneratedDifficulty => Mathf.Max(1, highestGeneratedDifficulty);

        public void EnsureVersion()
        {
            version = CurrentVersion;
            attackPatterns ??= new List<AttackPatternEntry>();
        }

        public void RecordOrbitBias(float orbitBias, float weight = 1f)
        {
            EnsureVersion();
            var absoluteBias = Mathf.Abs(orbitBias) * Mathf.Max(weight, 0f);
            if (absoluteBias <= 0f)
            {
                return;
            }

            if (orbitBias >= 0f)
            {
                counterClockwiseOrbitWeight += absoluteBias;
            }
            else
            {
                clockwiseOrbitWeight += absoluteBias;
            }
        }

        public void RecordAttackSignature(string signature, float time)
        {
            EnsureVersion();
            if (string.IsNullOrWhiteSpace(signature))
            {
                return;
            }

            for (var i = 0; i < attackPatterns.Count; i++)
            {
                if (!string.Equals(attackPatterns[i].Signature, signature, StringComparison.Ordinal))
                {
                    continue;
                }

                attackPatterns[i].Count++;
                attackPatterns[i].LastSeenTime = time;
                return;
            }

            if (attackPatterns.Count >= MaxTrackedPatterns)
            {
                attackPatterns.Sort((left, right) => left.Count.CompareTo(right.Count));
                attackPatterns.RemoveAt(0);
            }

            attackPatterns.Add(new AttackPatternEntry
            {
                Signature = signature,
                Count = 1,
                LastSeenTime = time
            });
        }

        public int GetAttackUsageCount(string signature)
        {
            if (string.IsNullOrWhiteSpace(signature) || attackPatterns == null)
            {
                return 0;
            }

            for (var i = 0; i < attackPatterns.Count; i++)
            {
                if (string.Equals(attackPatterns[i].Signature, signature, StringComparison.Ordinal))
                {
                    return attackPatterns[i].Count;
                }
            }

            return 0;
        }

        public string GetMostUsedAttackSignature(int minimumCount = 1)
        {
            if (attackPatterns == null || attackPatterns.Count == 0)
            {
                return string.Empty;
            }

            AttackPatternEntry bestMatch = null;
            for (var i = 0; i < attackPatterns.Count; i++)
            {
                if (attackPatterns[i].Count < minimumCount)
                {
                    continue;
                }

                if (bestMatch == null || attackPatterns[i].Count > bestMatch.Count)
                {
                    bestMatch = attackPatterns[i];
                }
            }

            return bestMatch?.Signature ?? string.Empty;
        }

        public float GetDominantAttackPatternRatio()
        {
            if (attackPatterns == null || attackPatterns.Count == 0)
            {
                return 0f;
            }

            var totalCount = 0;
            var highestCount = 0;
            for (var i = 0; i < attackPatterns.Count; i++)
            {
                totalCount += Mathf.Max(0, attackPatterns[i].Count);
                highestCount = Mathf.Max(highestCount, attackPatterns[i].Count);
            }

            return totalCount > 0 ? highestCount / (float)totalCount : 0f;
        }

        public void RecordGeneratedLevel(int difficulty)
        {
            EnsureVersion();
            totalGeneratedLevels++;
            lastGeneratedDifficulty = Mathf.Max(1, difficulty);
            highestGeneratedDifficulty = Mathf.Max(highestGeneratedDifficulty, lastGeneratedDifficulty);
        }

        public void RecordLevelCompletion(float completionTime, int difficulty)
        {
            EnsureVersion();
            var sanitizedTime = Mathf.Max(0f, completionTime);
            totalCompletedLevels++;
            totalCompletionTime += sanitizedTime;
            lastCompletionTime = sanitizedTime;
            bestCompletionTime = bestCompletionTime <= 0f ? sanitizedTime : Mathf.Min(bestCompletionTime, sanitizedTime);
            highestGeneratedDifficulty = Mathf.Max(highestGeneratedDifficulty, Mathf.Max(1, difficulty));
        }

        [Serializable]
        public class AttackPatternEntry
        {
            public string Signature;
            public int Count;
            public float LastSeenTime;
        }
    }
}

