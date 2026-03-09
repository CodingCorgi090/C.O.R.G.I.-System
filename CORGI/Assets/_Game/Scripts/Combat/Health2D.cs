using System;
using UnityEngine;

namespace _Game.Scripts.Combat
{
    [DisallowMultipleComponent]
    public class Health2D : MonoBehaviour
    {
        [SerializeField, Min(1f)] private float maxHealth = 100f;
        [SerializeField, Min(0f)] private float invulnerabilityDuration = 0.05f;
        [SerializeField] private bool destroyOnDeath;
        [SerializeField] private bool disableOnDeath = true;

        private float _currentHealth;
        private float _invulnerableUntil;

        public event Action<DamageInfo, float, float> Damaged;
        public event Action<DamageInfo> Died;

        public float CurrentHealth => _currentHealth;
        public float MaxHealth => maxHealth;
        public bool IsDead { get; private set; }
        public float HealthNormalized => maxHealth > 0f ? _currentHealth / maxHealth : 0f;

        private void Awake()
        {
            ResetHealth();
        }

        public bool TryApplyDamage(DamageInfo damageInfo)
        {
            if (IsDead || damageInfo.Amount <= 0f || Time.time < _invulnerableUntil)
            {
                return false;
            }

            var previousHealth = _currentHealth;
            _currentHealth = Mathf.Max(0f, _currentHealth - damageInfo.Amount);
            _invulnerableUntil = Time.time + invulnerabilityDuration;
            Damaged?.Invoke(damageInfo, previousHealth, _currentHealth);

            if (_currentHealth > 0f)
            {
                return true;
            }

            IsDead = true;
            Died?.Invoke(damageInfo);

            if (destroyOnDeath)
            {
                Destroy(gameObject);
            }
            else if (disableOnDeath)
            {
                gameObject.SetActive(false);
            }

            return true;
        }

        [ContextMenu("Reset Health")]
        public void ResetHealth()
        {
            IsDead = false;
            _currentHealth = maxHealth;
            _invulnerableUntil = 0f;
        }
    }
}

