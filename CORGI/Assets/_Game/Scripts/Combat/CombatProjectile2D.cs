using System.Collections.Generic;
using UnityEngine;

namespace _Game.Scripts.Combat
{
    [DisallowMultipleComponent]
    public class CombatProjectile2D : MonoBehaviour
    {
        private readonly HashSet<Health2D> _hitHealthTargets = new();
        private readonly RaycastHit2D[] _hitBuffer = new RaycastHit2D[16];

        private ContactFilter2D _contactFilter;
        private DamageInfo _damageInfo;
        private Transform _ignoredRoot;
        private Rigidbody2D _ignoredRigidbody;
        private Vector2 _direction;
        private LineRenderer _coreRenderer;
        private LineRenderer _glowRenderer;
        private TrailRenderer _trailRenderer;
        private Color _projectileColor;
        private float _glowPulseOffset;
        private int _sortingOrder;
        private float _speed;
        private float _radius;
        private float _maxDistance;
        private float _distanceTravelled;
        private bool _initialized;

        private static Material _sharedMaterial;

        public void Launch(
            DamageInfo damageInfo,
            Vector2 origin,
            Vector2 direction,
            float speed,
            float maxDistance,
            float radius,
            LayerMask hittableLayers,
            Transform ignoredRoot,
            Rigidbody2D ignoredRigidbody,
            Color color,
            int sortingOrder)
        {
            _damageInfo = damageInfo;
            _direction = direction.sqrMagnitude > 0.001f ? direction.normalized : Vector2.right;
            _speed = Mathf.Max(speed, 0.01f);
            _radius = Mathf.Max(radius, 0.05f);
            _maxDistance = Mathf.Max(maxDistance, 0.1f);
            _ignoredRoot = ignoredRoot;
            _ignoredRigidbody = ignoredRigidbody;
            _distanceTravelled = 0f;
            _projectileColor = color;
            _sortingOrder = sortingOrder;
            _glowPulseOffset = Random.value * Mathf.PI * 2f;
            _hitHealthTargets.Clear();

            _contactFilter.useLayerMask = true;
            _contactFilter.layerMask = hittableLayers;
            _contactFilter.useTriggers = true;

            transform.position = origin;
            transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(_direction.y, _direction.x) * Mathf.Rad2Deg);

            EnsureVisuals();
            UpdateVisualPulse();
            _initialized = true;
        }

        private void Update()
        {
            if (!_initialized)
            {
                return;
            }

            UpdateVisualPulse();
        }

        private void FixedUpdate()
        {
            if (!_initialized)
            {
                return;
            }

            var currentPosition = (Vector2)transform.position;
            var stepDistance = _speed * Time.fixedDeltaTime;
            var castDistance = stepDistance + _radius;
            var hitCount = Physics2D.CircleCast(currentPosition, _radius, _direction, _contactFilter, _hitBuffer, castDistance);
            RaycastHit2D? bestHit = null;

            for (var i = 0; i < hitCount; i++)
            {
                var hit = _hitBuffer[i];
                if (hit.collider == null || IsIgnoredCollider(hit.collider))
                {
                    continue;
                }

                if (bestHit == null || hit.distance < bestHit.Value.distance)
                {
                    bestHit = hit;
                }
            }

            if (bestHit.HasValue)
            {
                ResolveHit(bestHit.Value);
                return;
            }

            transform.position = currentPosition + _direction * stepDistance;
            _distanceTravelled += stepDistance;
            if (_distanceTravelled >= _maxDistance)
            {
                SpawnImpactFlash((Vector2)transform.position, 0.65f);
                Destroy(gameObject);
            }
        }

        private void ResolveHit(RaycastHit2D hit)
        {
            var impactPoint = hit.point == Vector2.zero ? (Vector2)transform.position : hit.point;
            var hurtbox = hit.collider.GetComponent<Hurtbox2D>() ?? hit.collider.GetComponentInParent<Hurtbox2D>();
            if (hurtbox != null && hurtbox.Health != null && _hitHealthTargets.Add(hurtbox.Health))
            {
                var resolvedDamageInfo = new DamageInfo(
                    _damageInfo.Source,
                    _damageInfo.AttackId,
                    _damageInfo.Amount,
                    _direction,
                    impactPoint,
                    _damageInfo.AttackSignature,
                    _damageInfo.IsCounterAttack,
                    _damageInfo.KnockbackForce,
                    _damageInfo.StunDuration);
                hurtbox.ApplyHit(resolvedDamageInfo);
            }

            SpawnImpactFlash(impactPoint, 1f);
            Destroy(gameObject);
        }

        private bool IsIgnoredCollider(Collider2D hitCollider)
        {
            if (hitCollider == null)
            {
                return true;
            }

            if (_ignoredRigidbody != null && hitCollider.attachedRigidbody == _ignoredRigidbody)
            {
                return true;
            }

            var colliderTransform = hitCollider.transform;
            return _ignoredRoot != null && (colliderTransform == _ignoredRoot || colliderTransform.IsChildOf(_ignoredRoot));
        }

        private void EnsureVisuals()
        {
            EnsureCoreRenderer();
            EnsureGlowRenderer();
            EnsureTrailRenderer();
            ConfigureProjectileShape();
        }

        private void EnsureCoreRenderer()
        {
            if (_coreRenderer == null)
            {
                _coreRenderer = GetComponent<LineRenderer>();
                if (_coreRenderer == null)
                {
                    _coreRenderer = gameObject.AddComponent<LineRenderer>();
                }
            }

            ConfigureLineRenderer(_coreRenderer, _sortingOrder, _projectileColor, Mathf.Max(_radius * 1.05f, 0.05f));
        }

