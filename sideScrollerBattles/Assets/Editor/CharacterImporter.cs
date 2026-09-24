#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.U2D.Sprites;
using UnityEngine;

/// <summary>
/// Imports the showcase character sheets under Assets/Characters into sliced sprites, per-action
/// AnimationClips, one AnimatorController per character, and a CharacterDefinition +
/// CharacterCatalog pair — all driven from Assets/Characters/characters.json, the same contract
/// the Flutter demo reads. Re-running replaces each generated asset's contents in place (reusing
/// existing sprite IDs and clip names), so a re-run with unchanged input touches nothing on disk.
/// </summary>
public static class CharacterImporter
{
    private const string CharactersRoot = "Assets/Characters";
    private const string CatalogJsonPath = CharactersRoot + "/characters.json";
    private const string CatalogAssetPath = CharactersRoot + "/CharacterCatalog.asset";
    private const int PixelsPerUnit = 256;
    private const int MaxTextureSize = 2048;

    [MenuItem("Characters/Import Sheets")]
    public static void ImportSheets()
    {
        string json = File.ReadAllText(CatalogJsonPath);
        var root = (JsonObject)MiniJson.Deserialize(json);
        if (!root.TryGetValue("characters", out object charactersRaw) || !(charactersRaw is List<object> charactersList))
        {
            Debug.LogError("CharacterImporter: characters.json has no \"characters\" array — aborting, no assets touched.");
            return;
        }
        var characters = charactersList.Cast<JsonObject>().ToList();

        // Validate everything before mutating anything: a bad entry partway through the JSON
        // must never leave some characters imported and others silently skipped.
        if (!ValidateCatalog(characters)) return;

        var definitions = new List<CharacterDefinition>();
        foreach (JsonObject characterJson in characters)
        {
            CharacterDefinition definition = ImportCharacter(characterJson);
            if (definition != null) definitions.Add(definition);
        }

        CharacterCatalog catalog = AssetDatabase.LoadAssetAtPath<CharacterCatalog>(CatalogAssetPath);
        if (catalog == null)
        {
            catalog = ScriptableObject.CreateInstance<CharacterCatalog>();
            AssetDatabase.CreateAsset(catalog, CatalogAssetPath);
        }
        catalog.EditorInit(definitions);
        EditorUtility.SetDirty(catalog);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"Characters/Import Sheets: imported {definitions.Count} character(s).");
    }

    /// <summary>
    /// Every character must have an "idle" animation, every animation's sheet PNG must exist,
    /// and its frameCount must fit the declared grid. Stops and logs at the first problem found
    /// — the importer never touches an asset until the whole catalog has passed this check.
    /// </summary>
    private static bool ValidateCatalog(List<JsonObject> characters)
    {
        foreach (JsonObject characterJson in characters)
        {
            string id = characterJson.TryGetValue("id", out object idObj) ? idObj as string : null;
            if (string.IsNullOrEmpty(id))
            {
                Debug.LogError("CharacterImporter: a character entry has no \"id\" — aborting import, no assets touched.");
                return false;
            }
            if (!characterJson.TryGetValue("animations", out object animsObj) || !(animsObj is JsonObject animations))
            {
                Debug.LogError($"CharacterImporter: character \"{id}\" has no \"animations\" object — aborting import, no assets touched.");
                return false;
            }
            if (!animations.ContainsKey("idle"))
            {
                Debug.LogError($"CharacterImporter: character \"{id}\" has no \"idle\" animation — aborting import, no assets touched.");
                return false;
            }

            string folder = $"{CharactersRoot}/{id}";
            foreach (KeyValuePair<string, object> entry in animations)
            {
                string key = entry.Key;
                if (!(entry.Value is JsonObject animJson))
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
            }
        }
        return true;
    }

    private static int ReadInt(JsonObject obj, string field) =>
        obj.TryGetValue(field, out object value) && value is double d ? (int)d : 0;

    private static CharacterDefinition ImportCharacter(JsonObject characterJson)
    {
        string id = (string)characterJson["id"];
        string displayName = (string)characterJson["name"];
        var animations = (JsonObject)characterJson["animations"];
        string folder = $"{CharactersRoot}/{id}";

        // JSON key order, "idle" first regardless of where it sits in the source file, since
        // it must exist and become the controller's default state.
        var orderedKeys = new List<string> { "idle" };
        orderedKeys.AddRange(animations.Keys.Where(k => k != "idle"));

        var clipsByKey = new Dictionary<string, AnimationClip>();
        var builtKeys = new List<string>();
        Sprite idleFrame0 = null;

        foreach (string key in orderedKeys)
        {
            var animJson = (JsonObject)animations[key];
            int frameWidth = ReadInt(animJson, "frameWidth");
            int frameHeight = ReadInt(animJson, "frameHeight");
            int columns = ReadInt(animJson, "columns");
            int rows = ReadInt(animJson, "rows");
            int frameCount = ReadInt(animJson, "frameCount");
            int fps = ReadInt(animJson, "fps");
            bool loop = animJson.TryGetValue("loop", out object loopObj) && loopObj is bool b && b;

            string texturePath = $"{folder}/{key}.png";
            SliceSheet(texturePath, key, frameWidth, frameHeight, columns, rows, frameCount);
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

    private static void SliceSheet(
        string texturePath,
        string action,
        int frameWidth,
        int frameHeight,
        int columns,
        int rows,
        int frameCount)
    {
        var importer = (TextureImporter)AssetImporter.GetAtPath(texturePath);
        if (importer == null)
        {
            Debug.LogError($"CharacterImporter: no texture importer at {texturePath}");
            return;
        }

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Multiple;
        importer.spritePixelsPerUnit = PixelsPerUnit;
        importer.filterMode = FilterMode.Bilinear;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
        importer.maxTextureSize = MaxTextureSize;

        var settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteMeshType = SpriteMeshType.FullRect;
        importer.SetTextureSettings(settings);
        importer.SaveAndReimport();

        var factory = new SpriteDataProviderFactories();
        factory.Init();
        ISpriteEditorDataProvider dataProvider = factory.GetSpriteEditorDataProviderFromObject(importer);
        dataProvider.InitSpriteEditorDataProvider();

        // Reuse each existing rect's spriteID by name, so a re-run with unchanged geometry keeps
        // the same sprite GUIDs (and so touches no .meta bytes) instead of rewriting all of them.
        Dictionary<string, GUID> existingIdsByName = dataProvider.GetSpriteRects()
            .ToDictionary(r => r.name, r => r.spriteID);

        var spriteRects = new List<SpriteRect>(frameCount);
        for (int i = 0; i < frameCount; i++)
        {
            int col = i % columns;
            int row = i / columns;
            // Row-major, top row first in the sheet; texture space has y=0 at the bottom, so
            // the top sheet row (row 0) sits at the highest y.
            float x = col * frameWidth;
            float y = (rows - 1 - row) * frameHeight;
            string name = $"{action}_{i:D2}";

            var spriteRect = new SpriteRect
            {
                name = name,
                spriteID = existingIdsByName.TryGetValue(name, out GUID existingId) ? existingId : GUID.Generate(),
                rect = new Rect(x, y, frameWidth, frameHeight),
                alignment = SpriteAlignment.Custom,
                pivot = new Vector2(0.5f, 0f), // bottom-centre: feet anchoring
            };
            spriteRects.Add(spriteRect);
        }
        dataProvider.SetSpriteRects(spriteRects.ToArray());

        var nameFileIdDataProvider = dataProvider.GetDataProvider<ISpriteNameFileIdDataProvider>();
        var nameFileIdPairs = spriteRects.Select(r => new SpriteNameFileIdPair(r.name, r.spriteID)).ToList();
        nameFileIdDataProvider.SetNameFileIdPairs(nameFileIdPairs);

        dataProvider.Apply();
        importer.SaveAndReimport();
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

        // Rebuild the state machine from scratch each run so re-imports stay idempotent even
        // when an action was renamed or removed.
        foreach (ChildAnimatorState child in stateMachine.states.ToArray())
        {
            stateMachine.RemoveState(child.state);
        }

        var statesByKey = new Dictionary<string, AnimatorState>();
        foreach (string key in orderedKeys)
        {
            AnimatorState state = stateMachine.AddState(key);
            state.motion = clipsByKey[key];
            statesByKey[key] = state;
        }
        stateMachine.defaultState = statesByKey["idle"];

        foreach (string key in orderedKeys)
        {
            if (key == "idle") continue;
            AnimatorStateTransition transition = statesByKey[key].AddTransition(statesByKey["idle"]);
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
