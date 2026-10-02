using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Yaui.Vector;

namespace Yaui.Tests
{
    public class VectorStrokeTests
    {
        private const string Paint = " fill=\"none\" stroke=\"black\" stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\"";

        private static string Svg(string content, string paint = Paint)
        {
            return "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 24 24\"" + paint + ">" + content + "</svg>";
        }

        [TestCase(LucideSvg.Circle, 64)]
        [TestCase(LucideSvg.Check, 24)]
        [TestCase(LucideSvg.Search, 120)]
        [TestCase(LucideSvg.Settings, 400)]
        [TestCase(LucideSvg.Galaxy, 300)]
        public void LucideCurveBudgets(string svg, int budget)
        {
            var layers = VectorBaker.Bake(svg, out _);
            var count = 0;
            foreach (var layer in layers)
            {
                count += layer.Curves.Count;
                VectorTests.Bands(layer);
            }
            Assert.That(count, Is.LessThanOrEqualTo(budget));
        }

        [TestCase(LucideSvg.Circle)]
        [TestCase(LucideSvg.Check)]
        [TestCase(LucideSvg.Search)]
        [TestCase(LucideSvg.Settings)]
        [TestCase(LucideSvg.Galaxy)]
        [TestCase(LucideSvg.Radio)]
        [TestCase(LucideSvg.Clapperboard)]
        public void LucideCoverage(string svg)
        {
            Compare(svg);
        }

        [TestCase("<path d=\"M3 4C21 2 2 22 21 20\"/>")]
        [TestCase("<path d=\"M3 12Q12 2 21 12Q12 22 3 12Z\"/>")]
        [TestCase("<path d=\"M4 5L20 19L4 19L20 5Z\"/>")]
        [TestCase("<path d=\"M3 12L20 12L4 12.1L19 12.2\"/>")]
        [TestCase("<path d=\"M4 12C22 12 2 12 20 12\"/>")]
        [TestCase("<path d=\"M12 12C20 2 4 2 12 12C20 22 4 22 12 12\"/>")]
        [TestCase("<circle cx=\"12\" cy=\"12\" r=\"0.4\"/>")]
        [TestCase("<path d=\"M4 12L4 12M12 12L12 12M20 12L20 12\"/>")]
        [TestCase("<path d=\"M4 12L4 12L12 12L12 12L20 12\"/>")]
        [TestCase("<g transform=\"translate(2 6) scale(.8 .5)\"><path d=\"M3 4C21 2 2 22 21 20\"/></g>")]
        public void DifficultStrokeCoverage(string content)
        {
            Compare(Svg(content));
        }

        [TestCase("butt", "miter", "4")]
        [TestCase("square", "miter", "1")]
        [TestCase("round", "bevel", "4")]
        public void CapAndJoinCoverage(string cap, string join, string limit)
        {
            Compare(Svg("<path d=\"M4 18V6L18 7L5 8\"/>", " fill=\"none\" stroke=\"black\" stroke-width=\"2\" stroke-linecap=\"" + cap +
                "\" stroke-linejoin=\"" + join + "\" stroke-miterlimit=\"" + limit + "\""));
        }

        [Test]
        public void CubicStrokeCoverage()
        {
            var random = new System.Random(193);
            for (var i = 0; i < 12; i++)
            {
                var coordinates = new string[8];
                for (var j = 0; j < coordinates.Length; j++)
                    coordinates[j] = (2 + 20 * random.NextDouble()).ToString(System.Globalization.CultureInfo.InvariantCulture);
                Compare(Svg("<path d=\"M" + string.Join(" ", coordinates, 0, 2) +
                    "C" + string.Join(" ", coordinates, 2, 6) + "\"/>"));
            }
        }

        [Test]
        public void CubicFillCoverage()
        {
            Compare(Svg("<path d=\"M3 12C1 0 23 0 21 12C23 24 1 24 3 12Z\"/>", " fill=\"black\""));
        }

        [Test]
        public void SmoothSegmentsHaveNoRoundJoins()
        {
            var layers = VectorBaker.Bake(Svg("<path d=\"M2 12C2 6 6 2 12 2C18 2 22 6 22 12\"/>"), out _);
            var arcs = 0;
            foreach (var curve in layers[0].Curves)
                if (curve.Weight != 1f) arcs++;
            Assert.That(arcs, Is.EqualTo(4));
        }

