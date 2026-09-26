using System;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using Yaui.Core;
using Yaui.Rendering;

namespace Yaui
{
    public enum ImageType
    {
        /// <summary>The sprite stretched over the content box.</summary>
        Simple,

        /// <summary>9-slice: the borders of the sprite keep their size, the center stretches.</summary>
        Sliced,

        /// <summary>A part of the simple image, by <see cref="YauiImage.FillMethod"/> and <see cref="YauiImage.FillAmount"/>.</summary>
        Filled
    }

    public enum FillMethod
    {
        Horizontal,
        Vertical,

        /// <summary>A quarter of a circle around a corner (<see cref="FillOrigin90"/>).</summary>
        Radial90,

        /// <summary>A half circle around the middle of an edge (<see cref="FillOrigin180"/>).</summary>
        Radial180,

        /// <summary>A full circle around the center (<see cref="FillOrigin360"/>).</summary>
        Radial360
    }

    // The values of YauiImage.FillOrigin for each fill method, like the ones of uGUI's Image.
    public enum FillOriginHorizontal
    {
        Left,
        Right
    }

    public enum FillOriginVertical
    {
        Bottom,
        Top
    }

    public enum FillOrigin90
    {
        BottomLeft,
        TopLeft,
        TopRight,
        BottomRight
    }

    public enum FillOrigin180
    {
        Bottom,
        Left,
        Top,
        Right
    }

    public enum FillOrigin360
    {
        Bottom,
        Right,
        Top,
        Left
    }

    /// <summary>
    /// An element that draws a sprite over its content box (inside the padding and border), on top of its box.
    /// The corner radii of the box also round a simple image.
    /// </summary>
    [AddComponentMenu("YAUI/Image")]
    public class YauiImage : YauiElement
    {
        [SerializeField] private Sprite sprite;
        [SerializeField] private Color color = Color.white;
        [SerializeField] private ImageType type = ImageType.Simple;

        /// <summary>Sliced: canvas units per pixel of the sprite borders.</summary>
        [SerializeField] private float borderScale = 1f;

        [SerializeField] private FillMethod fillMethod = FillMethod.Radial360;

        /// <summary>Where the fill starts: a value of the FillOrigin enum of <see cref="fillMethod"/>.</summary>
        [SerializeField] private int fillOrigin;

        [SerializeField] [Range(0f, 1f)] private float fillAmount = 1f;

        /// <summary>Radial fills: clockwise on screen.</summary>
        [SerializeField] private bool fillClockwise = true;

        [NonSerialized] private int start;
        [NonSerialized] private int capacity;
        [NonSerialized] private Sprite boundSprite;
        [NonSerialized] private Sprite overrideSprite;
        [NonSerialized] private SpriteTexture spriteTexture;

        public Sprite Sprite
        {
            get => sprite;
            set
            {
                sprite = value;
                SyncImage();
            }
        }

        /// <summary>
        /// Drawn instead of <see cref="Sprite"/> while set, without changing it (not serialized). Selectables set it
        /// for their sprite swap transitions.
        /// </summary>
        public Sprite OverrideSprite
        {
            get => overrideSprite;
            set
            {
                overrideSprite = value;
                SyncImage();
            }
        }

        private Sprite ActiveSprite => overrideSprite != null ? overrideSprite : sprite;

        public Color Color
        {
            get => color;
            set
            {
                color = value;
                SyncImage();
            }
        }

        public ImageType Type
        {
            get => type;
            set
            {
                type = value;
                SyncImage();
            }
        }

        public float BorderScale
        {
            get => borderScale;
            set
            {
                borderScale = value;
                SyncImage();
            }
        }

        public FillMethod FillMethod
        {
            get => fillMethod;
            set
            {
                fillMethod = value;
                SyncImage();
            }
        }

        /// <summary>A value of the FillOrigin enum of <see cref="FillMethod"/> (e.g. <see cref="FillOrigin360"/>).</summary>
        public int FillOrigin
        {
            get => fillOrigin;
            set
            {
                fillOrigin = value;
                SyncImage();
            }
        }

        public float FillAmount
        {
            get => fillAmount;
            set
            {
                fillAmount = Mathf.Clamp01(value);
                SyncImage();
            }
        }

        public bool FillClockwise
        {
            get => fillClockwise;
            set
            {
                fillClockwise = value;
                SyncImage();
            }
        }

        private protected override bool HasVisibleContent => ActiveSprite != null;

        protected override void OnValidate()
        {
            base.OnValidate();
            SyncImage();
        }

        protected override void OnDidApplyAnimationProperties()
        {
            base.OnDidApplyAnimationProperties();
            SyncImage();
        }

