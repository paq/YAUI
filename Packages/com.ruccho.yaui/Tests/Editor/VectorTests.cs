using System;
using NUnit.Framework;
using UnityEngine;
using Yaui.Core;
using Yaui.Rendering;
using Object = UnityEngine.Object;

namespace Yaui.Tests
{
    internal static class SlugReference
    {
        internal static float Coverage(VectorLayer layer, Vector2 point, Vector2 pixels)
        {
            point = (point - layer.Bounds.min) / layer.Bounds.size;
            pixels = Vector2.Scale(pixels, layer.Bounds.size);
            var h = Ray(layer, point, pixels.x, false);
            var v = Ray(layer, point, pixels.y, true);
            if (layer.EvenOdd)
            {
                h.x = 1f - Mathf.Abs(1f - Mathf.Abs(h.x) % 2f);
                v.x = 1f - Mathf.Abs(1f - Mathf.Abs(v.x) % 2f);
            }

            var weighted = Mathf.Abs((h.x * h.y + v.x * v.y) / Mathf.Max(h.y + v.y, 1e-7f));
            return Mathf.Clamp01(Mathf.Max(weighted, Mathf.Min(Mathf.Abs(h.x), Mathf.Abs(v.x))));
        }

        private static Vector2 Ray(VectorLayer layer, Vector2 point, float pixels, bool vertical)
        {
            var perpendicular = vertical ? 0 : 1;
            var axis = 1 - perpendicular;
            var bandIndex = Mathf.Clamp(Mathf.FloorToInt(point[perpendicular] * layer.BandCount), 0, layer.BandCount - 1);
            var band = layer.Bands[bandIndex + (vertical ? layer.BandCount : 0)];
            var coverage = 0f;
            var weight = 0f;
            for (var i = 0; i < band.Count; i++)
            {
                var c = layer.Curves[layer.Indices[band.Start + i]];
                c.P0 -= point;
                c.P1 -= point;
                c.P2 -= point;
                if (VectorLayer.Maximum(c, axis) * pixels < -0.5f) break;
                var shift = (c.P0[perpendicular] > 0 ? 2 : 0) + (c.P1[perpendicular] > 0 ? 4 : 0) +
                            (c.P2[perpendicular] > 0 ? 8 : 0);
                var code = (0x2E74 >> shift) & 3;
                if (code == 0) continue;
                var a = c.P0[perpendicular] - 2 * c.Weight * c.P1[perpendicular] + c.P2[perpendicular];
                var b = c.P0[perpendicular] - c.Weight * c.P1[perpendicular];
                var d = Mathf.Sqrt(Mathf.Max(b * b - a * c.P0[perpendicular], 0));
                for (var root = 0; root < 2; root++)
                {
                    if ((code & (1 << root)) == 0) continue;
                    var t = Mathf.Abs(a) < 1e-6f ? c.P0[perpendicular] / (2 * b) : (b + (root == 0 ? -d : d)) / a;
                    var s = 1 - t;
                    var denominator = s * s + 2 * c.Weight * s * t + t * t;
                    var distance = (s * s * c.P0[axis] + 2 * c.Weight * s * t * c.P1[axis] +
                                    t * t * c.P2[axis]) / denominator * pixels;
                    coverage += (root == 0 ? 1 : -1) * Mathf.Clamp01(distance + 0.5f);
                    weight = Mathf.Max(weight, Mathf.Clamp01(1 - Mathf.Abs(distance) * 2));
                }
            }

            return new Vector2(vertical ? -coverage : coverage, weight);
        }
    }

    public class VectorTests
    {
        private static string Svg(string content, string attributes = "")
        {
            return "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 24 24\" " + attributes + ">" + content + "</svg>";
        }

