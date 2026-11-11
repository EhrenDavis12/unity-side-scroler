#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;

/// <summary>
/// Builds LoadoutsCatalog.asset from every Assets/Characters/&lt;character&gt;_loadouts/
/// loadouts.json it finds — one LoadoutSet per folder, with the asset path of each body and
/// weapon sheet per animation of each frame set (see LoadoutsManifest for the naming). Sheets are recorded by
/// path, not loaded, so the screen can hold just the animation on screen in memory.
///
/// A sheet the JSON names that is missing on disk, or not sliced to the JSON's frame count, is
/// a logged warning and an empty path (that layer draws nothing) — never an exception. A frame
/// set's animation with no usable body sheet at all is left out of the catalog. Re-running with
/// unchanged input overwrites the same asset in place; with no *_loadouts folders it writes an
/// empty catalog and warns, so the screen can say so.
///
/// A character with an Assets/Characters/&lt;character&gt;_versions/versions.json but no
/// *_loadouts folder (the knight) gets a LoadoutSet synthesised from it: one body, no weapons,
/// and per action the version marked prod (a status starting with "prod"), else the highest
/// version, pointing at the version sheets where they already sit — nothing is copied.
/// </summary>
public static class LoadoutsImporter
{
    private const string CharactersRoot = "Assets/Characters";
    private const string CatalogAssetPath = CharactersRoot + "/LoadoutsCatalog.asset";

    [MenuItem("Characters/Import Loadouts")]
    public static void ImportLoadouts()
    {
        AssetDatabase.Refresh(); // sheets dropped in just now must be imported (sliced) before they are checked

        List<string> jsonPaths = Directory.GetDirectories(CharactersRoot)
            .Select(dir => dir.Replace('\\', '/'))
            .Where(dir => dir.EndsWith("_loadouts"))
            .Select(dir => dir + "/loadouts.json")
            .Where(File.Exists)
            .OrderBy(p => p)
            .ToList();

        if (jsonPaths.Count == 0)
        {
            Debug.LogWarning($"LoadoutsImporter: no \"*_loadouts/loadouts.json\" found under {CharactersRoot} — writing an empty catalog.");
        }

        var sets = new List<LoadoutSet>();
        foreach (string jsonPath in jsonPaths)
        {
            LoadoutsManifest manifest = LoadoutsManifest.Read(jsonPath);
            if (manifest == null) continue; // already logged
            sets.Add(BuildSet(manifest, jsonPath.Substring(0, jsonPath.Length - "/loadouts.json".Length)));
        }
        foreach (string versionsPath in Directory.GetDirectories(CharactersRoot)
                     .Select(dir => dir.Replace('\\', '/') + "/versions.json")
                     .Where(File.Exists)
                     .OrderBy(p => p))
        {
            LoadoutSet? versionsSet = BuildVersionsSet(versionsPath, sets);
            if (versionsSet.HasValue) sets.Add(versionsSet.Value);
        }

        LoadoutsCatalog catalog = AssetDatabase.LoadAssetAtPath<LoadoutsCatalog>(CatalogAssetPath);
        if (catalog == null)
        {
            catalog = ScriptableObject.CreateInstance<LoadoutsCatalog>();
            AssetDatabase.CreateAsset(catalog, CatalogAssetPath);
        }
        catalog.EditorInit(sets.ToArray());
        EditorUtility.SetDirty(catalog);
        AssetDatabase.SaveAssets();

        Debug.Log($"Characters/Import Loadouts: built {sets.Count} character(s) from {jsonPaths.Count} folder(s) at {CatalogAssetPath}.");
    }

    private static LoadoutSet BuildSet(LoadoutsManifest manifest, string root)
    {
        var frameSets = new List<LoadoutFrameSet>();
        foreach (LoadoutsManifest.FrameSet frameSet in manifest.frameSets)
        {
            var animations = new List<LoadoutAnimation>();
            foreach (LoadoutsManifest.Animation a in frameSet.animations)
            {
                // A body outside this frame set has no sheet here by design — no warning.
                string[] bodySheets = manifest.bodies
                    .Select(b => b.frameSet == frameSet.id ? CheckSheet(root, LoadoutsManifest.BodyFile(a, b.id), a) : "").ToArray();
                string[] weaponSheets = manifest.weapons
                    .Select(w => CheckSheet(root, LoadoutsManifest.WeaponFile(frameSet, a, w.id), a)).ToArray();
                if (bodySheets.All(string.IsNullOrEmpty))
                {
                    Debug.LogWarning($"LoadoutsImporter: \"{manifest.character}\" frame set \"{frameSet.id}\" animation \"{a.action}\" has no usable body sheet — leaving it out.");
                    continue;
                }
                Rect[] bodyExtents = bodySheets.Select(MeasureExtent).ToArray();
                Rect[] weaponExtents = weaponSheets.Select(MeasureExtent).ToArray();
                // One sweep per animation: its sheets are all loaded by now, and none of the next
                // animation's are, so the peak is one animation's sheets rather than every sheet's.
                EditorUtility.UnloadUnusedAssetsImmediate();
                animations.Add(new LoadoutAnimation
                {
                    action = a.action,
                    label = a.label,
                    frameCount = a.frameCount,
                    fps = a.fps,
                    loop = a.loop,
                    bodySheets = bodySheets,
                    weaponSheets = weaponSheets,
                    bodyExtents = bodyExtents,
                    weaponExtents = weaponExtents,
                });
            }
            frameSets.Add(new LoadoutFrameSet { id = frameSet.id, animations = animations.ToArray() });
        }

        return new LoadoutSet
        {
            character = manifest.character,
            bodies = manifest.bodies.ToArray(),
            weapons = manifest.weapons.ToArray(),
            frameSets = frameSets.ToArray(),
        };
    }

