using System;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using Yaui.Core;
using Yaui.Rendering;

namespace Yaui
{
    /// <summary>
    /// An element that draws any texture (a render texture, a video, a texture without a sprite) over its content
    /// box, on top of its box. The corner radii of the box also round the image. Textures are never atlased: each
    /// one takes a texture slot of the draw.
    /// </summary>
    [AddComponentMenu("YAUI/Raw Image")]
    public class YauiRawImage : YauiElement
    {
        [SerializeField] Texture texture;
        [SerializeField] Color color = Color.white;

        /// <summary>The part of the texture drawn, in UVs within 0..1 (Y up). Textures do not repeat.</summary>
        [SerializeField] Rect uvRect = new(0f, 0f, 1f, 1f);

        [NonSerialized] int slot;
        [NonSerialized] Texture boundTexture;
        [NonSerialized] int textureId;

        public Texture Texture
        {
            get => texture;
            set
            {
                texture = value;
                SyncImage();
            }
        }

        public Color Color
        {
            get => color;
            set
            {
                color = value;
                SyncImage();
            }
        }

        public Rect UvRect
        {
            get => uvRect;
            set
            {
                uvRect = value;
                SyncImage();
            }
        }

        private protected override bool HasVisibleContent => texture != null;

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

        private protected override void OnRegistered() => SyncImage();

        private protected override void OnUnregistering()
        {
            if (YauiSystem.IsInitialized)
            {
                if (slot > 0)
                {
                    YauiSystem.Primitives.Free(slot);
                }

                YauiSystem.Textures.Release(textureId);
            }

            slot = 0;
            boundTexture = null;
            textureId = 0;
        }

        internal override void OnLayoutApplied() => SyncImage();

        private protected override void OnBoxChanged() => SyncImage();

        /// <summary>With a texture, a mask on the image takes the shape of the texture's alpha.</summary>
        internal override void AppendMaskShape(NativeList<uint> order)
        {
            if (slot == 0)
            {
                base.AppendMaskShape(order);
                return;
            }

            order.Add((uint)slot);
        }

        internal override void AppendDrawOrder(NativeList<uint> order)
        {
            base.AppendDrawOrder(order);
            if (slot > 0)
            {
                order.Add((uint)slot);
            }
        }

        /// <summary>The image primitive, or 0 (tests).</summary>
        internal int PrimitiveSlot => slot;

        void SyncImage()
        {
            if (NodeSlot <= 0)
            {
                return;
            }

            SyncHittable();
            if (texture != boundTexture)
            {
                var previousId = textureId;
                boundTexture = texture;
                textureId = texture != null ? YauiSystem.Textures.Acquire(texture) : 0;
                YauiSystem.Textures.Release(previousId);

                // Draws are split by the textures they use.
                if (textureId != previousId)
                {
                    Panel.OrderDirty = true;
                }
            }

            var primitives = YauiSystem.Primitives;
            if (texture == null)
            {
                if (slot > 0)
                {
                    primitives.Free(slot);
                    slot = 0;
                    Panel.OrderDirty = true;
                }

                YauiSystem.RequestUpdate();
                return;
            }

            if (slot == 0)
            {
                slot = primitives.Allocate();
                Panel.OrderDirty = true;
            }

            // The rect's min corner is the top-left: the top of the UV rect.
            primitives[slot] = new PrimitiveData
            {
                Rect = ContentRect(),
                UvRect = GpuPacking.Unorm16x4(new float4(uvRect.xMin, uvRect.yMax, uvRect.xMax, uvRect.yMin)),
                Color = GpuPacking.Color(color),
                Radii = GpuPacking.Half4(Box.CornerRadius),
                Node = (uint)NodeSlot,
                Flags = PrimitiveTexture.With(PrimitiveFlags.Image, textureId),
            };
            YauiSystem.RequestUpdate();
        }
    }
}
