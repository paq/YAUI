using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using Yaui.Core;

namespace Yaui.Editor
{
    /// <summary>
    /// Selects elements by clicking them in the Scene view. Overlay panels are placed like uGUI's overlay canvases
    /// (<see cref="YauiPanel.SceneViewCanvasToWorld"/>), world space panels where they are.
    /// </summary>
    [InitializeOnLoad]
    internal static class YauiSceneViewPicking
    {
        private static readonly List<(YauiElement Element, int Depth)> Hits = new();

        static YauiSceneViewPicking()
        {
            HandleUtility.pickGameObjectCustomPasses += Pick;
        }

        private static GameObject Pick(Camera camera, int layers, Vector2 position, GameObject[] ignore,
            GameObject[] filter,
            out int materialIndex)
        {
            materialIndex = -1;
            if (!YauiSystem.IsInitialized) return null;

            // Unity passes the position in the camera's pixels (bottom-left origin), not GUI points.
            var ray = camera.ScreenPointToRay(position);
            GameObject best = null;
            var bestDistance = float.PositiveInfinity;
            foreach (var panel in YauiSystem.AllPanels)
            {
                if (panel.Panel == null) continue;

                var matrix = panel.Panel.SceneViewCanvasToWorld;
                var normal = Vector3.Cross(matrix.GetColumn(0), matrix.GetColumn(1));
                var plane = new Plane(normal.normalized, matrix.MultiplyPoint3x4(Vector3.zero));
                if (!plane.Raycast(ray, out var distance) || distance >= bestDistance) continue;

                var canvas = matrix.inverse.MultiplyPoint3x4(ray.GetPoint(distance));
                Hits.Clear();
                panel.HitTestCanvasAll(new float2(canvas.x, canvas.y), Hits);
                foreach (var (element, _) in Hits)
                {
                    var go = element.gameObject;
                    if ((ignore != null && Array.IndexOf(ignore, go) >= 0) ||
                        (filter != null && Array.IndexOf(filter, go) < 0))
                        continue;

                    best = go;
                    bestDistance = distance;
                    break;
                }
            }

            return best;
        }
    }
}