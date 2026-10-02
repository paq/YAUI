using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Xml;
using Unity.VectorGraphics;
using UnityEngine;

namespace Yaui.Tests
{
    // Frozen cc613cf baker with tenfold finer flattening for independent coverage comparisons.
    internal static class VectorStrokeReference
    {
        private static readonly Vector2[] Directions = { Vector2.right, Vector2.up, Vector2.left, Vector2.down };
        internal static VectorLayer[] Bake(string svg, out Rect viewBox)
        {
            var document = new XmlDocument { XmlResolver = null };
            using (var reader = XmlReader.Create(new StringReader(svg), new XmlReaderSettings
                   { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }))
                document.Load(reader);

            var root = document.DocumentElement;
            if (root == null || root.LocalName != "svg") throw new FormatException("The document root must be svg.");
            var values = Regex.Split(root.GetAttribute("viewBox").Trim(), @"[\s,]+");
            var hasViewBox = values.Length == 4;
            viewBox = hasViewBox
                ? new Rect(Number(values[0]), Number(values[1]), Number(values[2]), Number(values[3]))
                : default;
            if (hasViewBox)
            {
                if (viewBox.width <= 0f || viewBox.height <= 0f) throw new FormatException("The viewBox must have positive dimensions.");
                root.SetAttribute("width", viewBox.width.ToString(CultureInfo.InvariantCulture));
                root.SetAttribute("height", viewBox.height.ToString(CultureInfo.InvariantCulture));
                root.RemoveAttribute("viewBox");
            }

            var first = Parse(document.OuterXml, "#ea17b9");
            var second = Parse(document.OuterXml, "#16d83a");
            if (!hasViewBox) viewBox = first.SceneViewport;
            if (viewBox.width <= 0f || viewBox.height <= 0f) throw new FormatException("The SVG must have a positive viewport.");
            var tolerance = Mathf.Max(viewBox.width, viewBox.height) * 0.00001f;
            var layers = new List<VectorLayer>();
            Visit(first.Scene.Root, second.Scene.Root, Matrix2D.identity, 1f,
                first.NodeOpacity, tolerance, layers);
            return layers.ToArray();
        }

        private static float Number(string value)
        {
            var result = float.Parse(value, CultureInfo.InvariantCulture);
            if (float.IsNaN(result) || float.IsInfinity(result)) throw new FormatException("SVG dimensions must be finite.");
            return result;
        }

        private static SVGParser.SceneInfo Parse(string svg, string marker)
        {
            return SVGParser.ImportSVG(new StringReader(Regex.Replace(svg, @"\bcurrentColor\b", marker,
                RegexOptions.IgnoreCase)), 96f, 1f, 0, 0, false);
        }

        private static void Visit(SceneNode node, SceneNode alternate, Matrix2D parent, float opacity,
            Dictionary<SceneNode, float> opacities, float tolerance, List<VectorLayer> layers)
        {
            var transform = parent * node.Transform;
            if (opacities != null && opacities.TryGetValue(node, out var value)) opacity *= value;
            if (node.Clipper != null) Debug.LogWarning("YAUI SVG: SVG clip paths are not supported.");
            if (node.Shapes != null)
            for (var i = 0; i < node.Shapes.Count; i++)
            {
                var shape = node.Shapes[i];
                var other = alternate.Shapes[i];
                if (shape.Fill != null)
                {
                    var curves = new List<VectorCurve>();
                    foreach (var contour in shape.Contours) Fill(contour, transform, tolerance, curves);
                    Add(curves, shape.Fill, other.Fill, opacity, shape.Fill.Mode == FillMode.OddEven, layers);
                }

                var stroke = shape.PathProps.Stroke;
                if (stroke == null || stroke.HalfThickness <= 0f) continue;
                if (stroke.Pattern != null && stroke.Pattern.Length > 0)
                    Debug.LogWarning("YAUI SVG: dashed strokes are rendered as continuous strokes.");
                var expanded = new List<VectorCurve>();
                var x = transform.MultiplyVector(Vector2.right);
                var y = transform.MultiplyVector(Vector2.up);
                var difference = x.sqrMagnitude - y.sqrMagnitude;
                var dot = Vector2.Dot(x, y);
                var scale = Mathf.Sqrt((x.sqrMagnitude + y.sqrMagnitude +
                    Mathf.Sqrt(difference * difference + 4f * dot * dot)) * 0.5f);
                foreach (var contour in shape.Contours)
                    Stroke(contour, shape.PathProps, tolerance / Mathf.Max(scale, 1e-6f), expanded);
                for (var c = 0; c < expanded.Count; c++)
                {
                    var curve = expanded[c];
                    curve.P0 = transform * curve.P0;
                    curve.P1 = transform * curve.P1;
                    curve.P2 = transform * curve.P2;
                    expanded[c] = curve;
                }

                Add(expanded, stroke.Fill, other.PathProps.Stroke.Fill, opacity, false, layers);
            }

            if (node.Children == null) return;
            for (var i = 0; i < node.Children.Count; i++)
                Visit(node.Children[i], alternate.Children[i], transform, opacity, opacities, tolerance, layers);
        }

