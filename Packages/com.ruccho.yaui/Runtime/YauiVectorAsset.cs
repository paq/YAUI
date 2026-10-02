using System;
using System.Collections.Generic;
using UnityEngine;
using Yaui.Vector;

namespace Yaui
{
    /// <summary>An SVG baked into layered quadratic outlines for the Slug renderer.</summary>
    public sealed class YauiVectorAsset : ScriptableObject
    {
        [SerializeField] private string sourceSvg;
        [SerializeField] private Rect viewBox;
        [SerializeField] private VectorLayer[] layers;
        [NonSerialized] private IReadOnlyList<VectorLayer> _readLayers;

        /// <summary>The source document.</summary>
        public string SourceSvg => sourceSvg;

        /// <summary>The SVG coordinate rectangle, with Y down.</summary>
        public Rect ViewBox => viewBox;

        /// <summary>The paints in document order.</summary>
        public IReadOnlyList<VectorLayer> Layers => _readLayers ??= Array.AsReadOnly(layers ?? Array.Empty<VectorLayer>());

        /// <summary>Parses and bakes an SVG. The caller owns the returned runtime asset.</summary>
        public static YauiVectorAsset FromSvg(string svg)
        {
            if (string.IsNullOrWhiteSpace(svg)) throw new ArgumentException("An SVG document is required.", nameof(svg));

            var baked = VectorBaker.Bake(svg, out var bounds);
            var asset = CreateInstance<YauiVectorAsset>();
            asset.sourceSvg = svg;
            asset.viewBox = bounds;
            asset.layers = baked;
            return asset;
        }
    }

    /// <summary>A quadratic curve in normalized layer coordinates. Positive weights describe rational arcs.</summary>
    [Serializable]
    public struct VectorCurve
    {
        public Vector2 P0;
        public Vector2 P1;
        public Vector2 P2;
        public float Weight;

        internal VectorCurve(Vector2 p0, Vector2 p1, Vector2 p2, float weight = 1f)
        {
            P0 = p0;
            P1 = p1;
            P2 = p2;
            Weight = weight;
        }
    }

    /// <summary>A paint, its outline and the ray acceleration bands.</summary>
    [Serializable]
    public sealed class VectorLayer
    {
        [SerializeField] private Color color;
        [SerializeField] private bool currentColor;
        [SerializeField] private bool evenOdd;
        [SerializeField] private Rect bounds;
        [SerializeField] private VectorCurve[] curves;
        [SerializeField] private VectorBand[] bands;
        [SerializeField] private int[] indices;
        [SerializeField] private int bandCount;
        [NonSerialized] private IReadOnlyList<VectorCurve> _readCurves;
        [NonSerialized] private IReadOnlyList<VectorBand> _readBands;
        [NonSerialized] private IReadOnlyList<int> _readIndices;

        public Color Color => color;
        public bool CurrentColor => currentColor;
        public bool EvenOdd => evenOdd;
        /// <summary>The layer rectangle in viewBox coordinates.</summary>
        public Rect Bounds => bounds;
        public IReadOnlyList<VectorCurve> Curves => _readCurves ??= Array.AsReadOnly(curves);
        /// <summary>Horizontal bands followed by vertical bands.</summary>
        public IReadOnlyList<VectorBand> Bands => _readBands ??= Array.AsReadOnly(bands);
        public IReadOnlyList<int> Indices => _readIndices ??= Array.AsReadOnly(indices);
        /// <summary>The number of bands in each direction, from 1 to 16.</summary>
        public int BandCount => bandCount;

        internal VectorLayer(List<VectorCurve> outline, Color paint, bool current, bool parity)
        {
            color = paint;
            currentColor = current;
            evenOdd = parity;
            var min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            var max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            foreach (var c in outline)
            {
                min = Vector2.Min(min, Vector2.Min(c.P0, Vector2.Min(c.P1, c.P2)));
                max = Vector2.Max(max, Vector2.Max(c.P0, Vector2.Max(c.P1, c.P2)));
            }

            max = Vector2.Max(max, min + Vector2.one * 1e-5f);
            bounds = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
            curves = outline.ToArray();
            for (var i = 0; i < curves.Length; i++)
            {
                var c = curves[i];
                c.P0 = (c.P0 - min) / bounds.size;
                c.P1 = (c.P1 - min) / bounds.size;
                c.P2 = (c.P2 - min) / bounds.size;
                curves[i] = c;
            }

            bandCount = Mathf.Clamp((curves.Length + 7) / 8, 1, 16);
            bands = new VectorBand[bandCount * 2];
            var list = new List<int>();
            for (var axis = 0; axis < 2; axis++)
            for (var b = 0; b < bandCount; b++)
            {
                var perpendicular = 1 - axis;
                var candidates = new List<int>();
                for (var i = 0; i < curves.Length; i++)
                {
                    var c = curves[i];
                    var low = Mathf.Min(c.P0[perpendicular], Mathf.Min(c.P1[perpendicular], c.P2[perpendicular]));
                    var high = Mathf.Max(c.P0[perpendicular], Mathf.Max(c.P1[perpendicular], c.P2[perpendicular]));
                    if (low <= (b + 1f) / bandCount && high >= (float)b / bandCount) candidates.Add(i);
                }

                candidates.Sort((a, d) => Maximum(curves[d], axis).CompareTo(Maximum(curves[a], axis)));
                bands[axis * bandCount + b] = new VectorBand(list.Count, candidates.Count);
                list.AddRange(candidates);
            }

            indices = list.ToArray();
        }

        internal static float Maximum(VectorCurve c, int axis)
        {
            return Mathf.Max(c.P0[axis], Mathf.Max(c.P1[axis], c.P2[axis]));
        }
    }

    /// <summary>A range in a layer's curve index array.</summary>
    [Serializable]
    public struct VectorBand
    {
        public int Start;
        public int Count;

        internal VectorBand(int start, int count)
        {
            Start = start;
            Count = count;
        }
    }
}
