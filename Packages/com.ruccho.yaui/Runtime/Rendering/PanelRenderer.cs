using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using Yaui.Core;

namespace Yaui.Rendering
{
    /// <summary>
    /// Draws the panels. Each primitive is a quad of 4 vertices indexed 6 times (one shared index buffer).
    /// <list type="bullet">
    /// <item>Overlay panels: a render graph pass after post-processing on the last game camera that renders to the
    /// screen. One draw call per panel, in sort order.</item>
    /// <item>World space panels: <see cref="Graphics.RenderPrimitivesIndexed"/>, so that URP sorts and depth tests
    /// them with the rest of the transparent objects (planning/poc-p1.md, PoC 7).</item>
    /// </list>
    /// </summary>
    internal sealed class PanelRenderer : IDisposable
    {
        const int OverlayPass = 0;

        static readonly int PrimitivesId = Shader.PropertyToID("_YauiPrimitives");
        static readonly int ExtsId = Shader.PropertyToID("_YauiExts");
        static readonly int NodesId = Shader.PropertyToID("_YauiNodes");
        static readonly int ClipsId = Shader.PropertyToID("_YauiClips");
        static readonly int OrderId = Shader.PropertyToID("_YauiOrder");
        static readonly int PixelSizeId = Shader.PropertyToID("_YauiPixelSize");
        static readonly int PanelMatrixId = Shader.PropertyToID("_YauiPanelMatrix");
        static readonly int OrderOffsetId = Shader.PropertyToID("_YauiOrderOffset");
        static readonly int TexIds0Id = Shader.PropertyToID("_YauiTexIds0");
        static readonly int TexIds1Id = Shader.PropertyToID("_YauiTexIds1");
        static readonly int AtlasParamsId = Shader.PropertyToID("_YauiAtlasParams");

        static readonly int[] TextureIds =
        {
            Shader.PropertyToID("_YauiTex0"), Shader.PropertyToID("_YauiTex1"), Shader.PropertyToID("_YauiTex2"),
            Shader.PropertyToID("_YauiTex3"), Shader.PropertyToID("_YauiTex4"), Shader.PropertyToID("_YauiTex5"),
            Shader.PropertyToID("_YauiTex6"), Shader.PropertyToID("_YauiTex7"),
        };

        readonly Vector4[] atlasParams = new Vector4[TextureRegistry.SlotCount];

        static readonly int StencilRefId = Shader.PropertyToID("_YauiStencilRef");
        static readonly int StencilCompId = Shader.PropertyToID("_YauiStencilComp");
        static readonly int StencilPassId = Shader.PropertyToID("_YauiStencilPass");

        readonly Material material;

        // The uber shader with per-pixel clipping, for panels that have rotated nodes or rounded clips.
        readonly Material pixelClipMaterial;

        // The shapes of masks, with and without per-pixel clipping.
        readonly Material maskMaterial;
        readonly Material pixelClipMaskMaterial;

        // Materials with the stencil state of a segment, by base material, kind and depth.
        readonly Dictionary<(Material, SegmentKind, int), Material> stencilMaterials = new();
        readonly OverlayRenderPass pass;
        readonly SceneViewRenderPass sceneViewPass;
        readonly List<PanelState> sorted = new();
        readonly List<DrawItem> draws = new();
        GraphicsBuffer indices;
        int indexedQuads;
        bool overlayNeedsStencil;
        Camera target;

        struct DrawItem
        {
            public int Count;
            public Matrix4x4 Projection;
            public MaterialPropertyBlock Properties;
            public Material Material;
            public int Pass;
        }

        public PanelRenderer()
        {
            var shader = Resources.Load<Shader>("Yaui/Uber");
            material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave, renderQueue = 3000 };
            pixelClipMaterial = new Material(material) { hideFlags = HideFlags.HideAndDontSave };
            pixelClipMaterial.EnableKeyword("YAUI_PIXEL_CLIP");
            maskMaterial = new Material(Resources.Load<Shader>("Yaui/Mask"))
            {
                hideFlags = HideFlags.HideAndDontSave, renderQueue = 3000,
            };
            pixelClipMaskMaterial = new Material(maskMaterial) { hideFlags = HideFlags.HideAndDontSave };
            pixelClipMaskMaterial.EnableKeyword("YAUI_PIXEL_CLIP");
            pass = new OverlayRenderPass(this);
            sceneViewPass = new SceneViewRenderPass(this);
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
        }