        private protected override void OnRegistered()
        {
            SyncImage();
        }

        private protected override void OnUnregistering()
        {
            if (YauiSystem.IsInitialized)
            {
                if (capacity > 0) YauiSystem.Primitives.FreeRange(start, capacity);

                if (spriteTexture.IsValid) YauiSystem.Textures.ReleaseSprite(boundSprite, spriteTexture);
            }

            start = 0;
            capacity = 0;
            boundSprite = null;
            spriteTexture = default;
        }

        internal override void OnLayoutApplied()
        {
            SyncImage();
        }

        private protected override void OnBoxChanged()
        {
            SyncImage();
        }

        /// <summary>With a sprite, a mask on the image takes the shape of the sprite's alpha.</summary>
        internal override void AppendMaskShape(NativeList<uint> order)
        {
            if (capacity == 0)
            {
                base.AppendMaskShape(order);
                return;
            }

            for (var i = 0; i < capacity; i++) order.Add((uint)(start + i));
        }

        internal override void AppendDrawOrder(NativeList<uint> order)
        {
            base.AppendDrawOrder(order);
            for (var i = 0; i < capacity; i++) order.Add((uint)(start + i));
        }

        /// <summary>Writes the image primitives from the sprite and the laid-out content box.</summary>
        private void SyncImage()
        {
            if (NodeSlot <= 0) return;

            SyncHittable();
            var active = ActiveSprite;
            if (active != boundSprite)
            {
                var previousId = spriteTexture.TextureId;
                if (spriteTexture.IsValid) YauiSystem.Textures.ReleaseSprite(boundSprite, spriteTexture);

                boundSprite = active;
                spriteTexture = active != null ? YauiSystem.Textures.AcquireSprite(active) : default;

                // Draws are split by the textures they use.
                if (spriteTexture.TextureId != previousId) Panel.OrderDirty = true;
            }

            var count = active == null ? 0 : type == ImageType.Sliced ? 9 : 1;
            var needed = count == 0 ? 0 : GpuStore<PrimitiveData>.RangeCapacity(count);
            var primitives = YauiSystem.Primitives;
            if (needed != capacity)
            {
                if (capacity > 0) primitives.FreeRange(start, capacity);

                start = needed > 0 ? primitives.AllocateRange(needed) : 0;
                capacity = needed;
                Panel.OrderDirty = true;
            }

            var written = 0;
            if (count > 0 && spriteTexture.IsValid)
                written = type switch
                {
                    ImageType.Sliced => WriteSliced(),
                    ImageType.Filled => WriteFilled(),
                    _ => WriteSimple()
                };

            for (var i = written; i < capacity; i++) primitives[start + i] = default;

            YauiSystem.RequestUpdate();
        }

        /// <summary>UVs of the sprite: u0 / u1 left and right, v0 / v1 bottom and top (textures are Y up).</summary>
        private float4 SpriteUv()
        {
            return spriteTexture.Uv;
        }

        private PrimitiveData Primitive(float4 rect, float4 uvAtMinMax, uint2 radii)
        {
            return new PrimitiveData
            {
                Rect = rect,
                UvRect = GpuPacking.Unorm16x4(uvAtMinMax),
                Color = GpuPacking.Color(color),
                Radii = radii,
                Node = (uint)NodeSlot,
                Flags = PrimitiveTexture.With(PrimitiveFlags.Image, spriteTexture.TextureId)
            };
        }

        private int WriteSimple()
        {
            var uv = SpriteUv();

            // The rect's min corner is the top-left: the top of the sprite (v1).
            YauiSystem.Primitives[start] = Primitive(ContentRect(), new float4(uv.x, uv.w, uv.z, uv.y),
                GpuPacking.Half4(Box.CornerRadius));
            return 1;
        }

        /// <summary>The range of the image primitives (tests).</summary>
        internal (int Start, int Capacity) PrimitiveRange => (start, capacity);

