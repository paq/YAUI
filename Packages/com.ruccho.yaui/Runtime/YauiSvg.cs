using System;
using Unity.Mathematics;
using UnityEngine;
using Yaui.Core;
using Yaui.Rendering;

namespace Yaui
{
    /// <summary>A registered vector asset. Each acquisition needs a release; released and old-generation handles are invalid.</summary>
    public readonly struct YauiVector
    {
        private readonly int _lease;
        private readonly int _generation;

        private YauiVector(int lease)
        {
            _lease = lease;
            _generation = YauiSystem.Generation;
        }

        public bool IsValid => YauiSystem.IsInitialized && _generation == YauiSystem.Generation &&
                               YauiSystem.Vectors.Contains(_lease);
        public YauiVectorAsset Asset => IsValid ? YauiSystem.Vectors.Asset(_lease) : null;

        /// <summary>Shares an asset's GPU geometry with its other users.</summary>
        public static YauiVector Acquire(YauiVectorAsset asset)
        {
            if (asset == null) throw new ArgumentNullException(nameof(asset));
            YauiSystem.EnsureInitialized();
            return new YauiVector(YauiSystem.Vectors.Acquire(asset));
        }

        public void Release()
        {
            if (IsValid) YauiSystem.Vectors.Release(_lease);
        }

        internal int Layer(int index) => YauiSystem.Vectors.Layer(_lease, index);
    }

    /// <summary>Draws an SVG over its content box, preserving its viewBox aspect by default.</summary>
    [AddComponentMenu("YAUI/SVG")]
    public class YauiSvg : YauiElement
    {
        [SerializeField] private YauiVectorAsset vector;
        [SerializeField] private Color color = Color.white;
        [SerializeField] private bool preserveAspect = true;
        [NonSerialized] private YauiVector _bound;
        private YauiPrimitive[] _primitives = Array.Empty<YauiPrimitive>();
        private Vector2 _measureSize;
        private bool _measureAspect;

        public YauiVectorAsset Vector
        {
            get => vector;
            set { vector = value; MarkMeasureDirty(); SyncVector(); }
        }

        /// <summary>The color of currentColor paints; alpha also multiplies fixed-color paints.</summary>
        public Color Color
        {
            get => color;
            set { color = value; SyncVector(); }
        }

        public bool PreserveAspect
        {
            get => preserveAspect;
            set { preserveAspect = value; MarkMeasureDirty(); SyncVector(); }
        }

        protected override bool HasVisibleContent => vector != null && vector.Layers.Count > 0;
        protected override bool ContentIsMaskShape => true;
        protected override bool MeasuresContent => true;

        protected override void OnValidate()
        {
            base.OnValidate();
            MarkMeasureDirty();
            SyncVector();
        }

        protected override void OnDidApplyAnimationProperties()
        {
            base.OnDidApplyAnimationProperties();
            MarkMeasureDirty();
            SyncVector();
        }

        protected override void OnRegistered() => SyncVector();
        protected override void OnLayoutApplied() => SyncVector();
        protected override void OnBoxChanged() => SyncVector();

        protected override void OnUnregistering()
        {
            _bound.Release();
            _bound = default;
        }

        protected override void OnPrepareMeasure()
        {
            _measureSize = vector != null ? vector.ViewBox.size : Vector2.zero;
            _measureAspect = preserveAspect;
        }

        protected override Vector2 MeasureContent(float width, YauiMeasureMode widthMode, float height,
            YauiMeasureMode heightMode)
        {
            var size = _measureSize;
            if (size.x <= 0f || size.y <= 0f) return Vector2.zero;
            if (_measureAspect)
            {
                if (widthMode == YauiMeasureMode.Exactly) size *= width / size.x;
                else if (heightMode == YauiMeasureMode.Exactly) size *= height / size.y;
                var scale = 1f;
                if (widthMode == YauiMeasureMode.AtMost && size.x > 0f) scale = Mathf.Min(scale, width / size.x);
                if (heightMode == YauiMeasureMode.AtMost && size.y > 0f) scale = Mathf.Min(scale, height / size.y);
                size *= scale;
            }
            else
            {
                if (widthMode != YauiMeasureMode.Undefined) size.x = widthMode == YauiMeasureMode.Exactly ? width : Mathf.Min(width, size.x);
                if (heightMode != YauiMeasureMode.Undefined) size.y = heightMode == YauiMeasureMode.Exactly ? height : Mathf.Min(height, size.y);
            }

            if (widthMode == YauiMeasureMode.Exactly) size.x = width;
            if (heightMode == YauiMeasureMode.Exactly) size.y = height;
            return size;
        }

        private void SyncVector()
        {
            if (!IsRegistered) return;
            SyncHittable();
            if (!_bound.IsValid || _bound.Asset != vector)
            {
                _bound.Release();
                _bound = vector != null ? YauiVector.Acquire(vector) : default;
            }

            if (!_bound.IsValid)
            {
                SetContent(ReadOnlySpan<YauiPrimitive>.Empty);
                return;
            }

            var content = ContentRect();
            var rect = new Rect(content.x, content.y, content.z, content.w);
            if (preserveAspect)
            {
                var size = vector.ViewBox.size;
                size *= Mathf.Min(rect.width / size.x, rect.height / size.y);
                rect = new Rect(rect.center - size * 0.5f, size);
            }

            if (_primitives.Length != vector.Layers.Count) _primitives = new YauiPrimitive[vector.Layers.Count];
            for (var i = 0; i < _primitives.Length; i++)
                _primitives[i] = YauiPrimitive.Vector(rect, _bound, i, color);
            SetContent(_primitives);
        }
    }
}
