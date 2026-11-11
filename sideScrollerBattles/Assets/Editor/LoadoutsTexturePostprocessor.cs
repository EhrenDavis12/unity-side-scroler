#if UNITY_EDITOR
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;

/// <summary>
/// Applies import settings and grid-slices every layered sheet under any
/// Assets/Characters/&lt;character&gt;_loadouts/ folder, driven by that folder's own
/// loadouts.json (see LoadoutsManifest) — the Loadouts screen's equivalent of
/// AnimationVersionsTexturePostprocessor, with the same quality settings apart from the mesh:
/// 8192 max texture size, uncompressed, full-rect mesh (the tight mesh clips thin strokes such
/// as a bow limb or a string on some frames; the cells are mostly empty and overdraw is
/// irrelevant here), bottom-centre pivot, and spritePixelsPerUnit equal to the cell size so
/// one cell is one world unit — a layered archer renders exactly as large and as sharp as on
/// the Animation Versions screen. Body and weapon sheets get identical slicing, so
/// frame i of each lands on the same pixels.
///
/// CharacterSheetPostprocessor also matches this folder's path (it only skips "*_versions"),
/// slices at 256px and warns; this one runs after it (higher postprocess order) so its slicing
/// is the one that sticks.
/// </summary>
public class LoadoutsTexturePostprocessor : AssetPostprocessor
{
    private static readonly Regex LoadoutsFolderPattern =
        new Regex(@"^(Assets/Characters/[^/]+_loadouts)/([^/]+\.png)$");
    private const int MaxTextureSize = 8192;

    public override int GetPostprocessOrder() => 100;

    private void OnPreprocessTexture()
    {
        Match match = LoadoutsFolderPattern.Match(assetPath);
        if (!match.Success) return;

        string jsonPath = match.Groups[1].Value + "/loadouts.json";
        if (!File.Exists(jsonPath)) return;

        // Slicing is driven by the JSON, not just the PNG — an edit to it must reimport every sheet.
        context.DependsOnSourceAsset(jsonPath);

        LoadoutsManifest manifest = LoadoutsManifest.Read(jsonPath);
        if (manifest == null || !manifest.TryFindAnimation(match.Groups[2].Value, out LoadoutsManifest.Animation animation)) return;

        var importer = (TextureImporter)assetImporter;
        importer.GetSourceTextureWidthAndHeight(out int textureWidth, out int textureHeight);
        if (textureWidth != animation.columns * animation.cellSize || textureHeight != animation.rows * animation.cellSize)
        {
            Debug.LogError(
                $"LoadoutsTexturePostprocessor: {assetPath} is {textureWidth}x{textureHeight} but loadouts.json says " +
                $"{animation.columns}x{animation.rows} cells of {animation.cellSize}px " +
                $"({animation.columns * animation.cellSize}x{animation.rows * animation.cellSize}) — leaving it unsliced.");
            return;
        }

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Multiple;
        importer.spritePixelsPerUnit = animation.cellSize;
        importer.filterMode = FilterMode.Bilinear;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
        importer.maxTextureSize = MaxTextureSize;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.isReadable = false;
        importer.ClearPlatformTextureSettings("iPhone");
        importer.ClearPlatformTextureSettings("Android");

        var settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteMeshType = SpriteMeshType.FullRect;
        importer.SetTextureSettings(settings);

        ApplySlicing(importer, animation, assetPath);
    }

    private static void ApplySlicing(TextureImporter importer, LoadoutsManifest.Animation animation, string assetPath)
    {
        var factory = new SpriteDataProviderFactories();
        factory.Init();
        ISpriteEditorDataProvider dataProvider = factory.GetSpriteEditorDataProviderFromObject(importer);
        dataProvider.InitSpriteEditorDataProvider();

        int cell = animation.cellSize;
        var desired = new SpriteRect[animation.frameCount];
        for (int i = 0; i < animation.frameCount; i++)
        {
            int col = i % animation.columns;
            int row = i / animation.columns;
            // Row-major, top row first in the sheet; texture space has y=0 at the bottom.
            string name = $"frame_{i:D2}";
            desired[i] = new SpriteRect
            {
                name = name,
                spriteID = SpriteId(assetPath, i),
                rect = new Rect(col * cell, (animation.rows - 1 - row) * cell, cell, cell),
                alignment = SpriteAlignment.Custom,
                pivot = new Vector2(0.5f, 0f), // bottom-centre: feet anchoring
            };
        }

        // Skips the write when the sheet is already sliced this way. (While CharacterSheetPostprocessor
        // also matches this folder it replaces the rects first, so this rarely hits — the ids above
        // being deterministic is what keeps the .meta stable either way.)
        if (RectsMatch(dataProvider.GetSpriteRects(), desired)) return;

        dataProvider.SetSpriteRects(desired);
        var nameFileIdDataProvider = dataProvider.GetDataProvider<ISpriteNameFileIdDataProvider>();
        nameFileIdDataProvider.SetNameFileIdPairs(desired.Select(r => new SpriteNameFileIdPair(r.name, r.spriteID)).ToList());

        // Apply only — never SaveAndReimport() from inside a preprocess callback; it would queue
        // another import and retrigger this one forever.
        dataProvider.Apply();
    }

    /// <summary>A sprite id derived from the sheet's path and frame index, so reimporting
    /// unchanged input writes a byte-identical .meta instead of fresh random ids every time.</summary>
    private static GUID SpriteId(string assetPath, int frame)
    {
        using (var md5 = MD5.Create())
        {
            byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes($"{assetPath}#frame_{frame:D2}"));
            return new GUID(string.Concat(hash.Select(b => b.ToString("x2"))));
        }
    }

    private static bool RectsMatch(SpriteRect[] a, SpriteRect[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i].name != b[i].name || a[i].spriteID != b[i].spriteID || a[i].rect != b[i].rect || a[i].pivot != b[i].pivot) return false;
        }
        return true;
    }
}
#endif
