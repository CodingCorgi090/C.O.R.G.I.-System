using UnityEngine;

namespace _Game.Scripts.Combat
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Shield2D))]
    public class ShieldVisual2D : MonoBehaviour
    {
        [SerializeField] private Shield2D shield;
        [SerializeField, Min(0.1f)] private float radius = 0.9f;
        [SerializeField, Min(0.01f)] private float thickness = 0.09f;
        [SerializeField, Range(6, 48)] private int segmentCount = 18;
        [SerializeField] private Color activeColor = new(0.2f, 0.95f, 1f, 0.9f);
        [SerializeField] private Color guardBrokenColor = new(1f, 0.25f, 0.2f, 0.9f);
        [SerializeField] private int sortingOrder = 10;

        private LineRenderer _lineRenderer;
        private static Material _sharedMaterial;

        private void Awake()
        {
            if (shield == null)
            {
                shield = GetComponent<Shield2D>();
            }

            EnsureLineRenderer();
        }

        private void LateUpdate()
        {
            if (shield == null)
            {
                shield = GetComponent<Shield2D>();
                if (shield == null)
                {
                    return;
                }
            }

            EnsureLineRenderer();

            var visible = shield.IsShielding || shield.IsGuardBroken;
            _lineRenderer.enabled = visible;
            if (!visible)
            {
                return;
            }

            var color = shield.IsGuardBroken ? guardBrokenColor : activeColor;
            _lineRenderer.startColor = color;
            _lineRenderer.endColor = color;
            _lineRenderer.widthMultiplier = thickness;
            UpdateArc();
        }

        private void EnsureLineRenderer()
        {
            if (_lineRenderer != null)
            {
                return;
            }

            _lineRenderer = GetComponent<LineRenderer>();
            if (_lineRenderer == null)
            {
                _lineRenderer = gameObject.AddComponent<LineRenderer>();
            }

            _lineRenderer.material = GetSharedMaterial();
            _lineRenderer.textureMode = LineTextureMode.Stretch;
            _lineRenderer.alignment = LineAlignment.TransformZ;
            _lineRenderer.numCapVertices = 6;
            _lineRenderer.numCornerVertices = 4;
            _lineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _lineRenderer.receiveShadows = false;
            _lineRenderer.loop = false;
            _lineRenderer.useWorldSpace = true;
            _lineRenderer.sortingOrder = sortingOrder;
            _lineRenderer.positionCount = Mathf.Max(segmentCount + 1, 2);
        }

        private void UpdateArc()
        {
            _lineRenderer.positionCount = Mathf.Max(segmentCount + 1, 2);
            var facing = shield.FacingDirection.sqrMagnitude > 0.001f ? shield.FacingDirection.normalized : Vector2.right;
            var centerAngle = Mathf.Atan2(facing.y, facing.x) * Mathf.Rad2Deg;
            var halfArc = shield.BlockArcDegrees * 0.5f;
            var origin = (Vector2)transform.position;

            for (var i = 0; i < _lineRenderer.positionCount; i++)
            {
                var t = _lineRenderer.positionCount == 1 ? 0f : i / (float)(_lineRenderer.positionCount - 1);
                var angle = centerAngle - halfArc + shield.BlockArcDegrees * t;
                var radians = angle * Mathf.Deg2Rad;
                var point = origin + new Vector2(Mathf.Cos(radians), Mathf.Sin(radians)) * radius;
                _lineRenderer.SetPosition(i, point);
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

