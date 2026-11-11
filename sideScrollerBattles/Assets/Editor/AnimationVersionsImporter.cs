#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Builds AnimationVersionsCatalog.asset from every Assets/Characters/&lt;character&gt;_versions/
/// versions.json it finds: one AnimationVersionEntry per JSON entry, with its sprite frames loaded
/// from the sheet AnimationVersionsTexturePostprocessor already sliced (frame names "frame_00",
/// "frame_01", ...). Entries are ordered by character, then — within a character — its own "Idle
/// rotation" entry first if it has one, then by action and version; adding a whole new character
/// is just another "&lt;character&gt;_versions/" folder, no code change.
///
/// An entry with "pending": true carries no frames — the screen shows it as a placeholder card
/// instead of playing it. An entry with "isRotation": true (a character's own "Idle rotation"
/// entry) carries no frames of its own either — IdleRotationPlayer drives it from that same
/// character's idle/fidget entries, named by action/version via "rotationIdleVersion"/
/// "rotationFidgetActions"/"rotationFidgetVersions". An optional "status" string (e.g.
/// "prod candidate") is carried through as-is and shown as a badge next to the title — absent
/// entries get an empty status, which the screen renders as no badge at all.
///
/// After every entry is loaded, runs FidgetIdleMatcher once per rotation entry's fidgets against
/// that same rotation's own idle frames, so IdleRotationPlayer's crossfades never do image work at
/// runtime — see Assets/Editor/FidgetIdleMatcher.cs and AnimationVersionEntry.matchedIdleFrames.
///
/// Re-running with unchanged input overwrites the same asset in place.
/// </summary>
public static class AnimationVersionsImporter
{
    private const string CharactersRoot = "Assets/Characters";
    private const string CatalogAssetPath = CharactersRoot + "/AnimationVersionsCatalog.asset";

    // How many of a rotation's idle frames closest to rest IdleRotationPlayer may cut away from —
    // see FidgetIdleMatcher.FindBestMatches.
    private const int FidgetMatchCount = 5;

    [MenuItem("Characters/Import Animation Versions")]
    public static void ImportVersions()
    {
        List<string> versionsJsonPaths = Directory.GetDirectories(CharactersRoot)
            .Where(dir => dir.Replace('\\', '/').EndsWith("_versions"))
            .Select(dir => dir.Replace('\\', '/') + "/versions.json")
            .Where(File.Exists)
            .OrderBy(p => p)
            .ToList();

        if (versionsJsonPaths.Count == 0)
        {
            Debug.LogError($"AnimationVersionsImporter: no \"*_versions/versions.json\" found under {CharactersRoot} — aborting.");
            return;
        }

        var entries = new List<AnimationVersionEntry>();
        foreach (string versionsJsonPath in versionsJsonPaths)
        {
            string versionsRoot = versionsJsonPath.Substring(0, versionsJsonPath.Length - "/versions.json".Length);
            if (!LoadEntries(versionsJsonPath, versionsRoot, entries)) return; // already logged — abort, no assets touched
        }

        // Group by character; within a character, its own "Idle rotation" entry first (regardless
        // of what its own action name would otherwise alphabetize to), then by action and version.
        entries = entries
            .OrderBy(e => e.character)
            .ThenBy(e => e.isRotation ? 0 : 1)
            .ThenBy(e => e.action)
            .ThenBy(e => e.version)
            .ToList();

        ComputeFidgetMatches(entries);

        AnimationVersionsCatalog catalog = AssetDatabase.LoadAssetAtPath<AnimationVersionsCatalog>(CatalogAssetPath);
        if (catalog == null)
        {
            catalog = ScriptableObject.CreateInstance<AnimationVersionsCatalog>();
            AssetDatabase.CreateAsset(catalog, CatalogAssetPath);
        }
        catalog.EditorInit(entries);
        EditorUtility.SetDirty(catalog);
        AssetDatabase.SaveAssets();

        Debug.Log($"Characters/Import Animation Versions: built {entries.Count} entrie(s) from {versionsJsonPaths.Count} character folder(s) at {CatalogAssetPath}.");
    }