        /// <summary>
        /// Pixel size of the camera the overlay was last drawn on, or zero. Overlay panels are laid out for this size.
        /// </summary>
        public Vector2Int TargetSize { get; private set; }

        /// <summary>Picks the camera to draw overlays on: the last game camera rendering to the screen.</summary>
        public void SelectCamera(List<Camera> cameras)
        {
            target = null;
            foreach (var camera in cameras)
            {
                if (camera.cameraType == CameraType.Game && camera.targetTexture == null)
                {
                    target = camera;
                }
            }

            if (target != null)
            {
                TargetSize = new Vector2Int(target.pixelWidth, target.pixelHeight);
            }
        }

        /// <summary>Main thread, after the uploads: binds the shared data (read when the draws execute).</summary>
        public void Prepare()
        {
            // Overlay draws need the index buffer too.
            EnsureIndices(MaxDrawCount());
            // Globals, so that custom materials see them too.
            Shader.SetGlobalBuffer(PrimitivesId, YauiSystem.Primitives.Buffer);
            Shader.SetGlobalBuffer(ExtsId, YauiSystem.Exts.Buffer);
            Shader.SetGlobalBuffer(NodesId, YauiSystem.Nodes.Gpu.Buffer);
            Shader.SetGlobalBuffer(ClipsId, YauiSystem.Clips.Buffer);
            YauiSystem.Textures.Atlas.RestoreIfLost();
        }

        /// <summary>
        /// Main thread, at submission: queues the world space panels for the cameras of this frame. Queuing at
        /// collection (inside the render pipeline) is too late; the buffers are bound and filled before they draw.
        /// </summary>
        public void QueueWorldPanels()
        {
            var maxQuads = 0;
            foreach (var panel in YauiSystem.AllPanels)
            {
                maxQuads = Math.Max(maxQuads, panel.DrawCount);
            }

            EnsureIndices(maxQuads);
            foreach (var panel in YauiSystem.AllPanels)
            {
                if (panel.DrawCount > 0 && panel.OrderBuffer != null && panel.Panel.RenderMode == PanelRenderMode.World)
                {
                    RenderWorld(panel);
                }
            }
        }

        static int MaxDrawCount()
        {
            var max = 0;
            foreach (var panel in YauiSystem.AllPanels)
            {
                // Text glyph blocks may grow at collection; leave room.
                max = Math.Max(max, panel.DrawCount);
            }

            return max;
        }

        void EnsureIndices(int quads)
        {
            if (indices != null && indexedQuads >= quads)
            {
                return;
            }

            indexedQuads = Math.Max(1024, Mathf.NextPowerOfTwo(quads));
            indices?.Dispose();
            indices = new GraphicsBuffer(GraphicsBuffer.Target.Index, indexedQuads * 6, sizeof(uint));
            var data = new uint[indexedQuads * 6];
            for (var q = 0; q < indexedQuads; q++)
            {
                var v = (uint)q * 4;
                // Two triangles: (0,0) (0,1) (1,0) / (1,0) (0,1) (1,1), corner = x | y << 1.
                data[q * 6 + 0] = v;
                data[q * 6 + 1] = v + 2;
                data[q * 6 + 2] = v + 1;
                data[q * 6 + 3] = v + 1;
                data[q * 6 + 4] = v + 2;
                data[q * 6 + 5] = v + 3;
            }

            indices.SetData(data);
        }

        readonly List<Camera> worldCameras = new();

        /// <summary>Binds the textures of a segment to the slots of its draw.</summary>
        void BindTextures(PanelState panel, DrawSegment segment, MaterialPropertyBlock properties)
        {
            var registry = YauiSystem.Textures;
            var ids0 = new Vector4(-1f, -1f, -1f, -1f);
            var ids1 = ids0;
            for (var slot = 0; slot < TextureRegistry.SlotCount; slot++)
            {
                var id = slot < segment.TextureCount ? panel.SegmentTextures[segment.TextureStart + slot] : 0;
                var texture = registry.Get(id);
                properties.SetTexture(TextureIds[slot], texture != null ? texture : Texture2D.whiteTexture);
                atlasParams[slot] = registry.Parameters(id);
                if (id > 0)
                {
                    if (slot < 4)
                    {
                        ids0[slot] = id;
                    }
                    else
                    {
                        ids1[slot - 4] = id;
                    }
                }
            }

            properties.SetVector(TexIds0Id, ids0);
            properties.SetVector(TexIds1Id, ids1);
            properties.SetVectorArray(AtlasParamsId, atlasParams);
        }