        private static void Check(string svg, Vector2[] inside, Vector2[] outside)
        {
            var asset = YauiVectorAsset.FromSvg(svg);
            try
            {
                Assert.That(asset.Layers.Count, Is.GreaterThan(0));
                foreach (var point in inside)
                    Assert.That(Coverage(asset, point), Is.GreaterThan(0.99f), "inside " + point);
                foreach (var point in outside)
                    Assert.That(Coverage(asset, point), Is.LessThan(0.01f), "outside " + point);
                foreach (var layer in asset.Layers) Bands(layer);
            }
            finally { Object.DestroyImmediate(asset); }
        }

        private static float Coverage(YauiVectorAsset asset, Vector2 point)
        {
            var result = 0f;
            foreach (var layer in asset.Layers)
                result = Mathf.Max(result, SlugReference.Coverage(layer, point, Vector2.one * 100f));
            return result;
        }

        internal static void Bands(VectorLayer layer)
        {
            for (var axis = 0; axis < 2; axis++)
            for (var b = 0; b < layer.BandCount; b++)
            {
                var band = layer.Bands[axis * layer.BandCount + b];
                var previous = float.PositiveInfinity;
                var listed = new System.Collections.Generic.HashSet<int>();
                for (var j = 0; j < band.Count; j++)
                {
                    var index = layer.Indices[band.Start + j];
                    Assert.That(index, Is.InRange(0, layer.Curves.Count - 1));
                    Assert.That(listed.Add(index), Is.True);
                    var maximum = VectorLayer.Maximum(layer.Curves[index], axis);
                    Assert.That(maximum, Is.LessThanOrEqualTo(previous));
                    previous = maximum;
                }

                for (var c = 0; c < layer.Curves.Count; c++)
                {
                    var curve = layer.Curves[c];
                    var k = 1 - axis;
                    var min = Mathf.Min(curve.P0[k], Mathf.Min(curve.P1[k], curve.P2[k]));
                    var max = Mathf.Max(curve.P0[k], Mathf.Max(curve.P1[k], curve.P2[k]));
                    if (min <= (b + 1f) / layer.BandCount && max >= (float)b / layer.BandCount)
                        Assert.That(listed.Contains(c), Is.True, "missing curve " + c);
                }
            }
        }

        [Test]
        public void FilledRectangle()
        {
            Check(Svg("<rect x=\"4\" y=\"6\" width=\"16\" height=\"12\"/>"),
                new[] { new Vector2(12, 12), new Vector2(4.1f, 6.1f) },
                new[] { new Vector2(3, 12), new Vector2(12, 19) });
        }

        [Test]
        public void FilledCircle()
        {
            Check(Svg("<circle cx=\"12\" cy=\"12\" r=\"8\"/>"),
                new[] { new Vector2(12, 12), new Vector2(17, 17) },
                new[] { new Vector2(20, 20), new Vector2(12, 21) });
        }

        [Test]
        public void RoundCaps()
        {
            Check(Svg("<path d=\"M6 12H18\"/>", "fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\""),
                new[] { new Vector2(5.2f, 12), new Vector2(18.7f, 12.5f), new Vector2(12, 12) },
                new[] { new Vector2(4.8f, 12), new Vector2(19, 13), new Vector2(12, 13.2f) });
        }

        [Test]
        public void RoundJoins()
        {
            Check(Svg("<path d=\"M6 18V6H18\"/>", "fill=\"none\" stroke=\"black\" stroke-width=\"2\" stroke-linejoin=\"round\""),
                new[] { new Vector2(5.3f, 5.3f), new Vector2(6, 12), new Vector2(12, 6) },
                new[] { new Vector2(5.1f, 5.1f), new Vector2(12, 12) });
        }

        [Test]
        public void ClosedStroke()
        {
            Check(Svg("<rect x=\"6\" y=\"6\" width=\"12\" height=\"12\"/>",
                    "fill=\"none\" stroke=\"black\" stroke-width=\"2\" stroke-linejoin=\"round\""),
                new[] { new Vector2(6, 12), new Vector2(18, 12), new Vector2(5.3f, 5.3f) },
                new[] { new Vector2(12, 12), new Vector2(4, 12) });
        }