        private int WriteFilled()
        {
            var rect = ContentRect();
            var uv = SpriteUv();
            // UVs at the top-left and at the bottom-right (the top of the sprite is v1).
            var uvRect = new float4(uv.x, uv.w, uv.z, uv.y);
            var amount = math.saturate(fillAmount);
            var p = Primitive(rect, uvRect, GpuPacking.Half4(Box.CornerRadius));
            if (fillMethod is FillMethod.Horizontal or FillMethod.Vertical)
            {
                // A linear fill is the image cut on one side, with its UVs cut alike.
                var axis = fillMethod == FillMethod.Horizontal ? 0 : 1;
                // The rect is Y down: a fill from the bottom starts at the max of the axis.
                var fromMax = axis == 0
                    ? fillOrigin == (int)FillOriginHorizontal.Right
                    : fillOrigin == (int)FillOriginVertical.Bottom;
                var size = rect[axis + 2];
                var filled = size * amount;
                if (filled <= 0f) return 0;

                var uvFrom = uvRect[axis];
                var uvTo = uvRect[axis + 2];
                if (fromMax)
                {
                    rect[axis] += size - filled;
                    uvRect[axis] = math.lerp(uvFrom, uvTo, 1f - amount);
                }
                else
                {
                    uvRect[axis + 2] = math.lerp(uvFrom, uvTo, amount);
                }

                rect[axis + 2] = filled;
                p.Rect = rect;
                p.UvRect = GpuPacking.Unorm16x4(uvRect);
            }
            else
            {
                RadialFill(out var center, out var from, out var range);
                if (!fillClockwise && range < math.PI * 2f)
                    // A quarter or a half circle filled counter-clockwise starts from the other end of its range.
                    from += range;

                p.Flags |= PrimitiveFlags.RadialFill;
                p.BorderColor = GpuPacking.Half4(new float4(center, from, range * amount * (fillClockwise ? 1f : -1f)));
            }

            YauiSystem.Primitives[start] = p;
            return 1;
        }

        // Angles in radians, growing clockwise on screen (Y down).
        private const float Right = 0f;
        private const float Down = math.PI * 0.5f;
        private const float Left = math.PI;
        private const float Up = -math.PI * 0.5f;

        /// <summary>
        /// The center (0..1 of the rect, Y down), the start of a clockwise fill and the angle of the full fill.
        /// </summary>
        private void RadialFill(out float2 center, out float from, out float range)
        {
            switch (fillMethod)
            {
                case FillMethod.Radial90:
                    range = math.PI * 0.5f;
                    (center, from) = (FillOrigin90)fillOrigin switch
                    {
                        FillOrigin90.TopLeft => (new float2(0f, 0f), Right),
                        FillOrigin90.TopRight => (new float2(1f, 0f), Down),
                        FillOrigin90.BottomRight => (new float2(1f, 1f), Left),
                        _ => (new float2(0f, 1f), Up)
                    };
                    break;
                case FillMethod.Radial180:
                    range = math.PI;
                    (center, from) = (FillOrigin180)fillOrigin switch
                    {
                        FillOrigin180.Left => (new float2(0f, 0.5f), Up),
                        FillOrigin180.Top => (new float2(0.5f, 0f), Right),
                        FillOrigin180.Right => (new float2(1f, 0.5f), Down),
                        _ => (new float2(0.5f, 1f), Left)
                    };
                    break;
                default:
                    range = math.PI * 2f;
                    center = new float2(0.5f, 0.5f);
                    from = (FillOrigin360)fillOrigin switch
                    {
                        FillOrigin360.Right => Right,
                        FillOrigin360.Top => Up,
                        FillOrigin360.Left => Left,
                        _ => Down
                    };
                    break;
            }
        }

        private int WriteSliced()
        {
            var rect = ContentRect();
            var uv = SpriteUv();
            var textureSize = spriteTexture.TextureSize;

            // Sprite borders: left, bottom, right, top in pixels.
            var border = (float4)boundSprite.border;
            var local = border * borderScale;

            // Borders larger than the rect shrink proportionally.
            var fitX = math.min(1f, rect.z / math.max(local.x + local.z, 1e-4f));
            var fitY = math.min(1f, rect.w / math.max(local.y + local.w, 1e-4f));
            var xs = new float4(0f, local.x * fitX, rect.z - local.z * fitX, rect.z);
            var ys = new float4(0f, local.w * fitY, rect.w - local.y * fitY, rect.w);
            var us = new float4(uv.x, uv.x + border.x / textureSize.x, uv.z - border.z / textureSize.x, uv.z);
            var vs = new float4(uv.w, uv.w - border.w / textureSize.y, uv.y + border.y / textureSize.y, uv.y);

            var written = 0;
            for (var row = 0; row < 3; row++)
            for (var column = 0; column < 3; column++)
            {
                var size = new float2(xs[column + 1] - xs[column], ys[row + 1] - ys[row]);
                if (size.x <= 0f || size.y <= 0f) continue;

                var cell = new float4(rect.x + xs[column], rect.y + ys[row], size);
                var cellUv = new float4(us[column], vs[row], us[column + 1], vs[row + 1]);
                YauiSystem.Primitives[start + written++] = Primitive(cell, cellUv, default);
            }

            return written;
        }
    }
}