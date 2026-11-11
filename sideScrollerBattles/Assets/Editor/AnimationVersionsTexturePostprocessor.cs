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
/// Applies import settings and grid-slices every comparison sheet under any
/// Assets/Characters/&lt;character&gt;_versions/ folder, driven by that folder's own
/// versions.json — the Animation Versions screen's equivalent of CharacterSheetPostprocessor,
/// kept separate because a sheet here is keyed by "&lt;action&gt;_v&lt;version&gt;.png" and its
/// frame grid varies per version, not per action, so it doesn't fit characters.json's schema.
/// CharacterSheetPostprocessor explicitly skips any "*_versions" folder so the two never fight
/// over the same file.
///
/// Same quality settings as CharacterSheetPostprocessor: 8192 max texture size, uncompressed,
/// tight mesh, one cell always one world unit (spritePixelsPerUnit tracks the entry's own
/// frameWidth) — so a version's sprite renders at the same on-screen scale as any other sheet.
/// </summary>
public class AnimationVersionsTexturePostprocessor : AssetPostprocessor
{
    private static readonly Regex VersionsFolderPattern =
        new Regex(@"^(Assets/Characters/[^/]+_versions)/([^/]+\.png)$");
    private const int MaxTextureSize = 8192;

    private void OnPreprocessTexture()
    {
        Match match = VersionsFolderPattern.Match(assetPath);
        if (!match.Success) return;

        string versionsRoot = match.Groups[1].Value;
        string fileName = match.Groups[2].Value;
        string versionsJsonPath = versionsRoot + "/versions.json";
        if (!File.Exists(versionsJsonPath)) return;

        // Any sheet's slicing is driven by its folder's versions.json, not just its own PNG — so
        // an edit to the JSON alone must reimport every sheet it describes.
        context.DependsOnSourceAsset(versionsJsonPath);

        var root = JObject.Parse(File.ReadAllText(versionsJsonPath));
        if (!(root["versions"] is JArray versionsArray)) return;
        JObject entry = versionsArray.OfType<JObject>().FirstOrDefault(o => (string)o["file"] == fileName);
        if (entry == null) return;

        int frameWidth = 512; // fixed cell size for every version sheet, per the source export
        int columns = (int)entry["columns"];
        int rows = (int)entry["rows"];
        int frameCount = (int)entry["frameCount"];

        var importer = (TextureImporter)assetImporter;
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Multiple;
        importer.spritePixelsPerUnit = frameWidth;
        importer.filterMode = FilterMode.Bilinear;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
        importer.maxTextureSize = MaxTextureSize;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        // FidgetIdleMatcher (see there) reads pixels straight off a rotation's own idle sheet and
        // its fidget sheets at import time, to find each fidget's best-matching idle frames —
        // needs CPU read access. Scoped to just those sheets: isReadable keeps an extra
        // uncompressed CPU-side copy, which made every sheet's import here take 20-90s instead of
        // under a second when it was briefly set unconditionally for a whole folder.
        importer.isReadable = NeedsPixelReadAccess(versionsArray, fileName);
        importer.ClearPlatformTextureSettings("iPhone");
        importer.ClearPlatformTextureSettings("Android");

        var settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteMeshType = SpriteMeshType.Tight;
        importer.SetTextureSettings(settings);

        ApplySlicing(importer, frameWidth, columns, rows, frameCount);
    }

    /// <summary>True if this file is a rotation's own idle sheet or one of its fidget sheets, for
    /// any rotation entry in this same folder — see AnimationVersionEntry.isRotation and its
    /// rotationIdleVersion/rotationFidgetActions/rotationFidgetVersions fields.</summary>
    private static bool NeedsPixelReadAccess(JArray versionsArray, string fileName)
    {
        List<JObject> all = versionsArray.OfType<JObject>().ToList();
        foreach (JObject rotation in all.Where(o => (bool?)o["isRotation"] ?? false))
        {
            JObject idleEntry = FindEntry(all, "idle", (int?)rotation["rotationIdleVersion"] ?? -1);
            if (idleEntry != null && (string)idleEntry["file"] == fileName) return true;

            var fidgetActions = rotation["rotationFidgetActions"]?.Select(t => (string)t).ToList() ?? new List<string>();
            var fidgetVersions = rotation["rotationFidgetVersions"]?.Select(t => (int)t).ToList() ?? new List<int>();
            for (int i = 0; i < fidgetActions.Count && i < fidgetVersions.Count; i++)
            {
                JObject fidgetEntry = FindEntry(all, fidgetActions[i], fidgetVersions[i]);
                if (fidgetEntry != null && (string)fidgetEntry["file"] == fileName) return true;
            }
        }
        return false;
    }

    private static JObject FindEntry(List<JObject> entries, string action, int version) =>
        entries.FirstOrDefault(o => (string)o["action"] == action && (int?)o["version"] == version);

    private static void ApplySlicing(TextureImporter importer, int frameWidth, int columns, int rows, int frameCount)
    {
        var factory = new SpriteDataProviderFactories();
        factory.Init();
        ISpriteEditorDataProvider dataProvider = factory.GetSpriteEditorDataProviderFromObject(importer);
        dataProvider.InitSpriteEditorDataProvider();

        Dictionary<string, GUID> existingIdsByName =
            dataProvider.GetSpriteRects().ToDictionary(r => r.name, r => r.spriteID);

        var desired = new SpriteRect[frameCount];
        for (int i = 0; i < frameCount; i++)
        {
            int col = i % columns;
            int row = i / columns;
            // Row-major, top row first in the sheet; texture space has y=0 at the bottom.
            float x = col * frameWidth;
            float y = (rows - 1 - row) * frameWidth;
            string name = $"frame_{i:D2}";

            desired[i] = new SpriteRect
            {
                name = name,
                spriteID = existingIdsByName.TryGetValue(name, out GUID existingId) ? existingId : GUID.Generate(),
                rect = new Rect(x, y, frameWidth, frameWidth),
                alignment = SpriteAlignment.Custom,
                pivot = new Vector2(0.5f, 0f), // bottom-centre: feet anchoring, matches CharacterSheetPostprocessor
            };
        }

        SpriteRect[] current = dataProvider.GetSpriteRects();
        if (RectsMatch(current, desired)) return; // already sliced — nothing to apply

        dataProvider.SetSpriteRects(desired);
        var nameFileIdDataProvider = dataProvider.GetDataProvider<ISpriteNameFileIdDataProvider>();
        List<SpriteNameFileIdPair> nameFileIdPairs =
            desired.Select(r => new SpriteNameFileIdPair(r.name, r.spriteID)).ToList();
        nameFileIdDataProvider.SetNameFileIdPairs(nameFileIdPairs);

        // Apply only — never SaveAndReimport(); see CharacterSheetPostprocessor for why.
        dataProvider.Apply();
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
