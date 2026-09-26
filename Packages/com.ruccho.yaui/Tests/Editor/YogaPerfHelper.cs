using System.Collections.Generic;
using System.Diagnostics;
using Yaui.Layout.Yoga;
using Ref = Yaui.Tests.YogaReference;

namespace Yaui.Tests
{
    /// <summary>
    /// Times layouts of the benchmark grid shape (1500 fixed-size cells of an icon and a measured label in a
    /// wrapping row) with the Burst port and the managed reference port. Called from the editor while profiling.
    /// </summary>
    public static class YogaPerfHelper
    {
        private const int Cells = 1500;

        private static YogaSize MeasureLabel(float width)
        {
            return new YogaSize(System.Math.Min(40f, float.IsNaN(width) ? 40f : width), 20f);
        }

        public static string Run(string mode)
        {
            var watch = new Stopwatch();
            double full = 0, spawn = 0, measures = 0;
            var measureCount = 0;
            var fresh = 0;
            if (mode == "Burst")
            {
                var root = YogaNodeStore.Create();
                root.FlexDirection = FlexDirection.Row;
                root.FlexWrap = FlexWrap.Wrap;
                root.Width = YogaValue.Point(1080);
                root.Height = YogaValue.Point(1920);
                var all = new List<YogaNode> { root };

                YogaNode Cell()
                {
                    var cell = YogaNodeStore.Create();
                    cell.Width = YogaValue.Point(30);
                    cell.Height = YogaValue.Point(40);
                    cell.FlexDirection = FlexDirection.Row;
                    var icon = YogaNodeStore.Create();
                    icon.Width = YogaValue.Point(10);
                    icon.Height = YogaValue.Point(28);
                    var label = YogaNodeStore.Create();
                    label.FlexGrow = 1;
                    label.SetMeasureFunction((_, w, wm, h, hm) =>
                    {
                        measureCount++;
                        return MeasureLabel(w);
                    });
                    cell.InsertChild(icon, 0);
                    cell.InsertChild(label, 1);
                    all.Add(cell);
                    all.Add(icon);
                    all.Add(label);
                    return cell;
                }

                for (var i = 0; i < Cells; i++) root.InsertChild(Cell(), i);
                root.CalculateLayout(1080, 1920); // warm up (Burst compilation)
                root.Width = YogaValue.Point(1000);
                watch.Restart();
                root.CalculateLayout(1080, 1920);
                full = watch.Elapsed.TotalMilliseconds;
                for (var frame = 0; frame < 10; frame++)
                {
                    for (var i = 0; i < 100; i++)
                    {
                        var index = (frame * 100 + i) % Cells;
                        root.RemoveChild(index);
                        root.InsertChild(Cell(), index);
                    }

                    measureCount = 0;
                    watch.Restart();
                    root.CalculateLayout(1080, 1920);
                    spawn += watch.Elapsed.TotalMilliseconds;
                    measures += measureCount;
                    fresh = 0;
                    foreach (var node in all)
                        if (!node.Owner.IsNull || node == root)
                        {
                            if (node.HasNewLayout) fresh++;
                            node.HasNewLayout = false;
                        }
                }

                foreach (var node in all) YogaNodeStore.Destroy(node);
            }
            else
            {
                var root = new Ref.YogaNode();
                root.FlexDirection = FlexDirection.Row;
                root.FlexWrap = FlexWrap.Wrap;
                root.Width = Ref.YogaValue.Point(1080);
                root.Height = Ref.YogaValue.Point(1920);

                Ref.YogaNode Cell()
                {
                    var cell = new Ref.YogaNode();
                    cell.Width = Ref.YogaValue.Point(30);
                    cell.Height = Ref.YogaValue.Point(40);
                    cell.FlexDirection = FlexDirection.Row;
                    var icon = new Ref.YogaNode();
                    icon.Width = Ref.YogaValue.Point(10);
                    icon.Height = Ref.YogaValue.Point(28);
                    var label = new Ref.YogaNode();
                    label.FlexGrow = 1;
                    label.MeasureFunction = (_, w, wm, h, hm) =>
                    {
                        measureCount++;
                        var s = MeasureLabel(w);
                        return new Ref.YogaSize(s.Width, s.Height);
                    };
                    cell.InsertChild(icon, 0);
                    cell.InsertChild(label, 1);
                    return cell;
                }

                for (var i = 0; i < Cells; i++) root.InsertChild(Cell(), i);
                root.CalculateLayout(1080, 1920);
                root.Width = Ref.YogaValue.Point(1000);
                watch.Restart();
                root.CalculateLayout(1080, 1920);
                full = watch.Elapsed.TotalMilliseconds;
                for (var frame = 0; frame < 10; frame++)
                {
                    for (var i = 0; i < 100; i++)
                    {
                        var index = (frame * 100 + i) % Cells;
                        root.RemoveChild(index);
                        root.InsertChild(Cell(), index);
                    }

                    measureCount = 0;
                    watch.Restart();
                    root.CalculateLayout(1080, 1920);
                    spawn += watch.Elapsed.TotalMilliseconds;
                    measures += measureCount;
                    fresh = 0;

                    void Walk(Ref.YogaNode n)
                    {
                        if (n.HasNewLayout) fresh++;
                        n.HasNewLayout = false;
                        for (var c = 0; c < n.ChildCount; c++) Walk(n.GetChild(c));
                    }

                    Walk(root);
                }
            }

            return
                $"full relayout {full:F2} ms, spawn relayout {spawn / 10:F2} ms ({measures / 10:F0} measures, {fresh} new layouts)";
        }
    }
}