#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;

/// <summary>
/// Applies import settings and grid-slices every character sheet PNG under
/// Assets/Characters/&lt;id&gt;/ automatically on import, driven by Assets/Characters/characters.json
/// — so a sheet gets its sprite settings and slicing the moment Unity imports it, whether that
/// import came from Characters/Import Sheets or from Unity noticing the file changed on disk.
/// Slicing happens in <see cref="OnPreprocessTexture"/>, before the import that is about to
/// generate this texture's sprites: applying the desired rects there is what makes them reach
/// the sprites that same pass produces, unlike applying them after (the sprites for that pass
/// are already built by then). <see cref="AssetImporters.AssetImportContext.DependsOnSourceAsset"/>
/// on characters.json means editing the JSON alone reimports every sheet — Unity doesn't need
/// the PNG itself to change on disk. Re-running on an already-sliced sheet with unchanged
/// geometry is a no-op: rects are only reapplied when they differ from what's already there.
/// Source sheets are left uncompressed (no platform overrides) — they never ship; only the
/// per-character atlas pages built by CharacterAddressablesSetup do.
/// </summary>
public class CharacterSheetPostprocessor : AssetPostprocessor
{
    private const string CharactersRoot = "Assets/Characters";
    private const string CatalogJsonPath = CharactersRoot + "/characters.json";
    private const int PixelsPerUnit = 256;
    private const int MaxTextureSize = 2048;

    private static readonly Regex SheetPathPattern = new Regex(@"^Assets/Characters/([^/]+)/([^/]+)\.png$");

    private void OnPreprocessTexture()
    {
        if (!TryParseSheetPath(assetPath, out string id, out string action)) return;

        // Any sheet's slicing is driven by characters.json, not just its own PNG — so an edit
        // to the JSON alone must reimport every sheet it describes.
        context.DependsOnSourceAsset(CatalogJsonPath);

        var importer = (TextureImporter)assetImporter;
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Multiple;
        importer.spritePixelsPerUnit = PixelsPerUnit;
        importer.filterMode = FilterMode.Bilinear;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
        importer.maxTextureSize = MaxTextureSize;
        // Source sheets never ship — only the atlas pages built from them do — so leave them
        // uncompressed rather than the "Compressed" Unity defaults to. Left compressed, the
        // Sprite Atlas packer warns per sprite that packing from a compressed source loses
        // pixel detail; this is what actually produced the reported warnings, not the
        // now-removed iOS platform override alone.
        importer.textureCompression = TextureImporterCompression.Uncompressed;

        var settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteMeshType = SpriteMeshType.FullRect;
        importer.SetTextureSettings(settings);

        ApplySlicing(importer, id, action);
    }

    private static void ApplySlicing(TextureImporter importer, string id, string action)
    {
        ISpriteEditorDataProvider dataProvider = GetDataProvider(importer);

        importer.GetSourceTextureWidthAndHeight(out int textureWidth, out int textureHeight);
        SpriteRect[] desired = BuildDesiredRects(id, action, textureWidth, textureHeight, dataProvider);
        SpriteRect[] current = dataProvider.GetSpriteRects();
        if (RectsMatch(current, desired)) return; // already sliced — nothing to apply

        dataProvider.SetSpriteRects(desired);
        var nameFileIdDataProvider = dataProvider.GetDataProvider<ISpriteNameFileIdDataProvider>();
        List<SpriteNameFileIdPair> nameFileIdPairs =
            desired.Select(r => new SpriteNameFileIdPair(r.name, r.spriteID)).ToList();
        nameFileIdDataProvider.SetNameFileIdPairs(nameFileIdPairs);

        // Apply only — never SaveAndReimport(). We're still inside the import that is about to
        // build this texture's sprites; the importer picks up the applied data provider changes
        // as part of that same pass, so the sprites it produces use the new rects. Calling
        // SaveAndReimport() from inside a preprocess callback would instead queue a second
        // import and retrigger OnPreprocessTexture on the same asset indefinitely.
        dataProvider.Apply();
    }