        void RenderWorld(PanelState panel)
        {
            var matrix = panel.Panel.CanvasToWorld;
            var size = (Vector2)panel.CanvasSize;
            var bounds = new Bounds(matrix.MultiplyPoint3x4(Vector3.zero), Vector3.zero);
            bounds.Encapsulate(matrix.MultiplyPoint3x4(new Vector3(size.x, 0f)));
            bounds.Encapsulate(matrix.MultiplyPoint3x4(new Vector3(0f, size.y)));
            bounds.Encapsulate(matrix.MultiplyPoint3x4(new Vector3(size.x, size.y)));
            for (var i = 0; i < panel.Segments.Count; i++)
            {
                var properties = panel.SegmentProperties[i];
                properties.SetBuffer(OrderId, panel.OrderBuffer);
                properties.SetInt(OrderOffsetId, panel.Segments[i].Start);
                properties.SetMatrix(PanelMatrixId, matrix);
                BindTextures(panel, panel.Segments[i], properties);
            }

            if (panel.Segments.Count == 1)
            {
                RenderSegment(panel, 0, bounds, null);
                return;
            }

            // URP sorts transparent draws back to front and reorders ties by material, which would break the order
            // of the segments (a mask's shape, its content, and its removal). Each segment is placed a little closer
            // to each camera than the one before.
            CollectWorldCameras();
            foreach (var camera in worldCameras)
            {
                var center = bounds.center;
                var towards = camera.orthographic
                    ? -camera.transform.forward
                    : (camera.transform.position - center).normalized;
                var step = 1e-4f + 1e-5f * Vector3.Distance(camera.transform.position, center);
                for (var i = 0; i < panel.Segments.Count; i++)
                {
                    var shifted = bounds;
                    shifted.center = center + towards * (step * i);
                    RenderSegment(panel, i, shifted, camera);
                }
            }
        }

        void RenderSegment(PanelState panel, int index, Bounds bounds, Camera camera)
        {
            var renderParams = new RenderParams(MaterialOf(panel, panel.Segments[index]))
            {
                camera = camera,
                worldBounds = bounds,
                matProps = panel.SegmentProperties[index],
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false,
                layer = panel.Panel.gameObject.layer,
            };
            Graphics.RenderPrimitivesIndexed(renderParams, MeshTopology.Triangles, indices,
                panel.Segments[index].Count * 6);
        }

        void CollectWorldCameras()
        {
            worldCameras.Clear();
            foreach (var camera in Camera.allCameras)
            {
                worldCameras.Add(camera);
            }
#if UNITY_EDITOR
            foreach (UnityEditor.SceneView view in UnityEditor.SceneView.sceneViews)
            {
                if (view.camera != null)
                {
                    worldCameras.Add(view.camera);
                }
            }
#endif
        }

        void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (camera.cameraType == CameraType.SceneView)
            {
                // Overlay panels as quads in the scene (world space panels are drawn by URP in every camera).
                foreach (var panel in YauiSystem.AllPanels)
                {
                    if (panel.DrawCount > 0 && panel.Panel.RenderMode == PanelRenderMode.Overlay)
                    {
                        camera.GetUniversalAdditionalCameraData().scriptableRenderer.EnqueuePass(sceneViewPass);
                        return;
                    }
                }

                return;
            }

            if (camera != target)
            {
                return;
            }