        [TestCase("butt", 0.01f)]
        [TestCase("square", 0.99f)]
        public void CapStyles(string cap, float expected)
        {
            var asset = YauiVectorAsset.FromSvg(Svg("<path d=\"M6 12H18\"/>",
                "fill=\"none\" stroke=\"black\" stroke-width=\"2\" stroke-linecap=\"" + cap + "\""));
            try { Assert.That(Coverage(asset, new Vector2(5.2f, 12.8f)), Is.EqualTo(expected > 0.5f ? 1f : 0f).Within(0.01f)); }
            finally { Object.DestroyImmediate(asset); }
        }

        [TestCase("miter", true)]
        [TestCase("bevel", false)]
        public void JoinStyles(string join, bool inside)
        {
            var asset = YauiVectorAsset.FromSvg(Svg("<path d=\"M6 18V6H18\"/>",
                "fill=\"none\" stroke=\"black\" stroke-width=\"2\" stroke-linejoin=\"" + join + "\""));
            try { Assert.That(Coverage(asset, new Vector2(5.2f, 5.2f)), Is.EqualTo(inside ? 1f : 0f).Within(0.01f)); }
            finally { Object.DestroyImmediate(asset); }
        }

        [TestCase(LucideSvg.Radio)]
        [TestCase(LucideSvg.Clapperboard)]
        [TestCase(LucideSvg.Search)]
        public void LucideIcons(string svg)
        {
            var asset = YauiVectorAsset.FromSvg(svg);
            try
            {
                Assert.That(asset.ViewBox, Is.EqualTo(new Rect(0, 0, 24, 24)));
                Assert.That(asset.Layers.Count, Is.GreaterThan(0));
                foreach (var layer in asset.Layers)
                {
                    Assert.That(layer.CurrentColor, Is.True);
                    Assert.That(layer.Curves.Count, Is.LessThan(5000));
                    Bands(layer);
                }
                Assert.That(Coverage(asset, Vector2.zero), Is.LessThan(0.01f));
                var point = svg == LucideSvg.Radio ? new Vector2(14, 12) :
                    svg == LucideSvg.Search ? new Vector2(3, 11) : new Vector2(12, 21);
                Assert.That(Coverage(asset, point), Is.GreaterThan(0.99f));
            }
            finally { Object.DestroyImmediate(asset); }
        }

        [Test]
        public void EvenOddAndTransforms()
        {
            Check(Svg("<g transform=\"translate(2 2)\"><path fill-rule=\"evenodd\" d=\"M2 2H18V18H2Z M6 6H14V14H6Z\"/></g>"),
                new[] { new Vector2(5, 5) }, new[] { new Vector2(12, 12), new Vector2(1, 1) });
        }

        [Test]
        public void RoundCapArcsStayOnTheCircle()
        {
            var asset = YauiVectorAsset.FromSvg(Svg("<path d=\"M6 12H18\"/>",
                "fill=\"none\" stroke=\"black\" stroke-width=\"2\" stroke-linecap=\"round\""));
            try
            {
                var layer = asset.Layers[0];
                var arcs = 0;
                foreach (var curve in layer.Curves)
                {
                    if (curve.Weight == 1f) continue;
                    arcs++;
                    for (var step = 0; step <= 16; step++)
                    {
                        var t = step / 16f;
                        var u = 1f - t;
                        var p = (u * u * curve.P0 + 2f * curve.Weight * u * t * curve.P1 + t * t * curve.P2) /
                                (u * u + 2f * curve.Weight * u * t + t * t);
                        p = Vector2.Scale(p, layer.Bounds.size) + layer.Bounds.min;
                        var center = p.x < 12f ? new Vector2(6f, 12f) : new Vector2(18f, 12f);
                        Assert.That((p - center).magnitude, Is.EqualTo(1f).Within(3e-6f));
                    }
                }

                Assert.That(arcs, Is.EqualTo(4));
            }
            finally { Object.DestroyImmediate(asset); }
        }

