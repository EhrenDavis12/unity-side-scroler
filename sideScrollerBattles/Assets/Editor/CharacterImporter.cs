#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.AddressableAssets;

/// <summary>
/// Imports the showcase character sheets under Assets/Characters into per-action AnimationClips,
/// one AnimatorController per character, and a CharacterDefinition + CharacterCatalog pair — all
/// driven from Assets/Characters/characters.json, the same contract the Flutter demo reads.
/// Sprite slicing and per-texture import settings are handled automatically on import by
/// <see cref="CharacterSheetPostprocessor"/>, which declares a dependency on characters.json so
/// editing the JSON alone (without touching a PNG) still reimports every sheet it describes.
/// This menu item regenerates clips, controllers, definitions and the catalog from whatever the
/// sheets were sliced into, and prunes any character no longer present in the JSON. Re-running
/// with unchanged input touches nothing on disk.
/// </summary>
public static class CharacterImporter
{
    private const string CharactersRoot = "Assets/Characters";
    private const string CatalogJsonPath = CharactersRoot + "/characters.json";
    private const string CatalogAssetPath = CharactersRoot + "/CharacterCatalog.asset";

    [MenuItem("Characters/Import Sheets")]
    public static void ImportSheets()
    {
        string json = File.ReadAllText(CatalogJsonPath);
        var root = JObject.Parse(json);
        if (!(root["characters"] is JArray charactersArray))
        {
            Debug.LogError("CharacterImporter: characters.json has no \"characters\" array — aborting, no assets touched.");
            return;
        }
        List<JObject> characters = charactersArray.OfType<JObject>().ToList();

        // Validate everything before mutating anything: a bad entry partway through the JSON
        // must never leave some characters imported and others silently skipped.
        if (!ValidateCatalog(characters)) return;

        // Ids the previous run knew about — anything here that's missing from the current JSON
        // has been removed and needs its generated assets and Addressables entries pruned.
        CharacterCatalog existingCatalog = AssetDatabase.LoadAssetAtPath<CharacterCatalog>(CatalogAssetPath);
        List<string> previousIds = existingCatalog != null
            ? existingCatalog.Characters.Select(e => e.id).ToList()
            : new List<string>();

        var entries = new List<CharacterCatalogEntry>();
        foreach (JObject characterJson in characters)
        {
            CharacterDefinition definition = ImportCharacter(characterJson);
            if (definition == null) continue;

            string id = (string)characterJson["id"];
            string displayName = (string)characterJson["name"];
            string definitionPath = AssetDatabase.GetAssetPath(definition);
            string folder = $"{CharactersRoot}/{id}";
            AssetReferenceT<CharacterDefinition> reference =
                CharacterAddressablesSetup.MakeCharacterAddressable(id, definitionPath, folder);
            entries.Add(new CharacterCatalogEntry { id = id, displayName = displayName, definition = reference });
        }

        var currentIds = new HashSet<string>(characters.Select(c => (string)c["id"]));
        foreach (string previousId in previousIds)
        {
            if (!currentIds.Contains(previousId)) PruneRemovedCharacter(previousId);
        }

        CharacterCatalog catalog = existingCatalog != null ? existingCatalog : ScriptableObject.CreateInstance<CharacterCatalog>();
        if (existingCatalog == null) AssetDatabase.CreateAsset(catalog, CatalogAssetPath);
        catalog.EditorInit(entries);
        EditorUtility.SetDirty(catalog);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"Characters/Import Sheets: imported {entries.Count} character(s).");
    }

    /// <summary>Removes a character's generated definition, clips, controller and atlas (never
    /// its source sheet PNGs) plus its Addressables entries, for an id that was in the catalog
    /// before this run but is no longer in characters.json.</summary>
    private static void PruneRemovedCharacter(string id)
    {
        string folder = $"{CharactersRoot}/{id}";
        AssetDatabase.DeleteAsset($"{folder}/{id}.asset");
        AssetDatabase.DeleteAsset($"{folder}/{id}.controller");
        AssetDatabase.DeleteAsset($"{folder}/{id}.spriteatlasv2");
        foreach (string clipPath in AssetDatabase.FindAssets("t:AnimationClip", new[] { folder })
                     .Select(AssetDatabase.GUIDToAssetPath))
        {
            AssetDatabase.DeleteAsset(clipPath);
        }
        CharacterAddressablesSetup.RemoveCharacterGroup(id);
        Debug.LogWarning(
            $"CharacterImporter: character \"{id}\" is no longer in characters.json — " +
            "removed its definition, clips, controller, atlas, and Addressables entries.");
    }

    /// <summary>
    /// Every character must have an "idle" animation, every animation's sheet PNG must exist,
    /// and its frameCount must fit the declared grid. Stops and logs at the first problem found
    /// — the importer never touches an asset until the whole catalog has passed this check.
    /// </summary>
    private static bool ValidateCatalog(List<JObject> characters)
    {
        foreach (JObject characterJson in characters)
        {
            string id = (string)characterJson["id"];
            if (string.IsNullOrEmpty(id))
            {
                Debug.LogError("CharacterImporter: a character entry has no \"id\" — aborting import, no assets touched.");
                return false;
            }
            if (!(characterJson["animations"] is JObject animations))
            {
                Debug.LogError($"CharacterImporter: character \"{id}\" has no \"animations\" object — aborting import, no assets touched.");
                return false;
            }
            if (animations["idle"] == null)
            {
                Debug.LogError($"CharacterImporter: character \"{id}\" has no \"idle\" animation — aborting import, no assets touched.");
                return false;
            }

            string folder = $"{CharactersRoot}/{id}";
            foreach (JProperty entry in animations.Properties())
            {
                string key = entry.Name;
                if (!(entry.Value is JObject animJson))
                {
                    Debug.LogError($"CharacterImporter: character \"{id}\" animation \"{key}\" is not an object — aborting import, no assets touched.");
                    return false;
                }

                string texturePath = $"{folder}/{key}.png";
                if (!File.Exists(texturePath))
                {
                    Debug.LogError(
                        $"CharacterImporter: character \"{id}\" animation \"{key}\": sheet not found at {texturePath} — aborting import, no assets touched.");
                    return false;
                }

                int frameWidth = ReadInt(animJson, "frameWidth");
                int frameHeight = ReadInt(animJson, "frameHeight");
                int columns = ReadInt(animJson, "columns");
                int rows = ReadInt(animJson, "rows");
                int frameCount = ReadInt(animJson, "frameCount");
                if (columns <= 0 || rows <= 0 || frameCount <= 0 || frameCount > columns * rows)
                {
                    Debug.LogError(
                        $"CharacterImporter: character \"{id}\" animation \"{key}\": frameCount {frameCount} must be " +
                        $"> 0 and <= columns*rows ({columns}*{rows}) — aborting import, no assets touched.");
                    return false;
                }

                if (AssetImporter.GetAtPath(texturePath) is TextureImporter textureImporter)
                {
                    textureImporter.GetSourceTextureWidthAndHeight(out int textureWidth, out int textureHeight);
                    int gridWidth = columns * frameWidth;
                    int gridHeight = rows * frameHeight;
                    if (gridWidth > textureWidth || gridHeight > textureHeight)
                    {
                        Debug.LogError(
                            $"CharacterImporter: character \"{id}\" animation \"{key}\": grid {columns}x{rows} at " +
                            $"{frameWidth}x{frameHeight} ({gridWidth}x{gridHeight}) exceeds the sheet's " +
                            $"{textureWidth}x{textureHeight} texture — aborting import, no assets touched.");
                        return false;
                    }
                }
            }
        }
        return true;
    }

    private static int ReadInt(JObject obj, string field) => (int?)obj[field] ?? 0;

    private static CharacterDefinition ImportCharacter(JObject characterJson)
    {
        string id = (string)characterJson["id"];
        string displayName = (string)characterJson["name"];
        var animations = (JObject)characterJson["animations"];
        string folder = $"{CharactersRoot}/{id}";

        // JSON key order, "idle" first regardless of where it sits in the source file, since
        // it must exist and become the controller's default state.
        var orderedKeys = new List<string> { "idle" };
        orderedKeys.AddRange(animations.Properties().Select(p => p.Name).Where(k => k != "idle"));

        var clipsByKey = new Dictionary<string, AnimationClip>();
        var builtKeys = new List<string>();
        Sprite idleFrame0 = null;

        foreach (string key in orderedKeys)
        {
            var animJson = (JObject)animations[key];
            int fps = ReadInt(animJson, "fps");
            int frameCount = ReadInt(animJson, "frameCount");
            bool loop = (bool?)animJson["loop"] ?? false;

            string texturePath = $"{folder}/{key}.png";
            Sprite[] sprites = LoadOrderedSprites(texturePath, key, frameCount);

            int missing = sprites.Count(s => s == null);
            if (missing > 0)
            {
                Debug.LogError(
                    $"CharacterImporter: character \"{id}\" animation \"{key}\": {missing} of {frameCount} sliced " +
                    "sprites failed to load — skipping this clip.");
                if (key == "idle")
                {
                    Debug.LogError($"CharacterImporter: character \"{id}\": idle clip could not be built — skipping this character.");
                    return null;
                }
                continue;
            }

            clipsByKey[key] = CreateClip(folder, key, fps, loop, sprites);
            builtKeys.Add(key);
            if (key == "idle") idleFrame0 = sprites[0];
        }

        AnimatorController controller = BuildController(folder, id, builtKeys, clipsByKey);
        List<string> actionNames = builtKeys.Where(k => k != "idle").ToList();

        return CreateOrUpdateDefinition(folder, id, displayName, controller, idleFrame0, actionNames);
    }

    private static Sprite[] LoadOrderedSprites(string texturePath, string action, int frameCount)
    {
        var sprites = new Sprite[frameCount];
        string prefix = action + "_";
        foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(texturePath))
        {
            if (asset is Sprite sprite && sprite.name.StartsWith(prefix))
            {
                if (int.TryParse(sprite.name.Substring(prefix.Length), out int frameIndex) && frameIndex < frameCount)
                {
                    sprites[frameIndex] = sprite;
                }
            }
        }
        return sprites;
    }

    private static AnimationClip CreateClip(string folder, string action, int fps, bool loop, Sprite[] orderedSprites)
    {
        var clip = new AnimationClip { frameRate = fps, name = action };
        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = loop;
        AnimationUtility.SetAnimationClipSettings(clip, settings);

        var binding = new EditorCurveBinding
        {
            type = typeof(SpriteRenderer),
            path = "",
            propertyName = "m_Sprite",
        };
        var keyframes = new ObjectReferenceKeyframe[orderedSprites.Length];
        for (int i = 0; i < orderedSprites.Length; i++)
        {
            keyframes[i] = new ObjectReferenceKeyframe { time = i / (float)fps, value = orderedSprites[i] };
        }
        AnimationUtility.SetObjectReferenceCurve(clip, binding, keyframes);

        string clipPath = $"{folder}/{action}.anim";
        var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
        if (existing != null)
        {
            // CopySerialized copies every serialized field, including m_Name — set on the source
            // clip above so the main object's name still matches the file after the copy.
            EditorUtility.CopySerialized(clip, existing);
            existing.name = action;
            EditorUtility.SetDirty(existing);
            return existing;
        }
        AssetDatabase.CreateAsset(clip, clipPath);
        return clip;
    }

    private static AnimatorController BuildController(
        string folder,
        string id,
        List<string> orderedKeys,
        Dictionary<string, AnimationClip> clipsByKey)
    {
        string controllerPath = $"{folder}/{id}.controller";
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
        if (controller == null)
        {
            controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
        }

        AnimatorControllerLayer layer = controller.layers[0];
        AnimatorStateMachine stateMachine = layer.stateMachine;

        // Reuse each existing state by name instead of clearing and rebuilding from scratch, so
        // a re-run with unchanged actions keeps the same state/transition fileIDs (and so
        // touches no bytes in the .controller) instead of only keeping behaviour the same.
        Dictionary<string, AnimatorState> existingStatesByKey = stateMachine.states
            .ToDictionary(child => child.state.name, child => child.state);

        foreach (ChildAnimatorState child in stateMachine.states.ToArray())
        {
            if (!orderedKeys.Contains(child.state.name)) stateMachine.RemoveState(child.state);
        }

        var statesByKey = new Dictionary<string, AnimatorState>();
        foreach (string key in orderedKeys)
        {
            AnimatorState state = existingStatesByKey.TryGetValue(key, out AnimatorState existing)
                ? existing
                : stateMachine.AddState(key);
            state.motion = clipsByKey[key];
            statesByKey[key] = state;
        }
        stateMachine.defaultState = statesByKey["idle"];

        foreach (string key in orderedKeys)
        {
            if (key == "idle") continue;
            AnimatorState state = statesByKey[key];

            // Remove any transition that no longer points at idle (e.g. idle itself was
            // recreated), and reuse the existing idle transition when there already is one.
            AnimatorStateTransition[] stale = state.transitions
                .Where(t => t.destinationState != statesByKey["idle"])
                .ToArray();
            foreach (AnimatorStateTransition t in stale) state.RemoveTransition(t);

            AnimatorStateTransition transition = state.transitions
                .FirstOrDefault(t => t.destinationState == statesByKey["idle"]);
            if (transition == null) transition = state.AddTransition(statesByKey["idle"]);
            transition.hasExitTime = true;
            transition.exitTime = 1.0f;
            transition.hasFixedDuration = false;
            transition.duration = 0f;
            transition.conditions = new AnimatorCondition[0];
        }

        EditorUtility.SetDirty(controller);
        return controller;
    }

    private static CharacterDefinition CreateOrUpdateDefinition(
        string folder,
        string id,
        string displayName,
        AnimatorController controller,
        Sprite thumbnail,
        List<string> actionNames)
    {
        string path = $"{folder}/{id}.asset";
        var definition = AssetDatabase.LoadAssetAtPath<CharacterDefinition>(path);
        if (definition == null)
        {
            definition = ScriptableObject.CreateInstance<CharacterDefinition>();
            AssetDatabase.CreateAsset(definition, path);
        }
        definition.EditorInit(id, displayName, controller, thumbnail, actionNames);
        EditorUtility.SetDirty(definition);
        return definition;
    }
}
#endif