        private static Color Paint(IFill fill)
        {
            if (fill is SolidFill solid) return solid.Color;
            if (fill is GradientFill gradient && gradient.Stops.Length > 0) return gradient.Stops[0].Color;
            return Color.white;
        }

        private static void Add(List<VectorCurve> curves, IFill fill, IFill alternate, float opacity,
            bool parity, List<VectorLayer> layers)
        {
            if (curves.Count == 0 || fill == null) return;
            if (!(fill is SolidFill))
                Debug.LogWarning("YAUI SVG: gradients and other non-solid paints use a solid approximation.");
            var color = Paint(fill);
            var current = color != Paint(alternate);
            if (current) color = new Color(1f, 1f, 1f, color.a);
            color.a *= opacity * fill.Opacity;
            if (color.a > 0f) layers.Add(new VectorLayer(curves, color, current, parity));
        }

        private static void Fill(BezierContour contour, Matrix2D transform, float tolerance, List<VectorCurve> curves)
        {
            var segments = contour.Segments;
            if (segments == null || segments.Length < 2) return;
            var count = contour.Closed ? segments.Length : segments.Length - 1;
            for (var i = 0; i < count; i++)
            {
                var s = segments[i];
                Cubic(transform * s.P0, transform * s.P1, transform * s.P2,
                    transform * segments[(i + 1) % segments.Length].P0, tolerance, 0, curves, null);
            }

            if (!contour.Closed) Line(transform * segments[^1].P0, transform * segments[0].P0, curves);
        }

        private static void Cubic(Vector2 a, Vector2 b, Vector2 c, Vector2 d, float tolerance, int depth,
            List<VectorCurve> curves, List<Vector2> points)
        {
            var control = (3f * (b + c) - a - d) * 0.25f;
            var error = Mathf.Max((b - (a + 2f * control) / 3f).magnitude,
                (c - (d + 2f * control) / 3f).magnitude);
            var flat = Mathf.Max(Distance(b, a, d), Distance(c, a, d));
            if (depth >= 18 || (points == null ? error : flat) <= tolerance)
            {
                if (points != null) points.Add(d);
                else curves.Add(new VectorCurve(a, control, d));
                return;
            }

            var ab = (a + b) * 0.5f;
            var bc = (b + c) * 0.5f;
            var cd = (c + d) * 0.5f;
            var abc = (ab + bc) * 0.5f;
            var bcd = (bc + cd) * 0.5f;
            var middle = (abc + bcd) * 0.5f;
            Cubic(a, ab, abc, middle, tolerance, depth + 1, curves, points);
            Cubic(middle, bcd, cd, d, tolerance, depth + 1, curves, points);
        }

        private static float Distance(Vector2 p, Vector2 a, Vector2 b)
        {
            var delta = b - a;
            var t = delta.sqrMagnitude > 0f ? Mathf.Clamp01(Vector2.Dot(p - a, delta) / delta.sqrMagnitude) : 0f;
            return (p - a - delta * t).magnitude;
        }

        private static void Line(Vector2 a, Vector2 b, List<VectorCurve> curves)
        {
            if ((a - b).sqrMagnitude > 1e-16f) curves.Add(new VectorCurve(a, (a + b) * 0.5f, b));
        }

        private static float Cross(Vector2 a, Vector2 b)
        {
            return a.x * b.y - a.y * b.x;
        }