    /// <summary>A LoadoutSet from one *_versions/versions.json — see the class comment; null when
    /// the folder's character already has a loadouts set, or the file names no playable version.</summary>
    private static LoadoutSet? BuildVersionsSet(string versionsPath, List<LoadoutSet> existing)
    {
        string root = versionsPath.Substring(0, versionsPath.Length - "/versions.json".Length);
        List<JObject> entries;
        try
        {
            entries = JObject.Parse(File.ReadAllText(versionsPath))["versions"].OfType<JObject>().ToList();
        }
        catch (System.Exception e)
        {
            Debug.LogError($"LoadoutsImporter: could not read {versionsPath}: {e.Message}");
            return null;
        }

        string character = entries.Select(o => (string)o["character"]).FirstOrDefault(c => !string.IsNullOrEmpty(c));
        if (character == null || existing.Any(s => s.character == character)) return null;

        var animations = new List<LoadoutAnimation>();
        // First-seen action order; the rotation entry plays no sheet of its own, so it has no file.
        foreach (IGrouping<string, JObject> group in entries.Where(o => !string.IsNullOrEmpty((string)o["file"])).GroupBy(o => (string)o["action"]))
        {
            JObject pick = group
                .OrderByDescending(o => ((string)o["status"] ?? "").StartsWith("prod"))
                .ThenByDescending(o => (int)o["version"])
                .First();
            var animation = new LoadoutsManifest.Animation
            {
                action = group.Key,
                label = ActionLabel(group.Key),
                frameCount = (int)pick["frameCount"],
                fps = (int)pick["fps"],
                loop = (bool)pick["loop"],
            };
            string sheet = CheckSheet(root, (string)pick["file"], animation);
            if (sheet == "")
            {
                Debug.LogWarning($"LoadoutsImporter: \"{character}\" action \"{group.Key}\" has no usable sheet — leaving it out.");
                continue;
            }
            animations.Add(new LoadoutAnimation
            {
                action = animation.action,
                label = animation.label,
                frameCount = animation.frameCount,
                fps = animation.fps,
                loop = animation.loop,
                bodySheets = new[] { sheet },
                weaponSheets = new string[0],
                bodyExtents = new[] { MeasureExtent(sheet) },
                weaponExtents = new Rect[0],
            });
            EditorUtility.UnloadUnusedAssetsImmediate();
        }
        if (animations.Count == 0) return null;

        string name = char.ToUpperInvariant(character[0]) + character.Substring(1);
        return new LoadoutSet
        {
            character = character,
            bodies = new[] { new LoadoutBody { id = character, label = name, frameSet = "versions" } },
            weapons = new LoadoutOption[0],
            frameSets = new[] { new LoadoutFrameSet { id = "versions", animations = animations.ToArray() } },
        };
    }

    private static string ActionLabel(string action)
    {
        string spaced = action.Replace('_', ' ');
        return char.ToUpperInvariant(spaced[0]) + spaced.Substring(1);
    }

    /// <summary>The union of a sheet's sprites' tight bounds, in cell units from the bottom-centre
    /// pivot (see LoadoutAnimation.bodyExtents); a zero rect for a missing sheet. Loads the sheet;
    /// the caller sweeps once per animation, since every sheet at once would be over a GB.</summary>
    private static Rect MeasureExtent(string path)
    {
        if (string.IsNullOrEmpty(path)) return Rect.zero;

        Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
        Vector2 max = new Vector2(float.MinValue, float.MinValue);
        foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
        {
            if (!(asset is Sprite sprite)) continue;
            // The tight mesh hugs the drawn pixels (Sprite.bounds would be the whole cell); one
            // cell is one unit (PPU = cellSize) and vertices are relative to the pivot.
            foreach (Vector2 v in sprite.vertices)
            {
                min = Vector2.Min(min, v);
                max = Vector2.Max(max, v);
            }
        }
        return max.x < min.x ? Rect.zero : Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    /// <summary>Returns the sheet's asset path if it exists and is sliced into the animation's
    /// frame count; otherwise logs a warning and returns "". Reads the slicing from the
    /// importer's data provider, so the (huge) texture is never loaded.</summary>
    private static string CheckSheet(string root, string fileName, LoadoutsManifest.Animation animation)
    {
        string path = $"{root}/{fileName}";
        if (!File.Exists(path))
        {
            Debug.LogWarning($"LoadoutsImporter: {path} is named in loadouts.json but missing — skipping it.");
            return "";
        }

        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null)
        {
            Debug.LogWarning($"LoadoutsImporter: {path} is not imported as a texture yet — skipping it.");
            return "";
        }

        var factory = new SpriteDataProviderFactories();
        factory.Init();
        ISpriteEditorDataProvider dataProvider = factory.GetSpriteEditorDataProviderFromObject(importer);
        dataProvider.InitSpriteEditorDataProvider();
        int sliced = dataProvider.GetSpriteRects().Length;
        if (sliced != animation.frameCount)
        {
            Debug.LogWarning($"LoadoutsImporter: {path} is sliced into {sliced} frame(s), loadouts.json says {animation.frameCount} — skipping it.");
            return "";
        }
        return path;
    }
}
#endif
