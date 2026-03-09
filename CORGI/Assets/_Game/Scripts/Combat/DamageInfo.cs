using UnityEngine;

namespace _Game.Scripts.Combat
{
    public readonly struct DamageInfo
    {
        public readonly GameObject Source;
        public readonly int AttackId;
        public readonly float Amount;
        public readonly Vector2 Direction;
        public readonly Vector2 Point;
        public readonly string AttackSignature;
        public readonly bool IsCounterAttack;

        public DamageInfo(GameObject source, int attackId, float amount, Vector2 direction, Vector2 point, string attackSignature, bool isCounterAttack)
        {
            Source = source;
            AttackId = attackId;
            Amount = amount;
            Direction = direction;
            Point = point;
            AttackSignature = attackSignature;
            IsCounterAttack = isCounterAttack;
        }
    }
}