    /// <summary>Parses one character folder's versions.json, appending its entries to
    /// <paramref name="entries"/>. Returns false (after logging) on any error, so the caller can
    /// abort without touching any asset.</summary>
    private static bool LoadEntries(string versionsJsonPath, string versionsRoot, List<AnimationVersionEntry> entries)
    {
        var root = JObject.Parse(File.ReadAllText(versionsJsonPath));
        if (!(root["versions"] is JArray versionsArray))
        {
            Debug.LogError($"AnimationVersionsImporter: {versionsJsonPath} has no \"versions\" array — aborting, no assets touched.");
            return false;
        }

        foreach (JToken token in versionsArray)
        {
            if (!(token is JObject entryJson)) continue;

            bool pending = (bool?)entryJson["pending"] ?? false;
            bool isRotation = (bool?)entryJson["isRotation"] ?? false;
            var entry = new AnimationVersionEntry
            {
                character = (string)entryJson["character"],
                action = (string)entryJson["action"],
                version = (int)entryJson["version"],
                description = (string)entryJson["description"],
                pending = pending,
                status = (string)entryJson["status"] ?? "",
                isRotation = isRotation,
            };

            if (isRotation)
            {
                entry.rotationIdleVersion = (int?)entryJson["rotationIdleVersion"] ?? -1;
                entry.rotationFidgetActions = entryJson["rotationFidgetActions"]?.Select(t => (string)t).ToArray() ?? System.Array.Empty<string>();
                entry.rotationFidgetVersions = entryJson["rotationFidgetVersions"]?.Select(t => (int)t).ToArray() ?? System.Array.Empty<int>();
            }

            if (pending || isRotation)
            {
                entries.Add(entry);
                continue;
            }

            string fileName = (string)entryJson["file"];
            int frameCount = (int)entryJson["frameCount"];
            string texturePath = $"{versionsRoot}/{fileName}";

            Sprite[] frames = LoadOrderedFrames(texturePath, frameCount);
            if (frames == null)
            {
                Debug.LogError(
                    $"AnimationVersionsImporter: \"{entry.character}\" \"{entry.action}\" v{entry.version} — no sprites " +
                    $"sliced at {texturePath} (expected {frameCount}) — aborting, no assets touched.");
                return false;
            }

            entry.fps = (int)entryJson["fps"];
            entry.loop = (bool)entryJson["loop"];
            entry.frames = frames;
            entries.Add(entry);
        }
        return true;
    }

    /// <summary>For every rotation entry, finds its own idle entry (same character,
    /// action "idle", version rotationIdleVersion) and each of its fidget entries (same character,
    /// rotationFidgetActions[i]/rotationFidgetVersions[i]), then fills in each fidget's
    /// matchedIdleFrames/matchedIdleFrameDistances against that idle entry's own frames.</summary>
    private static void ComputeFidgetMatches(List<AnimationVersionEntry> entries)
    {
        // ToList(): the loop body mutates entries (writing matched frames back into fidget
        // entries) — enumerating a lazy Where() directly over the same list it's about to
        // mutate throws "Collection was modified".
        foreach (AnimationVersionEntry rotation in entries.Where(e => e.isRotation).ToList())
        {
            int idleIndex = entries.FindIndex(e =>
                e.character == rotation.character && e.action == "idle" && e.version == rotation.rotationIdleVersion);
            if (idleIndex < 0)
            {
                Debug.LogWarning(
                    $"AnimationVersionsImporter: \"{rotation.character}\" idle rotation names idle v{rotation.rotationIdleVersion}, " +
                    "which doesn't exist — skipping its fidget/idle match analysis.");
                continue;
            }
            Sprite[] idleFrames = entries[idleIndex].frames;

            for (int i = 0; i < rotation.rotationFidgetActions.Length && i < rotation.rotationFidgetVersions.Length; i++)
            {
                string fidgetAction = rotation.rotationFidgetActions[i];
                int fidgetVersion = rotation.rotationFidgetVersions[i];
                int fidgetIndex = entries.FindIndex(e =>
                    e.character == rotation.character && e.action == fidgetAction && e.version == fidgetVersion);
                if (fidgetIndex < 0)
                {
                    Debug.LogWarning(
                        $"AnimationVersionsImporter: \"{rotation.character}\" idle rotation names \"{fidgetAction}\" " +
                        $"v{fidgetVersion}, which doesn't exist — skipping.");
                    continue;
                }

                AnimationVersionEntry fidget = entries[fidgetIndex];
                if (fidget.frames == null || fidget.frames.Length == 0) continue;

                int[] matches = FidgetIdleMatcher.FindBestMatches(idleFrames, fidget.frames[0], FidgetMatchCount, out float[] distances);
                fidget.matchedIdleFrames = matches;
                fidget.matchedIdleFrameDistances = distances;
                entries[fidgetIndex] = fidget;

                Debug.Log(
                    $"AnimationVersionsImporter: \"{fidget.character}\" \"{fidget.action}\" v{fidget.version} — " +
                    $"best-matching idle v{rotation.rotationIdleVersion} frames {string.Join(", ", matches)} " +
                    $"(distances {string.Join(", ", distances.Select(d => d.ToString("F4")))}).");
            }
        }
    }

    private static Sprite[] LoadOrderedFrames(string texturePath, int frameCount)
    {
        var sprites = new Sprite[frameCount];
        foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(texturePath))
        {
            if (asset is Sprite sprite && sprite.name.StartsWith("frame_"))
            {
                if (int.TryParse(sprite.name.Substring("frame_".Length), out int frameIndex) && frameIndex < frameCount)
                {
                    sprites[frameIndex] = sprite;
                }
            }
        }
        return sprites.Any(s => s == null) ? null : sprites;
    }
}
#endif
