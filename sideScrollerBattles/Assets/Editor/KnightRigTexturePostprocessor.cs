#if UNITY_EDITOR
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Applies Sprite (Single) import settings to every part PNG under Assets/Characters/knight_rig/,
/// with each sprite's pivot set to its joint from rig.json — converted from the canvas's
/// top-left, y-down pixel space into Unity's normalized, bottom-left, y-up sprite-pivot space.
/// Distinct from CharacterSheetPostprocessor: these are single-pose cutout parts, never sliced
/// into a per-frame grid, and this rig has no entry in characters.json at all — CharacterSheetPostprocessor
/// recognizes and skips this folder by its rig.json sibling instead of touching these files itself.
/// Also rebuilds KnightRig.asset (see <see cref="OnPostprocessAllAssets"/>) whenever rig.json or a
/// part PNG changes, so a re-authored joint or a re-cropped rect never leaves the built rig stale.
/// </summary>
public class KnightRigTexturePostprocessor : AssetPostprocessor
{
    private const string RigRoot = "Assets/Characters/knight_rig";
    private const string RigJsonPath = RigRoot + "/rig.json";
    private const float PixelsPerUnit = 256f;

    private void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith(RigRoot + "/") || !assetPath.EndsWith(".png")) return;
        if (!File.Exists(RigJsonPath)) return;

        // rig.json alone changing (a re-authored joint, say) must reimport every part it names.
        context.DependsOnSourceAsset(RigJsonPath);

        string fileName = Path.GetFileName(assetPath);
        if (!TryFindPart(fileName, out JObject part)) return;

        var rect = ((JArray)part["rect"]).Select(v => (float)v).ToArray();
        var joint = ((JArray)part["joint"]).Select(v => (float)v).ToArray();
        float x = rect[0], y = rect[1], w = rect[2], h = rect[3];
        float jointX = joint[0], jointY = joint[1];

        float pivotX = (jointX - x) / w;
        float pivotY = 1f - (jointY - y) / h;

        var importer = (TextureImporter)assetImporter;
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = PixelsPerUnit;
        importer.filterMode = FilterMode.Bilinear;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
        // Left uncompressed, same reasoning as CharacterSheetPostprocessor: block compression
        // greys the dark outline antialiasing on these parts.
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.ClearPlatformTextureSettings("iPhone");
        importer.ClearPlatformTextureSettings("Android");

        var settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteAlignment = (int)SpriteAlignment.Custom;
        settings.spritePivot = new Vector2(pivotX, pivotY);
        settings.spriteMeshType = SpriteMeshType.Tight;
        importer.SetTextureSettings(settings);
    }

    /// <summary>Rebuilds KnightRig.asset whenever this batch imported, deleted, or moved rig.json
    /// itself or one of its part PNGs — so RigCharacterImporter never needs to be re-run by hand
    /// after a rig edit, a part being removed, or a part being renamed/relocated. Runs after every
    /// texture in the batch has already been re-imported above, so the sprites RigCharacterImporter
    /// loads already reflect this batch's pivots. A part deleted (or moved away) without a
    /// replacement still lands cleanly: ImportRig() aborts with a clear "no sprite imported" error
    /// rather than building a broken rig.</summary>
    private static void OnPostprocessAllAssets(
        string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
    {
        bool rigChanged =
            importedAssets.Any(IsRigAsset) ||
            deletedAssets.Any(IsRigAsset) ||
            movedAssets.Any(IsRigAsset) ||
            movedFromAssetPaths.Any(IsRigAsset);
        if (rigChanged) RigCharacterImporter.ImportRig();
    }

    private static bool IsRigAsset(string path) =>
        path == RigJsonPath || (path.StartsWith(RigRoot + "/") && path.EndsWith(".png"));

    private static bool TryFindPart(string fileName, out JObject part)
    {
        var root = JObject.Parse(File.ReadAllText(RigJsonPath));
        if (root["parts"] is JObject parts)
        {
            foreach (JProperty prop in parts.Properties())
            {
                if (prop.Value is JObject partJson && (string)partJson["file"] == fileName)
                {
                    part = partJson;
                    return true;
                }
            }
        }
        part = null;
        return false;
    }
}
#endif
