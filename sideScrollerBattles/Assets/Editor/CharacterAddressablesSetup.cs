#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.U2D;

/// <summary>
/// Makes each character's CharacterDefinition and its own Sprite Atlas V2 addressable, one
/// Addressables group per character (<c>Character_&lt;id&gt;</c>), packed together into a single
/// bundle — so selecting one character loads (and releases) only that character's data, instead
/// of the whole roster sharing one atlas and one resident set of pages. Called by
/// CharacterImporter.ImportSheets — re-running is idempotent: an entry already in the group with
/// the right address/label is left alone. <see cref="RemoveCharacterGroup"/> is the matching
/// teardown for a character pruned from characters.json.
/// </summary>
public static class CharacterAddressablesSetup
{
    private const string GroupNamePrefix = "Character_";
    private const string CharacterLabel = "character";
    private const string IosPlatform = "iPhone";
    private const int AtlasPadding = 4;
    private const int AtlasMaxTextureSize = 2048;

    /// <summary>Makes one character's definition and its per-character atlas addressable,
    /// together in that character's own group so they always ship (and load) in one bundle —
    /// the atlas tag is set to equal <paramref name="id"/>, which is also what
    /// AddressableSpriteAtlasBinder resolves by.</summary>
    public static AssetReferenceT<CharacterDefinition> MakeCharacterAddressable(
        string id, string definitionAssetPath, string characterFolder)
    {
        AddressableAssetSettings settings = GetOrCreateSettings();
        AddressableAssetGroup group = GetOrCreateCharacterGroup(settings, id);

        string definitionGuid = AssetDatabase.AssetPathToGUID(definitionAssetPath);
        AddressableAssetEntry definitionEntry = settings.CreateOrMoveEntry(definitionGuid, group, readOnly: false, postEvent: false);
        definitionEntry.address = $"character/{id}";
        settings.AddLabel(CharacterLabel);
        definitionEntry.SetLabel(CharacterLabel, true, true, false);

        SpriteAtlas atlas = EnsureCharacterAtlas(id, characterFolder);
        string atlasGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(atlas));
        AddressableAssetEntry atlasEntry = settings.CreateOrMoveEntry(atlasGuid, group, readOnly: false, postEvent: false);
        atlasEntry.address = atlas.tag;

        return new AssetReferenceT<CharacterDefinition>(definitionGuid);
    }

    /// <summary>Removes a pruned character's Addressables group (both its definition and atlas
    /// entries) entirely. Does not touch the underlying assets — CharacterImporter deletes those
    /// itself.</summary>
    public static void RemoveCharacterGroup(string id)
    {
        AddressableAssetSettings settings = GetOrCreateSettings();
        AddressableAssetGroup group = settings.FindGroup(GroupNamePrefix + id);
        if (group != null) settings.RemoveGroup(group);
    }

    /// <summary>Creates (or updates) the character's own Sprite Atlas V2 at
    /// <c>&lt;characterFolder&gt;/&lt;id&gt;.spriteatlasv2</c>, packing just that character's folder —
    /// one atlas per character, not the whole roster. Configured entirely through
    /// SpriteAtlasImporter: in Unity 6000.6, SpriteAtlasAsset's own Set* methods are obsolete and
    /// don't affect a V2 atlas's build settings, which is why an earlier attempt at per-character
    /// atlases here "didn't stick".</summary>
    private static SpriteAtlas EnsureCharacterAtlas(string id, string characterFolder)
    {
        string atlasPath = $"{characterFolder}/{id}.spriteatlasv2";
        var atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(atlasPath);
        if (atlas == null)
        {
            var atlasAsset = new SpriteAtlasAsset();
            var folderAsset = AssetDatabase.LoadAssetAtPath<DefaultAsset>(characterFolder);
            atlasAsset.Add(new Object[] { folderAsset });
            SpriteAtlasAsset.Save(atlasAsset, atlasPath);
            AssetDatabase.ImportAsset(atlasPath);
        }

        var importer = (SpriteAtlasImporter)AssetImporter.GetAtPath(atlasPath);
        importer.packingSettings = new SpriteAtlasPackingSettings
        {
            padding = AtlasPadding,
            enableRotation = false,
            enableTightPacking = true,
            enableAlphaDilation = true,
        };
        importer.textureSettings = new SpriteAtlasTextureSettings
        {
            // maxTextureSize here is read-only (the effective cap for the default platform);
            // the real cap for the platforms that matter is set per-platform below.
            anisoLevel = 1,
            filterMode = FilterMode.Bilinear,
            generateMipMaps = false,
            readable = false,
            sRGB = true,
        };

        TextureImporterPlatformSettings iosSettings = importer.GetPlatformSettings(IosPlatform)
            ?? new TextureImporterPlatformSettings { name = IosPlatform };
        iosSettings.overridden = true;
        iosSettings.format = TextureImporterFormat.ASTC_6x6;
        iosSettings.maxTextureSize = AtlasMaxTextureSize;
        importer.SetPlatformSettings(iosSettings);

        // Addressables now owns packaging this atlas; turning off "include in build" is also
        // what makes Unity's own SpriteAtlasManager stop finding it by itself, so
        // AddressableSpriteAtlasBinder's late-bound hook is what resolves it at runtime instead.
        importer.includeInBuild = false;
        importer.SaveAndReimport();

        return AssetDatabase.LoadAssetAtPath<SpriteAtlas>(atlasPath);
    }

    private static AddressableAssetSettings GetOrCreateSettings()
    {
        return AddressableAssetSettingsDefaultObject.Settings != null
            ? AddressableAssetSettingsDefaultObject.Settings
            : AddressableAssetSettingsDefaultObject.GetSettings(true);
    }

    private static AddressableAssetGroup GetOrCreateCharacterGroup(AddressableAssetSettings settings, string id)
    {
        string groupName = GroupNamePrefix + id;
        AddressableAssetGroup group = settings.FindGroup(groupName);
        if (group != null) return group;

        group = settings.CreateGroup(
            groupName,
            setAsDefaultGroup: false,
            readOnly: false,
            postEvent: false,
            schemasToCopy: null,
            typeof(BundledAssetGroupSchema),
            typeof(ContentUpdateGroupSchema));

        var bundleSchema = group.GetSchema<BundledAssetGroupSchema>();
        // This group holds only this character's definition and atlas, so PackTogether puts
        // both in the one bundle that loading character/<id> pulls in — a character loads (and
        // is released) independently of every other character's group.
        bundleSchema.BundleMode = BundledAssetGroupSchema.BundlePackingMode.PackTogether;
        bundleSchema.BuildPath.SetVariableByName(settings, AddressableAssetSettings.kLocalBuildPath);
        bundleSchema.LoadPath.SetVariableByName(settings, AddressableAssetSettings.kLocalLoadPath);

        return group;
    }
}
#endif
