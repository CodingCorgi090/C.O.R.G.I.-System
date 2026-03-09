using UnityEngine;

namespace _Game.Scripts
{
    public enum PlayerAttackStyle
    {
        Neutral,
        Horizontal,
        Vertical,
        Diagonal,
        Rush
    }

    public readonly struct PlayerAttackData
    {
        public readonly int AttackId;
        public readonly PlayerAttackStyle Style;
        public readonly Vector2 Direction;
        public readonly Vector2 PlayerPosition;
        public readonly float Time;
        public readonly bool IsSprinting;
        public readonly float Damage;
        public readonly float Range;
        public readonly float Radius;

        public PlayerAttackData(
            int attackId,
            PlayerAttackStyle style,
            Vector2 direction,
            Vector2 playerPosition,
            float time,
            bool isSprinting,
            float damage,
            float range,
            float radius)
        {
            AttackId = attackId;
            Style = style;
            Direction = direction;
            PlayerPosition = playerPosition;
            Time = time;
            IsSprinting = isSprinting;
            Damage = damage;
            Range = range;
            Radius = radius;
        }

        public string Signature => $"{Style}:{Mathf.RoundToInt(Direction.x)}:{Mathf.RoundToInt(Direction.y)}:{(IsSprinting ? 1 : 0)}";
    }
}
