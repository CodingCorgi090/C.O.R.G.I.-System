using UnityEngine;

namespace _Game.Scripts.Generation
{
    [DisallowMultipleComponent]
    public class LevelCompletionTrigger : MonoBehaviour
    {
        private RuntimeLevelGenerator _levelGenerator;
        [SerializeField, Min(0.1f)] private float fallbackVisualRadius = 0.75f;
        [SerializeField] private Color fallbackVisualColor = new(1f, 0.9f, 0.2f, 0.95f);
        [SerializeField] private int sortingOrder = 11;

        private CircleCollider2D _triggerCollider;
        private LineRenderer _lineRenderer;
        private static Material _sharedMaterial;

        public void Initialize(RuntimeLevelGenerator generator)
        {
            _levelGenerator = generator;
            EnsureTrigger();
            EnsureFallbackVisual();
        }

        private void Awake()
        {
            EnsureTrigger();
            EnsureFallbackVisual();
        }

        private void Update()
        {
            if (_lineRenderer == null)
            {
                return;
            }

            var pulse = 0.9f + 0.1f * Mathf.Sin(Time.time * 4f);
            var color = fallbackVisualColor;
            color.a *= 0.75f + 0.25f * (0.5f + 0.5f * Mathf.Sin(Time.time * 5f));
            _lineRenderer.startColor = color;
            _lineRenderer.endColor = color;
            _lineRenderer.widthMultiplier = 0.08f * pulse;
            DrawRing(fallbackVisualRadius * pulse);
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            var player = other.GetComponent<PlayerMovementController>() ?? other.GetComponentInParent<PlayerMovementController>();
            if (player == null)
            {
                return;
            }

            _levelGenerator?.HandleGoalReached(player);
        }

        private void EnsureTrigger()
        {
            if (_triggerCollider == null)
            {
                _triggerCollider = GetComponent<CircleCollider2D>();
                if (_triggerCollider == null)
                {
                    _triggerCollider = gameObject.AddComponent<CircleCollider2D>();
                }
            }

            _triggerCollider.isTrigger = true;
            _triggerCollider.radius = fallbackVisualRadius;
        }

        private void EnsureFallbackVisual()
        {
            if (GetComponent<Renderer>() != null && GetComponent<LineRenderer>() == null)
            {
                return;
            }

            if (_lineRenderer == null)
            {
                _lineRenderer = GetComponent<LineRenderer>();
                if (_lineRenderer == null)
                {
                    _lineRenderer = gameObject.AddComponent<LineRenderer>();
                }
            }

            _lineRenderer.material = GetSharedMaterial();
            _lineRenderer.textureMode = LineTextureMode.Stretch;
            _lineRenderer.alignment = LineAlignment.TransformZ;
            _lineRenderer.loop = true;
            _lineRenderer.useWorldSpace = false;
            _lineRenderer.positionCount = 24;
            _lineRenderer.numCapVertices = 4;
            _lineRenderer.numCornerVertices = 4;
            _lineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _lineRenderer.receiveShadows = false;
            _lineRenderer.sortingOrder = sortingOrder;
            DrawRing(fallbackVisualRadius);
        }

        private void DrawRing(float radius)
        {
            if (_lineRenderer == null)
            {
                return;
            }

            for (var i = 0; i < _lineRenderer.positionCount; i++)
            {
                var angle = i / (float)_lineRenderer.positionCount * Mathf.PI * 2f;
                _lineRenderer.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f));
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