        private void EnsureGlowRenderer()
        {
            if (_glowRenderer == null)
            {
                var glowTransform = transform.Find("GlowRenderer");
                if (glowTransform == null)
                {
                    glowTransform = new GameObject("GlowRenderer").transform;
                    glowTransform.SetParent(transform, false);
                }

                _glowRenderer = glowTransform.GetComponent<LineRenderer>();
                if (_glowRenderer == null)
                {
                    _glowRenderer = glowTransform.gameObject.AddComponent<LineRenderer>();
                }
            }

            var glowColor = Color.Lerp(_projectileColor, Color.white, 0.45f);
            glowColor.a = Mathf.Clamp01(_projectileColor.a * 0.45f + 0.2f);
            ConfigureLineRenderer(_glowRenderer, _sortingOrder - 1, glowColor, Mathf.Max(_radius * 2.15f, 0.1f));
        }

        private void EnsureTrailRenderer()
        {
            if (_trailRenderer == null)
            {
                _trailRenderer = GetComponent<TrailRenderer>();
                if (_trailRenderer == null)
                {
                    _trailRenderer = gameObject.AddComponent<TrailRenderer>();
                }
            }

            _trailRenderer.material = GetSharedMaterial();
            _trailRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _trailRenderer.receiveShadows = false;
            _trailRenderer.alignment = LineAlignment.TransformZ;
            _trailRenderer.numCapVertices = 4;
            _trailRenderer.numCornerVertices = 2;
            _trailRenderer.time = Mathf.Lerp(0.06f, 0.16f, Mathf.InverseLerp(0.05f, 0.5f, _radius));
            _trailRenderer.minVertexDistance = 0.015f;
            _trailRenderer.emitting = true;
            _trailRenderer.sortingOrder = _sortingOrder - 2;
            _trailRenderer.widthMultiplier = Mathf.Max(_radius * 0.9f, 0.03f);

            var gradient = new Gradient();
            var trailColor = Color.Lerp(_projectileColor, Color.white, 0.25f);
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(trailColor, 0f),
                    new GradientColorKey(_projectileColor, 0.5f),
                    new GradientColorKey(_projectileColor, 1f)
                },
                new[]
                {
                    new GradientAlphaKey(Mathf.Clamp01(_projectileColor.a * 0.8f), 0f),
                    new GradientAlphaKey(Mathf.Clamp01(_projectileColor.a * 0.35f), 0.45f),
                    new GradientAlphaKey(0f, 1f)
                });
            _trailRenderer.colorGradient = gradient;

            var widthCurve = new AnimationCurve(
                new Keyframe(0f, 0.95f),
                new Keyframe(0.45f, 0.6f),
                new Keyframe(1f, 0f));
            _trailRenderer.widthCurve = widthCurve;
        }

        private void ConfigureProjectileShape()
        {
            var length = Mathf.Max(_radius * 3.4f, 0.24f);
            var tailOffset = -length * 0.42f;
            var headOffset = length * 0.78f;

            _coreRenderer.positionCount = 2;
            _coreRenderer.SetPosition(0, new Vector3(tailOffset, 0f, 0f));
            _coreRenderer.SetPosition(1, new Vector3(headOffset, 0f, 0f));

            _glowRenderer.positionCount = 2;
            _glowRenderer.SetPosition(0, new Vector3(tailOffset, 0f, 0f));
            _glowRenderer.SetPosition(1, new Vector3(headOffset, 0f, 0f));
        }

        private void UpdateVisualPulse()
        {
            if (_coreRenderer == null || _glowRenderer == null)
            {
                return;
            }

            var pulse = 0.85f + 0.15f * (0.5f + 0.5f * Mathf.Sin(Time.time * 18f + _glowPulseOffset));
            _glowRenderer.widthMultiplier = Mathf.Max(_radius * 2.15f * pulse, 0.1f);

            var coreColor = Color.Lerp(_projectileColor, Color.white, 0.1f);
            _coreRenderer.startColor = coreColor;
            _coreRenderer.endColor = _projectileColor;

            var glowColor = Color.Lerp(_projectileColor, Color.white, 0.55f);
            glowColor.a = Mathf.Clamp01((_projectileColor.a * 0.35f + 0.25f) * pulse);
            _glowRenderer.startColor = glowColor;
            _glowRenderer.endColor = glowColor;
        }

        private void SpawnImpactFlash(Vector2 position, float intensity)
        {
            var flashObject = new GameObject("ProjectileImpactFlash");
            flashObject.transform.SetPositionAndRotation(position, Quaternion.identity);
            var flash = flashObject.AddComponent<CombatImpactFlash2D>();
            var flashColor = Color.Lerp(_projectileColor, Color.white, 0.35f);
            flashColor.a = Mathf.Clamp01(_projectileColor.a * 0.9f + 0.1f);
            flash.Initialize(flashColor, Mathf.Max(_radius * intensity, 0.08f), 0.12f, _sortingOrder + 1);
        }

        private static void ConfigureLineRenderer(LineRenderer renderer, int sortingOrder, Color color, float width)
        {
            renderer.material = GetSharedMaterial();
            renderer.textureMode = LineTextureMode.Stretch;
            renderer.alignment = LineAlignment.TransformZ;
            renderer.numCapVertices = 6;
            renderer.numCornerVertices = 4;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.loop = false;
            renderer.useWorldSpace = false;
            renderer.sortingOrder = sortingOrder;
            renderer.startColor = color;
            renderer.endColor = color;
            renderer.widthMultiplier = width;
        }

        private static Material GetSharedMaterial()
        {
            if (_sharedMaterial != null)
            {
                return _sharedMaterial;
            }

            var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/2D/Sprite-Lit-Default");
            _sharedMaterial = shader != null ? new Material(shader) : new Material(Shader.Find("Standard"));
            return _sharedMaterial;
        }
    }
}