    private static ISpriteEditorDataProvider GetDataProvider(TextureImporter importer)
    {
        var factory = new SpriteDataProviderFactories();
        factory.Init();
        ISpriteEditorDataProvider dataProvider = factory.GetSpriteEditorDataProviderFromObject(importer);
        dataProvider.InitSpriteEditorDataProvider();
        return dataProvider;
    }

    private static bool TryParseSheetPath(string path, out string id, out string action)
    {
        Match match = SheetPathPattern.Match(path);
        if (!match.Success)
        {
            id = null;
            action = null;
            return false;
        }
        id = match.Groups[1].Value;
        action = match.Groups[2].Value;
        return true;
    }

    private static SpriteRect[] BuildDesiredRects(
        string id, string action, int textureWidth, int textureHeight, ISpriteEditorDataProvider dataProvider)
    {
        if (!TryReadFrameGrid(id, action, out int frameWidth, out int frameHeight, out int columns, out int rows, out int frameCount))
        {
            // Fallback: slice the full grid at the fixed 256px cell size, and say so.
            frameWidth = PixelsPerUnit;
            frameHeight = PixelsPerUnit;
            columns = Mathf.Max(1, textureWidth / PixelsPerUnit);
            rows = Mathf.Max(1, textureHeight / PixelsPerUnit);
            frameCount = columns * rows;
            Debug.LogWarning(
                $"CharacterSheetPostprocessor: characters.json has no entry for \"{id}\"/\"{action}\" — " +
                $"slicing the full {columns}x{rows} grid instead.");
        }

        Dictionary<string, GUID> existingIdsByName =
            dataProvider.GetSpriteRects().ToDictionary(r => r.name, r => r.spriteID);

        var rects = new SpriteRect[frameCount];
        for (int i = 0; i < frameCount; i++)
        {
            int col = i % columns;
            int row = i / columns;
            // Row-major, top row first in the sheet; texture space has y=0 at the bottom, so
            // the top sheet row (row 0) sits at the highest y.
            float x = col * frameWidth;
            float y = (rows - 1 - row) * frameHeight;
            string name = $"{action}_{i:D2}";

            rects[i] = new SpriteRect
            {
                name = name,
                spriteID = existingIdsByName.TryGetValue(name, out GUID existingId) ? existingId : GUID.Generate(),
                rect = new Rect(x, y, frameWidth, frameHeight),
                alignment = SpriteAlignment.Custom,
                pivot = new Vector2(0.5f, 0f), // bottom-centre: feet anchoring
            };
        }
        return rects;
    }

    private static bool TryReadFrameGrid(
        string id, string action, out int frameWidth, out int frameHeight, out int columns, out int rows, out int frameCount)
    {
        frameWidth = frameHeight = columns = rows = frameCount = 0;
        if (!File.Exists(CatalogJsonPath)) return false;

        var root = JObject.Parse(File.ReadAllText(CatalogJsonPath));
        if (!(root["characters"] is JArray characters)) return false;

        JObject characterJson = characters.OfType<JObject>().FirstOrDefault(c => (string)c["id"] == id);
        if (!(characterJson?["animations"]?[action] is JObject animation)) return false;

        frameWidth = (int?)animation["frameWidth"] ?? 0;
        frameHeight = (int?)animation["frameHeight"] ?? 0;
        columns = (int?)animation["columns"] ?? 0;
        rows = (int?)animation["rows"] ?? 0;
        frameCount = (int?)animation["frameCount"] ?? 0;
        return frameWidth > 0 && frameHeight > 0 && columns > 0 && rows > 0 && frameCount > 0;
    }

    private static bool RectsMatch(SpriteRect[] a, SpriteRect[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i].name != b[i].name) return false;
            if (a[i].rect != b[i].rect) return false;
            if (a[i].pivot != b[i].pivot) return false;
        }
        return true;
    }
}
#endif
