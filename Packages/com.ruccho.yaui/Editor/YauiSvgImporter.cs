using System.IO;
using UnityEditor.AssetImporters;

namespace Yaui.Editor
{
    /// <summary>An optional SVG importer selected per asset with AssetDatabase.SetImporterOverride.</summary>
    [ScriptedImporter(1, null, new[] { "svg" })]
    public sealed class YauiSvgImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext ctx)
        {
            var asset = YauiVectorAsset.FromSvg(File.ReadAllText(ctx.assetPath));
            asset.name = Path.GetFileNameWithoutExtension(ctx.assetPath);
            ctx.AddObjectToAsset("vector", asset);
            ctx.SetMainObject(asset);
        }
    }
}