            foreach (var panel in YauiSystem.AllPanels)
            {
                if (panel.DrawCount > 0 && panel.Panel.RenderMode == PanelRenderMode.Overlay)
                {
                    camera.GetUniversalAdditionalCameraData().scriptableRenderer.EnqueuePass(pass);
                    return;
                }
            }
        }

        void PrepareOverlayDraws(Camera camera)
        {
            sorted.Clear();
            foreach (var panel in YauiSystem.AllPanels)
            {
                if (panel.DrawCount > 0 && panel.OrderBuffer != null &&
                    panel.Panel.RenderMode == PanelRenderMode.Overlay)
                {
                    sorted.Add(panel);
                }
            }

            sorted.Sort(SortOrderComparer.Instance);
            draws.Clear();
            overlayNeedsStencil = false;
            foreach (var panel in sorted)
            {
                overlayNeedsStencil |= panel.HasMasks;

                // Canvas space: origin at the top-left, Y down.
                var projection = Matrix4x4.Ortho(0f, panel.CanvasSize.x, panel.CanvasSize.y, 0f, -1f, 1f);
                for (var i = 0; i < panel.Segments.Count; i++)
                {
                    var segment = panel.Segments[i];
                    var properties = panel.SegmentProperties[i];
                    properties.SetBuffer(OrderId, panel.OrderBuffer);
                    properties.SetInt(OrderOffsetId, segment.Start);
                    properties.SetFloat(PixelSizeId, panel.CanvasSize.x / camera.pixelWidth);
                    BindTextures(panel, segment, properties);
                    var segmentMaterial = MaterialOf(panel, segment);
                    var pass = segmentMaterial.FindPass("Overlay");
                    if (pass < 0)
                    {
                        continue;
                    }

                    draws.Add(new DrawItem
                    {
                        Count = segment.Count,
                        Projection = projection,
                        Properties = properties,
                        Material = segmentMaterial,
                        Pass = pass,
                    });
                }
            }
        }

        /// <summary>
        /// The material of a draw: the segment's own or the uber shader (the mask shader for mask shapes), with or
        /// without per-pixel clips, and with the stencil state of the segment.
        /// </summary>
        Material MaterialOf(PanelState panel, DrawSegment segment)
        {
            Material baseMaterial;
            if (segment.Kind != SegmentKind.Draw)
            {
                baseMaterial = panel.NeedsPixelClip ? pixelClipMaskMaterial : maskMaterial;
            }
            else if (segment.Material != null)
            {
                baseMaterial = segment.Material;
            }
            else
            {
                baseMaterial = panel.NeedsPixelClip ? pixelClipMaterial : material;
            }

            if (segment.Kind == SegmentKind.Draw && segment.StencilDepth == 0)
            {
                return baseMaterial;
            }

            var key = (baseMaterial, segment.Kind, segment.StencilDepth);
            if (!stencilMaterials.TryGetValue(key, out var derived) || derived == null)
            {
                derived = new Material(baseMaterial) { hideFlags = HideFlags.HideAndDontSave };
                stencilMaterials[key] = derived;
            }
            else if (segment.Material != null)
            {
                // A custom material may have changed since.
                derived.CopyPropertiesFromMaterial(baseMaterial);
            }

            switch (segment.Kind)
            {
                case SegmentKind.MaskPush:
                    derived.SetFloat(StencilRefId, segment.StencilDepth - 1);
                    derived.SetFloat(StencilCompId, (float)CompareFunction.Equal);
                    derived.SetFloat(StencilPassId, (float)StencilOp.IncrementSaturate);
                    break;
                case SegmentKind.MaskPop:
                    derived.SetFloat(StencilRefId, segment.StencilDepth);
                    derived.SetFloat(StencilCompId, (float)CompareFunction.Equal);
                    derived.SetFloat(StencilPassId, (float)StencilOp.DecrementSaturate);
                    break;
                default:
                    derived.SetFloat(StencilRefId, segment.StencilDepth);
                    derived.SetFloat(StencilCompId, (float)CompareFunction.Equal);
                    derived.SetFloat(StencilPassId, (float)StencilOp.Keep);
                    break;
            }

            return derived;
        }

        static void DestroyMaterial(Material m)
        {
            if (m == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(m);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(m);
            }
        }

        public void Dispose()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            indices?.Dispose();
            indices = null;
            DestroyMaterial(material);
            DestroyMaterial(pixelClipMaterial);
            DestroyMaterial(maskMaterial);
            DestroyMaterial(pixelClipMaskMaterial);
            foreach (var derived in stencilMaterials.Values)
            {
                DestroyMaterial(derived);
            }

            stencilMaterials.Clear();
        }

        sealed class SortOrderComparer : IComparer<PanelState>
        {
            public static readonly SortOrderComparer Instance = new();

            public int Compare(PanelState a, PanelState b) => a.Panel.SortOrder.CompareTo(b.Panel.SortOrder);
        }

        /// <summary>Draws overlay panels in the Scene view with the world pass, at <see cref="YauiPanel.SceneViewCanvasToWorld"/>.</summary>
        sealed class SceneViewRenderPass : ScriptableRenderPass
        {
            readonly PanelRenderer renderer;
            readonly List<DrawItem> draws = new();

            public SceneViewRenderPass(PanelRenderer renderer)
            {
                this.renderer = renderer;
                renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
            }

            sealed class PassData
            {
                public List<DrawItem> Draws;
                public GraphicsBuffer Indices;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var resourceData = frameData.Get<UniversalResourceData>();
                draws.Clear();
                foreach (var panel in YauiSystem.AllPanels)
                {
                    if (panel.DrawCount == 0 || panel.OrderBuffer == null ||
                        panel.Panel.RenderMode != PanelRenderMode.Overlay)
                    {
                        continue;
                    }

                    var matrix = panel.Panel.SceneViewCanvasToWorld;
                    for (var i = 0; i < panel.Segments.Count; i++)
                    {
                        var segment = panel.Segments[i];
                        var segmentMaterial = renderer.MaterialOf(panel, segment);
                        var pass = segmentMaterial.FindPass("World");
                        if (pass < 0)
                        {
                            continue;
                        }

                        // A separate block: the game view draws of this frame use the segment's own.
                        var properties = new MaterialPropertyBlock();
                        properties.SetBuffer(OrderId, panel.OrderBuffer);
                        properties.SetInt(OrderOffsetId, segment.Start);
                        properties.SetMatrix(PanelMatrixId, matrix);
                        renderer.BindTextures(panel, segment, properties);
                        draws.Add(new DrawItem
                        {
                            Count = segment.Count,
                            Properties = properties,
                            Material = segmentMaterial,
                            Pass = pass,
                        });
                    }
                }

                using var builder = renderGraph.AddRasterRenderPass<PassData>("Yaui Scene View", out var data);
                builder.SetRenderAttachment(resourceData.activeColorTexture, 0);
                builder.SetRenderAttachmentDepth(resourceData.activeDepthTexture, AccessFlags.ReadWrite);
                builder.AllowPassCulling(false);
                data.Draws = draws;
                data.Indices = renderer.indices;
                builder.SetRenderFunc(static (PassData d, RasterGraphContext context) =>
                {
                    foreach (var draw in d.Draws)
                    {
                        context.cmd.DrawProcedural(d.Indices, Matrix4x4.identity, draw.Material, draw.Pass,
                            MeshTopology.Triangles, draw.Count * 6, 1, draw.Properties);
                    }
                });
            }
        }

        sealed class OverlayRenderPass : ScriptableRenderPass
        {
            readonly PanelRenderer renderer;

            public OverlayRenderPass(PanelRenderer renderer)
            {
                this.renderer = renderer;
                renderPassEvent = RenderPassEvent.AfterRendering + 10;
            }

            sealed class PassData
            {
                public List<DrawItem> Draws;
                public GraphicsBuffer Indices;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var resourceData = frameData.Get<UniversalResourceData>();
                var cameraData = frameData.Get<UniversalCameraData>();
                renderer.PrepareOverlayDraws(cameraData.camera);

                using var builder = renderGraph.AddRasterRenderPass<PassData>("Yaui Overlay", out var data);
                builder.SetRenderAttachment(resourceData.activeColorTexture, 0);
                if (renderer.overlayNeedsStencil)
                {
                    // The overlay draws after post-processing, where there is no depth-stencil buffer: a transient,
                    // cleared one for the masks.
                    // The back buffer has no descriptor: it has the camera's pixel size.
                    var width = cameraData.camera.pixelWidth;
                    var height = cameraData.camera.pixelHeight;
                    var samples = MSAASamples.None;
                    if (!resourceData.isActiveTargetBackBuffer)
                    {
                        var colorDesc = renderGraph.GetTextureDesc(resourceData.activeColorTexture);
                        width = colorDesc.width;
                        height = colorDesc.height;
                        samples = colorDesc.msaaSamples;
                    }

                    var desc = new TextureDesc(width, height)
                    {
                        name = "Yaui Stencil",
                        format = SystemInfo.GetGraphicsFormat(DefaultFormat.DepthStencil),
                        msaaSamples = samples,
                        clearBuffer = true,
                    };
                    builder.SetRenderAttachmentDepth(renderGraph.CreateTexture(desc), AccessFlags.Write);
                }

                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);
                data.Draws = renderer.draws;
                data.Indices = renderer.indices;
                builder.SetRenderFunc(static (PassData d, RasterGraphContext context) =>
                {
                    foreach (var draw in d.Draws)
                    {
                        context.cmd.SetViewProjectionMatrices(Matrix4x4.identity, draw.Projection);
                        context.cmd.DrawProcedural(d.Indices, Matrix4x4.identity, draw.Material, draw.Pass,
                            MeshTopology.Triangles, draw.Count * 6, 1, draw.Properties);
                    }
                });
            }
        }
    }
}
