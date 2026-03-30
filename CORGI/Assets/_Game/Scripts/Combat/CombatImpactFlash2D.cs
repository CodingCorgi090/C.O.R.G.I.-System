using UnityEngine;

namespace _Game.Scripts.Combat
{
    [DisallowMultipleComponent]
    public class CombatImpactFlash2D : MonoBehaviour
    {
        private LineRenderer _ringRenderer;
        private float _duration;
        private float _elapsed;
        private float _startRadius;
        private float _endRadius;
        private Color _baseColor;
        private int _segments;

        private static Material _sharedMaterial;

        public void Initialize(Color color, float radius, float duration, int sortingOrder)
        {
            _baseColor = color;
            _startRadius = Mathf.Max(radius * 0.35f, 0.05f);
            _endRadius = Mathf.Max(radius * 2.15f, _startRadius + 0.05f);
            _duration = Mathf.Max(duration, 0.05f);
            _segments = Mathf.Clamp(Mathf.RoundToInt(radius * 24f), 10, 24);

            EnsureRenderer(sortingOrder);
            UpdateVisual(0f);
        }

        private void Update()
        {
            _elapsed += Time.deltaTime;
            var normalizedTime = Mathf.Clamp01(_elapsed / _duration);
            UpdateVisual(normalizedTime);

            if (normalizedTime >= 1f)
            {
                Destroy(gameObject);
            }
        }

        private void EnsureRenderer(int sortingOrder)
        {
            if (_ringRenderer == null)
            {
                _ringRenderer = GetComponent<LineRenderer>();
                if (_ringRenderer == null)
                {
                    _ringRenderer = gameObject.AddComponent<LineRenderer>();
                }
            }

            _ringRenderer.material = GetSharedMaterial();
            _ringRenderer.textureMode = LineTextureMode.Stretch;
            _ringRenderer.alignment = LineAlignment.TransformZ;
            _ringRenderer.numCapVertices = 4;
            _ringRenderer.numCornerVertices = 4;
            _ringRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _ringRenderer.receiveShadows = false;
            _ringRenderer.loop = true;
            _ringRenderer.useWorldSpace = false;
            _ringRenderer.sortingOrder = sortingOrder;
            _ringRenderer.positionCount = _segments;
        }

        private void UpdateVisual(float normalizedTime)
        {
            if (_ringRenderer == null)
            {
                return;
            }

            var eased = 1f - Mathf.Pow(1f - normalizedTime, 2f);
            var radius = Mathf.Lerp(_startRadius, _endRadius, eased);
            var alpha = 1f - normalizedTime;
            var width = Mathf.Lerp(_startRadius * 0.8f, 0.02f, normalizedTime);
            var color = new Color(_baseColor.r, _baseColor.g, _baseColor.b, _baseColor.a * alpha);

            _ringRenderer.startColor = color;
            _ringRenderer.endColor = color;
            _ringRenderer.widthMultiplier = width;

            for (var i = 0; i < _segments; i++)
            {
                var angle = i / (float)_segments * Mathf.PI * 2f;
                var point = new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f);
                _ringRenderer.SetPosition(i, point);
            }
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

