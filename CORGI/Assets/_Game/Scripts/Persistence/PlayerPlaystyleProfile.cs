using System;
using System.Collections.Generic;
using UnityEngine;

namespace _Game.Scripts.Persistence
{
    [Serializable]
    public class PlayerPlaystyleProfile
    {
        public const int CurrentVersion = 1;
        private const int MaxTrackedPatterns = 32;

        [SerializeField] private int version = CurrentVersion;
        [SerializeField] private float clockwiseOrbitWeight;
        [SerializeField] private float counterClockwiseOrbitWeight;
        [SerializeField] private List<AttackPatternEntry> attackPatterns = new();

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

        [Serializable]
        public class AttackPatternEntry
        {
            public string Signature;
            public int Count;
            public float LastSeenTime;
        }
    }
}