        private static void Polygon(List<VectorCurve> curves, params Vector2[] points)
        {
            var area = 0f;
            for (var i = 0; i < points.Length; i++) area += Cross(points[i], points[(i + 1) % points.Length]);
            if (area < 0f) Array.Reverse(points);
            for (var i = 0; i < points.Length; i++) Line(points[i], points[(i + 1) % points.Length], curves);
        }

        private static void Disc(Vector2 center, float radius, List<VectorCurve> curves)
        {
            for (var i = 0; i < 4; i++)
            {
                var a = Directions[i];
                var b = Directions[(i + 1) % 4];
                curves.Add(new VectorCurve(center + a * radius, center + (a + b) * radius,
                    center + b * radius, Mathf.Sqrt(0.5f)));
            }
        }

        private static void Stroke(BezierContour contour, PathProperties props, float tolerance,
            List<VectorCurve> curves)
        {
            var segments = contour.Segments;
            if (segments == null || segments.Length == 0) return;
            var points = new List<Vector2> { segments[0].P0 };
            var count = contour.Closed ? segments.Length : segments.Length - 1;
            for (var i = 0; i < count; i++)
            {
                var s = segments[i];
                Cubic(s.P0, s.P1, s.P2, segments[(i + 1) % segments.Length].P0, tolerance, 0, null, points);
            }

            for (var i = points.Count - 1; i > 0; i--)
                if ((points[i] - points[i - 1]).sqrMagnitude < 1e-12f) points.RemoveAt(i);
            if (contour.Closed && points.Count > 1 && (points[^1] - points[0]).sqrMagnitude < 1e-12f)
                points.RemoveAt(points.Count - 1);
            for (var i = points.Count - 2; i > 0; i--)
            {
                var previous = (points[i] - points[i - 1]).normalized;
                var next = (points[i + 1] - points[i]).normalized;
                if (Vector2.Dot(previous, next) > 0f && Mathf.Abs(Cross(previous, next)) < 1e-4f &&
                    Distance(points[i], points[i - 1], points[i + 1]) <= tolerance * 0.125f)
                    points.RemoveAt(i);
            }

            var radius = props.Stroke.HalfThickness;
            if (points.Count < 2)
            {
                if (props.Head == PathEnding.Round || props.Tail == PathEnding.Round) Disc(points[0], radius, curves);
                return;
            }

            var edgeCount = contour.Closed ? points.Count : points.Count - 1;
            var directions = new Vector2[edgeCount];
            for (var i = 0; i < edgeCount; i++)
            {
                var a = points[i];
                var b = points[(i + 1) % points.Count];
                var direction = (b - a).normalized;
                directions[i] = direction;
                var normal = new Vector2(-direction.y, direction.x) * radius;
                if (!contour.Closed && i == 0 && props.Head == PathEnding.Square) a -= direction * radius;
                if (!contour.Closed && i == edgeCount - 1 && props.Tail == PathEnding.Square) b += direction * radius;
                Polygon(curves, a + normal, a - normal, b - normal, b + normal);
            }

            for (var i = contour.Closed ? 0 : 1; i < (contour.Closed ? points.Count : points.Count - 1); i++)
            {
                var previous = directions[(i + edgeCount - 1) % edgeCount];
                var next = directions[i % edgeCount];
                var turn = Cross(previous, next);
                if (Mathf.Abs(turn) < 1e-4f && Vector2.Dot(previous, next) > 0f) continue;
                var center = points[i];
                if (props.Corners == PathCorner.Round)
                {
                    Disc(center, radius, curves);
                    continue;
                }

                var side = turn > 0f ? -1f : 1f;
                var a = center + new Vector2(-previous.y, previous.x) * (radius * side);
                var b = center + new Vector2(-next.y, next.x) * (radius * side);
                if (props.Corners == PathCorner.Tipped && Mathf.Abs(turn) > 1e-6f)
                {
                    var miter = a + previous * (Cross(b - a, next) / turn);
                    if ((miter - center).magnitude <= radius * Mathf.Max(1f, props.Stroke.TippedCornerLimit))
                    {
                        Polygon(curves, center, a, miter, b);
                        continue;
                    }
                }

                Polygon(curves, center, a, b);
            }

            if (contour.Closed) return;
            if (props.Head == PathEnding.Round) Disc(points[0], radius, curves);
            if (props.Tail == PathEnding.Round) Disc(points[^1], radius, curves);
        }
    }
}
