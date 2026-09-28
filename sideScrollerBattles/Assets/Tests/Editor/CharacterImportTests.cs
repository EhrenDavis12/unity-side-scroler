using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Verifies Characters/Import Sheets' output: the catalog's shape, each character's controller,
/// and each clip's sprite keys/looping. Runs the import itself in setup so the assertions
/// always check freshly-generated assets rather than whatever happened to be on disk.
/// </summary>
public class CharacterImportTests
{
    private const string CatalogAssetPath = "Assets/Characters/CharacterCatalog.asset";
    private const string CharactersJsonPath = "Assets/Characters/characters.json";
    private const int ExpectedStateCount = 4; // idle, attack, defend, hurt

    [OneTimeSetUp]
    public void ImportSheets()
    {
        EditorApplication.ExecuteMenuItem("Characters/Import Sheets");
    }

    private static CharacterCatalog LoadCatalog()
    {
        var catalog = AssetDatabase.LoadAssetAtPath<CharacterCatalog>(CatalogAssetPath);
        Assert.IsNotNull(catalog, "CharacterCatalog asset not found at " + CatalogAssetPath);
        return catalog;
    }

    private static CharacterDefinition LoadDefinition(CharacterCatalogEntry entry)
    {
        string guid = entry.definition.AssetGUID;
        Assert.IsFalse(string.IsNullOrEmpty(guid), $"\"{entry.id}\" has no AssetReference GUID.");
        string path = AssetDatabase.GUIDToAssetPath(guid);
        Assert.IsFalse(string.IsNullOrEmpty(path), $"\"{entry.id}\"'s AssetReference GUID doesn't resolve to an asset path.");
        var definition = AssetDatabase.LoadAssetAtPath<CharacterDefinition>(path);
        Assert.IsNotNull(definition, $"\"{entry.id}\"'s AssetReference doesn't resolve to a CharacterDefinition.");
        return definition;
    }

    [Test]
    public void CatalogEntryCountMatchesCharactersJsonWithValidAssetReferences()
    {
        var charactersJson = JObject.Parse(File.ReadAllText(CharactersJsonPath));
        var characters = charactersJson["characters"] as JArray;
        Assert.IsNotNull(characters, "characters.json has no \"characters\" array.");

        CharacterCatalog catalog = LoadCatalog();
        Assert.AreEqual(characters.Count, catalog.Characters.Count,
            "CharacterCatalog's entry count should match characters.json's character count, " +
            "not a fixed roster size.");
        foreach (CharacterCatalogEntry entry in catalog.Characters) LoadDefinition(entry);
    }

    [Test]
    public void EveryDefinitionsControllerHasFourStatesWithIdleDefault()
    {
        CharacterCatalog catalog = LoadCatalog();
        foreach (CharacterCatalogEntry entry in catalog.Characters)
        {
            CharacterDefinition definition = LoadDefinition(entry);
            var controller = definition.Controller as AnimatorController;
            Assert.IsNotNull(controller, $"\"{entry.id}\" has no AnimatorController.");

            AnimatorStateMachine stateMachine = controller.layers[0].stateMachine;
            Assert.AreEqual(ExpectedStateCount, stateMachine.states.Length, $"\"{entry.id}\"'s controller should have {ExpectedStateCount} states.");
            Assert.AreEqual("idle", stateMachine.defaultState.name, $"\"{entry.id}\"'s controller default state should be idle.");
        }
    }

    [Test]
    public void EveryClipsSpriteKeyCountMatchesCharactersJsonFrameCountAndOnlyIdleLoops()
    {
        var charactersJson = JObject.Parse(File.ReadAllText(CharactersJsonPath));
        CharacterCatalog catalog = LoadCatalog();
        foreach (CharacterCatalogEntry entry in catalog.Characters)
        {
            CharacterDefinition definition = LoadDefinition(entry);
            var controller = definition.Controller as AnimatorController;

            foreach (ChildAnimatorState child in controller.layers[0].stateMachine.states)
            {
                var clip = child.state.motion as AnimationClip;
                Assert.IsNotNull(clip, $"\"{entry.id}\"'s state \"{child.state.name}\" has no AnimationClip.");

                int expectedFrameCount = ReadFrameCountFromJson(charactersJson, entry.id, child.state.name);

                EditorCurveBinding binding = AnimationUtility.GetObjectReferenceCurveBindings(clip).First();
                ObjectReferenceKeyframe[] keys = AnimationUtility.GetObjectReferenceCurve(clip, binding);
                Assert.AreEqual(expectedFrameCount, keys.Length,
                    $"\"{entry.id}\"'s \"{child.state.name}\" clip should have {expectedFrameCount} sprite keys " +
                    "— characters.json's per-action frameCount, not a fixed count shared by every action.");

                if (child.state.name == "idle")
                {
                    Assert.IsTrue(clip.isLooping, $"\"{entry.id}\"'s idle clip should loop.");
                }
                else
                {
                    Assert.IsFalse(clip.isLooping, $"\"{entry.id}\"'s \"{child.state.name}\" clip should not loop.");
                }
            }
        }
    }

    // Mirrors CharacterSheetPostprocessor.TryReadFrameGrid's lookup, but reads only frameCount:
    // the PRD-level spec is that each action's sprite-key count comes from characters.json, not
    // from a single grid shared by every action (idle is 75 frames in a 9x9 grid; other actions
    // may have fewer), so this test must not assume a uniform frame grid the way the importer's
    // own fallback does.
    private static int ReadFrameCountFromJson(JObject charactersJson, string id, string action)
    {
        var characters = charactersJson["characters"] as JArray;
        Assert.IsNotNull(characters, "characters.json has no \"characters\" array.");
        JObject characterJson = characters.OfType<JObject>().FirstOrDefault(c => (string)c["id"] == id);
        Assert.IsNotNull(characterJson, $"characters.json has no entry for \"{id}\".");
        var animation = characterJson["animations"]?[action] as JObject;
        Assert.IsNotNull(animation, $"characters.json's \"{id}\" has no \"{action}\" animation entry.");
        int? frameCount = (int?)animation["frameCount"];
        Assert.IsTrue(frameCount.HasValue && frameCount.Value > 0,
            $"characters.json's \"{id}\"/\"{action}\" frameCount should be a positive integer.");
        return frameCount.Value;
    }
}
