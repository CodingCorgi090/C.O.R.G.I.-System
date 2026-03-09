using UnityEngine;

namespace _Game.Scripts.Combat
{
    [DisallowMultipleComponent]
    public class Hurtbox2D : MonoBehaviour
    {
        [SerializeField] private Health2D health;
        [SerializeField] private Collider2D hurtboxCollider;

        public Health2D Health => health;
        public Collider2D HurtboxCollider => hurtboxCollider;

        private void Awake()
        {
            if (health == null)
            {
                health = GetComponentInParent<Health2D>();
            }

            if (hurtboxCollider == null)
            {
                hurtboxCollider = GetComponent<Collider2D>();
            }
        }

        public bool ApplyHit(DamageInfo damageInfo)
        {
            return health != null && health.TryApplyDamage(damageInfo);
        }
    }
}