        [Test]
        public void CircleSlugCoverage()
        {
            var curves = new List<VectorCurve>();
            var directions = new[] { Vector2.right, Vector2.up, Vector2.left, Vector2.down };
            for (var i = 0; i < 4; i++)
            {
                var a = directions[i];
                var b = directions[(i + 1) % 4];
                var center = new Vector2(12, 12);
                curves.Add(new VectorCurve(center + a * 11, center + (a + b) * 11, center + b * 11, Mathf.Sqrt(0.5f)));
                curves.Add(new VectorCurve(center + b * 9, center + (a + b) * 9, center + a * 9, Mathf.Sqrt(0.5f)));
            }
            var exact = new VectorLayer(curves, Color.black, false, false);
            var actual = VectorBaker.Bake(LucideSvg.Circle, out _)[0];
            var max = 0f;
            for (var y = 0; y < 384; y++)
            for (var x = 0; x < 384; x++)
            {
                var point = new Vector2((x + 0.37f) / 16f, (y + 0.61f) / 16f);
                max = Mathf.Max(max, Mathf.Abs(SlugReference.Coverage(actual, point, Vector2.one * 4) -
                    SlugReference.Coverage(exact, point, Vector2.one * 4)));
            }
            Console.WriteLine("Maximum analytic-circle Slug coverage error: " + max);
            Assert.That(max, Is.LessThan(0.04f));
        }

        internal static float Compare(string svg)
        {
            var actual = VectorBaker.Bake(svg, out _);
            var reference = VectorStrokeReference.Bake(svg, out _);
            var a = Raster(actual);
            var b = Raster(reference);
            var max = 0f;
            var worst = 0;
            for (var i = 0; i < a.Length; i++)
            {
                var error = Mathf.Abs(a[i] - b[i]);
                if (error > max) { max = error; worst = i; }
            }
            Console.WriteLine("Maximum 96px coverage error: " + max);
            Assert.That(max, Is.LessThan(0.025f), "pixel " + worst % 96 + ", " + worst / 96);
            return max;
        }

        // Integrate the nonzero union before filtering. Filtering overlapping quads first
        // saturates their shared edges and is not a reference for silhouette coverage.
        internal static float[] Raster(VectorLayer[] layers)
        {
            const int size = 96;
            const int samples = 64;
            var result = new float[size * size];
            foreach (var layer in layers)
            {
                var coverage = new float[result.Length];
                var events = new List<(double X, int Winding)>();
                for (var row = 0; row < size * samples; row++)
                {
                    var y = ((row + 0.5) / (4 * samples) - layer.Bounds.yMin) / layer.Bounds.height;
                    events.Clear();
                    foreach (var curve in layer.Curves)
                    {
                        var p = (double)curve.P0.y - y;
                        var q = (double)curve.P1.y - y;
                        var r = (double)curve.P2.y - y;
                        var a = p - 2 * curve.Weight * q + r;
                        var b = 2 * (curve.Weight * q - p);
                        if (Math.Abs(a) < 1e-14)
                        {
                            if (Math.Abs(b) > 1e-14) Crossing(-p / b, b, curve, layer, events);
                        }
                        else
                        {
                            var d = b * b - 4 * a * p;
                            if (d <= 0) continue;
                            var root = Math.Sqrt(d);
                            Crossing((-b - root) / (2 * a), -root, curve, layer, events);
                            Crossing((-b + root) / (2 * a), root, curve, layer, events);
                        }
                    }
                    events.Sort((x, z) => x.X.CompareTo(z.X));
                    var winding = 0;
                    var start = 0.0;
                    foreach (var e in events)
                    {
                        var wasInside = layer.EvenOdd ? (winding & 1) != 0 : winding != 0;
                        winding += e.Winding;
                        var inside = layer.EvenOdd ? (winding & 1) != 0 : winding != 0;
                        if (!wasInside && inside) start = e.X;
                        if (!wasInside || inside) continue;
                        var low = Math.Max(0, start);
                        var high = Math.Min(size, e.X);
                        for (var x = (int)Math.Floor(low); x < Math.Ceiling(high); x++)
                        {
                            if (x < 0 || x >= size) continue;
                            coverage[(row / samples) * size + x] +=
                                (float)Math.Max(0, Math.Min(high, x + 1) - Math.Max(low, x)) / samples;
                        }
                    }
                }
                for (var i = 0; i < result.Length; i++) result[i] = Mathf.Max(result[i], coverage[i]);
            }
            return result;
        }

        private static void Crossing(double t, double derivative, VectorCurve curve, VectorLayer layer,
            List<(double X, int Winding)> events)
        {
            if (t < 0 || t >= 1) return;
            var u = 1 - t;
            var x = (u * u * curve.P0.x + 2 * curve.Weight * u * t * curve.P1.x + t * t * curve.P2.x) /
                    (u * u + 2 * curve.Weight * u * t + t * t);
            events.Add(((x * layer.Bounds.width + layer.Bounds.xMin) * 4, derivative > 0 ? 1 : -1));
        }
    }
}