        [Test]
        public void MiterLimitFallsBackToBevel()
        {
            Check(Svg("<path d=\"M6 18V6H18\"/>",
                    "fill=\"none\" stroke=\"black\" stroke-width=\"2\" stroke-linejoin=\"miter\" stroke-miterlimit=\"1\""),
                new[] { new Vector2(6, 12) }, new[] { new Vector2(5.2f, 5.2f) });
        }

        [Test]
        public void NonzeroViewBoxAndNonuniformStrokeTransform()
        {
            Check("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"10 20 24 24\"><g transform=\"translate(12 24) scale(2 1)\"><path d=\"M2 4H8\" fill=\"none\" stroke=\"black\" stroke-width=\"2\" stroke-linecap=\"round\"/></g></svg>",
                new[] { new Vector2(14.5f, 28), new Vector2(29.5f, 28) },
                new[] { new Vector2(13.5f, 28), new Vector2(22, 29.2f) });
        }

        [Test]
        public void SvgMeasuresItsAspectAndReleasesOnDisable()
        {
            var root = new GameObject("Vector test panel");
            var asset = YauiVectorAsset.FromSvg("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 24 12\"><rect width=\"24\" height=\"12\"/></svg>");
            try
            {
                var panel = root.AddComponent<YauiPanel>();
                panel.RenderMode = PanelRenderMode.World;
                panel.ReferenceResolution = new Vector2(200f, 200f);
                var child = new GameObject("SVG");
                child.transform.SetParent(root.transform, false);
                var svg = child.AddComponent<YauiSvg>();
                svg.Vector = asset;
                var layout = LayoutStyle.Default;
                layout.width = 48f;
                layout.alignSelf = FlexAlign.FlexStart;
                svg.Layout = layout;
                YauiPanel.ForceUpdate();
                var range = svg.ContentRange;
                var primitive = YauiSystem.Primitives.Read(range.Start);
                Assert.That(primitive.Rect.z, Is.EqualTo(48f).Within(0.01f));
                Assert.That(primitive.Rect.w, Is.EqualTo(24f).Within(0.01f));
                Assert.That(primitive.Flags, Is.EqualTo(PrimitiveFlags.Vector));
                svg.enabled = false;
                Assert.That(YauiSystem.Vectors.Layers.Read((int)primitive.BorderColor.x).BandCount, Is.Zero);
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(asset); }
        }

        [Test]
        public void CurrentColorAndFixedPaintRemainDistinct()
        {
            var asset = YauiVectorAsset.FromSvg(Svg("<rect width=\"10\" height=\"10\" fill=\"currentColor\"/><circle cx=\"16\" cy=\"16\" r=\"4\" fill=\"#ea17b9\"/>"));
            try
            {
                Assert.That(asset.Layers[0].CurrentColor, Is.True);
                Assert.That(asset.Layers[1].CurrentColor, Is.False);
                Assert.That(asset.SourceSvg, Does.Contain("currentColor"));
            }
            finally { Object.DestroyImmediate(asset); }
        }

