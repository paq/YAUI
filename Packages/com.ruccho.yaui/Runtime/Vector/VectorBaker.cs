using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Xml;
using Unity.VectorGraphics;
using UnityEngine;

namespace Yaui.Vector
{
    internal static class VectorBaker
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
            var tolerance = Mathf.Max(viewBox.width, viewBox.height) * 0.0005f;
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
                    transform * segments[(i + 1) % segments.Length].P0, tolerance, 0, curves);
            }

            if (!contour.Closed) Line(transform * segments[^1].P0, transform * segments[0].P0, curves);
        }

        private static void Cubic(Vector2 a, Vector2 b, Vector2 c, Vector2 d, float tolerance, int depth,
            List<VectorCurve> curves)
        {
            var control = (3f * (b + c) - a - d) * 0.25f;
            var error = Mathf.Max((b - (a + 2f * control) / 3f).magnitude,
                (c - (d + 2f * control) / 3f).magnitude);
            if (depth >= 18 || error <= tolerance)
            {
                curves.Add(new VectorCurve(a, control, d));
                return;
            }

            var ab = (a + b) * 0.5f;
            var bc = (b + c) * 0.5f;
            var cd = (c + d) * 0.5f;
            var abc = (ab + bc) * 0.5f;
            var bcd = (bc + cd) * 0.5f;
            var middle = (abc + bcd) * 0.5f;
            Cubic(a, ab, abc, middle, tolerance, depth + 1, curves);
            Cubic(middle, bcd, cd, d, tolerance, depth + 1, curves);
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

        private readonly struct StrokeSegment
        {
            internal readonly Vector2 A, B, C, D;

            internal StrokeSegment(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
            {
                A = a; B = b; C = c; D = d;
            }

            internal Vector2 Point(float t)
            {
                var u = 1f - t;
                return u * u * u * A + 3f * u * t * (u * B + t * C) + t * t * t * D;
            }

            internal Vector2 Tangent(float t)
            {
                var u = 1f - t;
                var tangent = u * u * (B - A) + 2f * u * t * (C - B) + t * t * (D - C);
                if (tangent.sqrMagnitude > 1e-16f) return tangent / tangent.magnitude;
                tangent = t < 0.5f ? C - A : D - B;
                tangent = tangent.sqrMagnitude > 1e-16f ? tangent : D - A;
                return tangent.sqrMagnitude > 0f ? tangent / tangent.magnitude : Vector2.zero;
            }

            internal Vector2 Offset(float t, float radius)
            {
                var tangent = Tangent(t);
                return Point(t) + new Vector2(-tangent.y, tangent.x) * radius;
            }
        }

        private static void Offset(StrokeSegment segment, float radius, float start, float end,
            float tolerance, int depth, List<VectorCurve> curves)
        {
            var middle = (start + end) * 0.5f;
            var a = segment.Offset(start, radius);
            var b = segment.Offset(middle, radius);
            var c = segment.Offset(end, radius);
            var control = 2f * b - (a + c) * 0.5f;
            var error = 0f;
            for (var i = 1; i < 8; i++)
            {
                var t = i / 8f;
                var u = 1f - t;
                var p = u * u * a + 2f * u * t * control + t * t * c;
                error = Mathf.Max(error, (p - segment.Offset(start + (end - start) * t, radius)).magnitude);
            }

            if (depth >= 18 || (error <= tolerance && StableTangents(Interval(segment, start, end))))
            {
                if ((a - control).sqrMagnitude + (c - control).sqrMagnitude > 1e-16f)
                    curves.Add(new VectorCurve(a, control, c));
                return;
            }

            Offset(segment, radius, start, middle, tolerance, depth + 1, curves);
            Offset(segment, radius, middle, end, tolerance, depth + 1, curves);
        }

        private static bool StableTangents(StrokeSegment segment)
        {
            var direction = segment.Tangent(0.5f);
            var a = segment.B - segment.A;
            var b = segment.C - segment.B;
            var c = segment.D - segment.C;
            return Vector2.Dot(a, direction) >= a.magnitude * 0.5f &&
                   Vector2.Dot(b, direction) >= b.magnitude * 0.5f &&
                   Vector2.Dot(c, direction) >= c.magnitude * 0.5f;
        }

        private static StrokeSegment Interval(StrokeSegment segment, float start, float end)
        {
            Split(segment, end, out var prefix, out _);
            Split(prefix, start / end, out _, out var interval);
            return interval;
        }

        private static bool Regular(StrokeSegment segment, float radius)
        {
            var a = 3f * (segment.B - segment.A);
            var b = 3f * (segment.C - segment.B);
            var c = 3f * (segment.D - segment.C);
            var ab = 2f * (b - a);
            var bc = 2f * (c - b);
            var cross = Mathf.Max(Mathf.Abs(Cross(a, ab)), Mathf.Abs(Cross(a, bc)));
            cross = Mathf.Max(cross, Mathf.Max(Mathf.Abs(Cross(b, ab)), Mathf.Abs(Cross(b, bc))));
            cross = Mathf.Max(cross, Mathf.Max(Mathf.Abs(Cross(c, ab)), Mathf.Abs(Cross(c, bc))));
            if (cross == 0f) return true;
            var side0 = Cross(b - a, -a);
            var side1 = Cross(c - b, -b);
            var side2 = Cross(a - c, -c);
            if ((side0 >= 0f && side1 >= 0f && side2 >= 0f) ||
                (side0 <= 0f && side1 <= 0f && side2 <= 0f)) return false;
            var speed = Mathf.Min(Distance(Vector2.zero, a, b),
                Mathf.Min(Distance(Vector2.zero, b, c), Distance(Vector2.zero, c, a)));
            return radius * cross <= speed * speed * speed;
        }

        private static Vector2 Evolute(StrokeSegment segment, float t, out float curvature)
        {
            var u = 1f - t;
            var velocity = 3f * (u * u * (segment.B - segment.A) +
                2f * u * t * (segment.C - segment.B) + t * t * (segment.D - segment.C));
            var acceleration = 6f * (u * (segment.C - 2f * segment.B + segment.A) +
                t * (segment.D - 2f * segment.C + segment.B));
            var cross = Cross(velocity, acceleration);
            var speedSquared = velocity.sqrMagnitude;
            curvature = speedSquared > 1e-16f ? cross / (speedSquared * Mathf.Sqrt(speedSquared)) : 0f;
            return Mathf.Abs(cross) > 1e-16f
                ? segment.Point(t) + new Vector2(-velocity.y, velocity.x) * (speedSquared / cross)
                : segment.Point(t);
        }

        private static void Fold(StrokeSegment segment, float radius, float start, float end,
            float tolerance, int depth, List<VectorCurve> curves)
        {
            var middle = (start + end) * 0.5f;
            if (Regular(Interval(segment, start, end), radius)) return;
            var ea = Evolute(segment, start, out var ka);
            var eb = Evolute(segment, middle, out var kb);
            var ec = Evolute(segment, end, out var kc);
            var fa = Mathf.Abs(ka) * radius > 1f;
            var fb = Mathf.Abs(kb) * radius > 1f;
            var fc = Mathf.Abs(kc) * radius > 1f;
            if (depth >= 18) return;
            if (!fa || !fb || !fc || ka * kb <= 0f || kb * kc <= 0f)
            {
                Fold(segment, radius, start, middle, tolerance, depth + 1, curves);
                Fold(segment, radius, middle, end, tolerance, depth + 1, curves);
                return;
            }

            var side = kb > 0f ? radius : -radius;
            var a = segment.Offset(start, side);
            var b = segment.Offset(middle, side);
            var c = segment.Offset(end, side);
            if (Mathf.Max((a - ea).magnitude, Mathf.Max((b - eb).magnitude, (c - ec).magnitude)) < tolerance)
                return;
            var control = 2f * b - (a + c) * 0.5f;
            var eControl = 2f * eb - (ea + ec) * 0.5f;
            var error = 0f;
            for (var i = 1; i < 8; i++)
            {
                var t = i / 8f;
                var u = 1f - t;
                var parameter = start + (end - start) * t;
                var e = Evolute(segment, parameter, out var k);
                if (k * side <= 1f) { error = float.PositiveInfinity; break; }
                error = Mathf.Max(error, (e - (u * u * ea + 2f * u * t * eControl + t * t * ec)).magnitude);
                error = Mathf.Max(error, (segment.Offset(parameter, side) -
                    (u * u * a + 2f * u * t * control + t * t * c)).magnitude);
            }
            if (error > tolerance)
            {
                Fold(segment, radius, start, middle, tolerance, depth + 1, curves);
                Fold(segment, radius, middle, end, tolerance, depth + 1, curves);
                return;
            }

            // Across the evolute the normal sweep reverses orientation. Twice its reversed
            // boundary changes the folded strip's winding from negative to positive.
            for (var i = 0; i < 2; i++)
            {
                if (side > 0f)
                {
                    curves.Add(new VectorCurve(a, control, c));
                    FoldEdge(c, ec, curves);
                    curves.Add(new VectorCurve(ec, eControl, ea));
                    FoldEdge(ea, a, curves);
                }
                else
                {
                    curves.Add(new VectorCurve(c, control, a));
                    FoldEdge(a, ea, curves);
                    curves.Add(new VectorCurve(ea, eControl, ec));
                    FoldEdge(ec, c, curves);
                }
            }
        }

        private static void FoldEdge(Vector2 a, Vector2 b, List<VectorCurve> curves)
        {
            var middle = (a + b) * 0.5f;
            for (var i = curves.Count - 1; i >= 0; i--)
            {
                var c = curves[i];
                if (c.Weight != 1f || !c.P0.Equals(b) || !c.P2.Equals(a) || !c.P1.Equals(middle)) continue;
                curves.RemoveAt(i);
                return;
            }
            Line(a, b, curves);
        }

        private static void Arc(Vector2 center, Vector2 a, Vector2 b, float angle, List<VectorCurve> curves)
        {
            var count = Mathf.CeilToInt(Mathf.Abs(angle) / (Mathf.PI * 0.5f));
            var step = angle / count;
            var weight = Mathf.Cos(step * 0.5f);
            var start = a - center;
            for (var i = 0; i < count; i++)
            {
                var rotation = step * (i + 1);
                var end = i == count - 1 ? b - center : new Vector2(
                    start.x * Mathf.Cos(rotation) - start.y * Mathf.Sin(rotation),
                    start.x * Mathf.Sin(rotation) + start.y * Mathf.Cos(rotation));
                var previous = a - center;
                var control = center + (previous + end) / (2f * weight * weight);
                curves.Add(new VectorCurve(a, control, center + end, weight));
                a = center + end;
            }
        }

        private static void Join(Vector2 center, Vector2 previous, Vector2 next, float radius,
            PathProperties props, List<VectorCurve> curves)
        {
            var a = center + new Vector2(-previous.y, previous.x) * radius;
            var b = center + new Vector2(-next.y, next.x) * radius;
            var turn = Cross(previous, next);
            var dot = Vector2.Dot(previous, next);
            if (Mathf.Abs(turn) < 1e-5f && dot > 0f)
            {
                Line(a, b, curves);
                return;
            }

            // Returning through the center keeps inner folds additive even at near reversals.
            if (turn * radius > 0f)
            {
                Line(a, center, curves);
                Line(center, b, curves);
            }
            else if (props.Corners == PathCorner.Round)
            {
                var angle = Mathf.Atan2(turn, dot);
                if (Mathf.Abs(turn) < 1e-6f && dot < 0f) angle = radius > 0f ? -Mathf.PI : Mathf.PI;
                Arc(center, a, b, angle, curves);
            }
            else
            {
                if (props.Corners == PathCorner.Tipped && Mathf.Abs(turn) > 1e-6f)
                {
                    var miter = a + previous * (Cross(b - a, next) / turn);
                    if ((miter - center).magnitude <= Mathf.Abs(radius) * Mathf.Max(1f, props.Stroke.TippedCornerLimit))
                    {
                        Line(a, miter, curves);
                        Line(miter, b, curves);
                        return;
                    }
                }
                Line(a, b, curves);
            }
        }

        private static void Cap(Vector2 center, Vector2 direction, float radius, PathEnding ending,
            List<VectorCurve> curves)
        {
            var normal = new Vector2(-direction.y, direction.x) * radius;
            var a = center - normal;
            var b = center + normal;
            if (ending == PathEnding.Round) Arc(center, a, b, Mathf.PI, curves);
            else if (ending == PathEnding.Square)
            {
                Line(a, a + direction * radius, curves);
                Line(a + direction * radius, b + direction * radius, curves);
                Line(b + direction * radius, b, curves);
            }
            else Line(a, b, curves);
        }

        private static void Reverse(List<VectorCurve> source, List<VectorCurve> destination)
        {
            for (var i = source.Count - 1; i >= 0; i--)
            {
                var c = source[i];
                destination.Add(new VectorCurve(c.P2, c.P1, c.P0, c.Weight));
            }
        }

        private static void Split(StrokeSegment s, float t, out StrokeSegment left, out StrokeSegment right)
        {
            var ab = Vector2.LerpUnclamped(s.A, s.B, t);
            var bc = Vector2.LerpUnclamped(s.B, s.C, t);
            var cd = Vector2.LerpUnclamped(s.C, s.D, t);
            var abc = Vector2.LerpUnclamped(ab, bc, t);
            var bcd = Vector2.LerpUnclamped(bc, cd, t);
            var middle = Vector2.LerpUnclamped(abc, bcd, t);
            left = new StrokeSegment(s.A, ab, abc, middle);
            right = new StrokeSegment(middle, bcd, cd, s.D);
        }

        private static void Stationary(StrokeSegment s, List<StrokeSegment> path)
        {
            var a = -s.A + 3f * s.B - 3f * s.C + s.D;
            var b = 2f * (s.A - 2f * s.B + s.C);
            var c = s.B - s.A;
            var axis = Mathf.Abs(a.x) + Mathf.Abs(b.x) + Mathf.Abs(c.x) >=
                       Mathf.Abs(a.y) + Mathf.Abs(b.y) + Mathf.Abs(c.y) ? 0 : 1;
            var roots = new List<float>();
            if (Mathf.Abs(a[axis]) < 1e-12f)
            {
                if (Mathf.Abs(b[axis]) > 1e-12f) roots.Add(-c[axis] / b[axis]);
            }
            else
            {
                var discriminant = b[axis] * b[axis] - 4f * a[axis] * c[axis];
                if (discriminant >= 0f)
                {
                    var q = -0.5f * (b[axis] + (b[axis] >= 0f ? 1f : -1f) * Mathf.Sqrt(discriminant));
                    roots.Add(q / a[axis]);
                    if (Mathf.Abs(q) > 1e-12f) roots.Add(c[axis] / q);
                }
            }
            roots.Sort();
            var previous = 0f;
            var remaining = s;
            foreach (var t in roots)
            {
                if (t <= previous + 1e-6f || t >= 1f - 1e-6f ||
                    (a * (t * t) + b * t + c).sqrMagnitude >
                    1e-12f * Mathf.Max(1f, a.sqrMagnitude + b.sqrMagnitude + c.sqrMagnitude)) continue;
                Split(remaining, (t - previous) / (1f - previous), out var left, out remaining);
                left = new StrokeSegment(left.A, left.B, left.D, left.D);
                remaining = new StrokeSegment(remaining.A, remaining.A, remaining.C, remaining.D);
                path.Add(left);
                previous = t;
            }
            path.Add(remaining);
        }

        private static void Stroke(BezierContour contour, PathProperties props, float tolerance,
            List<VectorCurve> curves)
        {
            var segments = contour.Segments;
            if (segments == null || segments.Length == 0) return;
            var path = new List<StrokeSegment>();
            var count = contour.Closed ? segments.Length : segments.Length - 1;
            for (var i = 0; i < count; i++)
            {
                var s = segments[i];
                var d = segments[(i + 1) % segments.Length].P0;
                if ((s.P1 - s.P0).sqrMagnitude + (s.P2 - s.P0).sqrMagnitude + (d - s.P0).sqrMagnitude > 1e-16f)
                    Stationary(new StrokeSegment(s.P0, s.P1, s.P2, d), path);
            }

            var radius = props.Stroke.HalfThickness;
            if (path.Count == 0)
            {
                if (props.Head == PathEnding.Round || props.Tail == PathEnding.Round) Disc(segments[0].P0, radius, curves);
                return;
            }

            tolerance = Mathf.Min(tolerance, radius * 0.005f);
            var left = new List<VectorCurve>();
            var right = new List<VectorCurve>();
            for (var i = 0; i < path.Count; i++)
            {
                var s = path[i];
                if (i > 0 || contour.Closed)
                {
                    var previous = path[(i + path.Count - 1) % path.Count].Tangent(1f);
                    var next = s.Tangent(0f);
                    Join(s.A, previous, next, radius, props, left);
                    Join(s.A, previous, next, -radius, props, right);
                }
                Fold(s, radius, 0f, 1f, tolerance, 0, curves);
                Offset(s, radius, 0f, 1f, tolerance * 0.5f, 0, left);
                Offset(s, -radius, 0f, 1f, tolerance * 0.5f, 0, right);
            }

            curves.AddRange(right);
            if (!contour.Closed) Cap(path[^1].D, path[^1].Tangent(1f), radius, props.Tail, curves);
            Reverse(left, curves);
            if (!contour.Closed) Cap(path[0].A, -path[0].Tangent(0f), radius, props.Head, curves);
        }
    }
}
