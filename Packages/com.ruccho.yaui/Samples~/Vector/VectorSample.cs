using System.Collections.Generic;
using UnityEngine;

namespace Yaui.Samples.Vector
{
    /// <summary>Builds a grid of SVG fills and strokes at five pixel sizes.</summary>
    public sealed class VectorSample : MonoBehaviour
    {
        [SerializeField] private TextAsset[] icons = System.Array.Empty<TextAsset>();
        private readonly List<YauiVectorAsset> _assets = new();
        private GameObject _panel;

        private void Start()
        {
            if (Camera.main == null)
            {
                var camera = new GameObject("Main Camera").AddComponent<Camera>();
                camera.tag = "MainCamera";
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.08f, 0.1f, 0.14f);
                camera.transform.position = new Vector3(0f, 0f, -10f);
            }

            _panel = new GameObject("SVG grid");
            var panel = _panel.AddComponent<YauiPanel>();
            panel.ScaleMode = PanelScaleMode.ConstantScale;
            var sizes = new[] { 16f, 20f, 24f, 48f, 96f };
            var colors = new[] { Color.white, new Color(0.25f, 0.75f, 1f), new Color(1f, 0.45f, 0.25f) };
            var sources = new List<string>();
            foreach (var icon in icons) if (icon != null) sources.Add(icon.text);
            sources.Add("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 24 24\"><rect x=\"2\" y=\"2\" width=\"14\" height=\"14\" fill=\"#e34b55\"/><circle cx=\"15\" cy=\"15\" r=\"7\" fill=\"#3ca8df\"/><path d=\"M3 20L12 4L21 20Z\" fill=\"#ffda4a\" fill-opacity=\"0.7\"/></svg>");
            sources.Add(Stroke("butt", "miter"));
            sources.Add(Stroke("square", "bevel"));
            var row = 0;
            foreach (var source in sources)
            {
                var asset = YauiVectorAsset.FromSvg(source);
                _assets.Add(asset);
                for (var color = 0; color < colors.Length; color++)
                for (var column = 0; column < sizes.Length; column++)
                {
                    var go = new GameObject("SVG " + sizes[column] + " px");
                    go.transform.SetParent(_panel.transform, false);
                    var svg = go.AddComponent<YauiSvg>();
                    svg.Vector = asset;
                    svg.Color = colors[color];
                    var layout = LayoutStyle.Default;
                    layout.position = PositionType.Absolute;
                    layout.inset = new Edges(24 + column * 116 + color * 600, 24 + row * 116, Length.Auto, Length.Auto);
                    layout.width = sizes[column];
                    layout.height = sizes[column];
                    svg.Layout = layout;
                }

                row++;
            }
        }

        private static string Stroke(string cap, string join)
        {
            return "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"" +
                   cap + "\" stroke-linejoin=\"" + join + "\"><path d=\"M4 18V6H16L20 12\"/></svg>";
        }

        private void OnDestroy()
        {
            if (_panel != null) Destroy(_panel);
            foreach (var asset in _assets) Destroy(asset);
        }
    }
}