        [Test]
        public void SvgFollowsAnAssetChangedInPlace()
        {
            var root = new GameObject("Vector test panel");
            var asset = YauiVectorAsset.FromSvg(Svg("<rect width=\"24\" height=\"24\"/>"));
            var other = YauiVectorAsset.FromSvg(Svg("<rect width=\"10\" height=\"10\" fill=\"currentColor\"/><circle cx=\"16\" cy=\"16\" r=\"4\" fill=\"#ea17b9\"/>"));
            try
            {
                var panel = root.AddComponent<YauiPanel>();
                panel.RenderMode = PanelRenderMode.World;
                panel.ReferenceResolution = new Vector2(200f, 200f);
                var child = new GameObject("SVG");
                child.transform.SetParent(root.transform, false);
                var svg = child.AddComponent<YauiSvg>();
                svg.Vector = asset;
                var layout = LayoutStyle.Default;
                layout.width = 48f;
                layout.alignSelf = FlexAlign.FlexStart;
                svg.Layout = layout;
                YauiPanel.ForceUpdate();
                Assert.That(asset.Layers.Count, Is.EqualTo(1));

                JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(other), asset);
                YauiPanel.ForceUpdate();

                Assert.That(asset.Layers.Count, Is.EqualTo(2));
                var start = svg.ContentRange.Start;
                for (var i = 0; i < 2; i++)
                {
                    var primitive = YauiSystem.Primitives.Read(start + i);
                    Assert.That(primitive.Flags, Is.EqualTo(PrimitiveFlags.Vector));
                    var layer = YauiSystem.Vectors.Layers.Read((int)primitive.BorderColor.x);
                    Assert.That(layer.BandCount, Is.EqualTo((uint)other.Layers[i].BandCount));
                }
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(asset); Object.DestroyImmediate(other); }
        }

        [Test]
        public void LeasesShareLayersAndReleasedHandlesStayInvalid()
        {
            var asset = YauiVectorAsset.FromSvg(Svg("<rect width=\"24\" height=\"24\"/>"));
            var a = YauiVector.Acquire(asset);
            var b = YauiVector.Acquire(asset);
            try
            {
                Assert.That(a.Layer(0), Is.EqualTo(b.Layer(0)));
                a.Release();
                a.Release();
                Assert.That(a.IsValid, Is.False);
                Assert.That(b.IsValid, Is.True);
                b.Release();
                var c = YauiVector.Acquire(asset);
                Assert.That(a.IsValid, Is.False);
                Assert.That(b.IsValid, Is.False);
                c.Release();
            }
            finally { a.Release(); b.Release(); Object.DestroyImmediate(asset); }
        }

        [Test]
        public void HandlesDoNotSurviveStoreGenerationChanges()
        {
            var asset = YauiVectorAsset.FromSvg(Svg("<rect width=\"24\" height=\"24\"/>"));
            var old = YauiVector.Acquire(asset);
            try
            {
                YauiSystem.Shutdown();
                Assert.That(old.IsValid, Is.False);
                var current = YauiVector.Acquire(asset);
                try
                {
                    old.Release();
                    Assert.That(old.IsValid, Is.False);
                    Assert.That(current.IsValid, Is.True);
                }
                finally { current.Release(); }
            }
            finally { old.Release(); Object.DestroyImmediate(asset); }
        }

        [Test]
        public void VectorFeatureAndTint()
        {
            var asset = YauiVectorAsset.FromSvg(Svg("<rect width=\"24\" height=\"24\" fill=\"currentColor\"/>"));
            var handle = YauiVector.Acquire(asset);
            try
            {
                var primitive = YauiPrimitive.Vector(new Rect(0, 0, 48, 48), handle, 0, Color.red);
                Assert.That(ShaderFeaturesExtensions.Of(primitive.Data.Flags), Is.EqualTo(ShaderFeatures.Vector));
                Assert.That(primitive.Data.Color, Is.EqualTo(GpuPacking.Color(Color.red)));
                Assert.That(primitive.Data.BorderColor.x, Is.EqualTo(handle.Layer(0)));
                Assert.That(primitive.WithBorder(2f, Color.green).Data.BorderColor, Is.EqualTo(primitive.Data.BorderColor));
                Assert.That(primitive.WithRadialFill(Vector2.zero, 0f, 1f).Data.Flags, Is.EqualTo(PrimitiveFlags.Vector));
            }
            finally { handle.Release(); Object.DestroyImmediate(asset); }
        }
    }
}
