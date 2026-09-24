using System.Linq;
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
    private const int ExpectedCharacterCount = 6;
    private const int ExpectedStateCount = 4; // idle, attack, defend, hurt
    private const int ExpectedFrameCount = 38;

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
    public void CatalogHasSixEntriesWithValidAssetReferences()
    {
        CharacterCatalog catalog = LoadCatalog();
        Assert.AreEqual(ExpectedCharacterCount, catalog.Characters.Count);
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
    public void EveryClipHas38SpriteKeysAndOnlyIdleLoops()
    {
        CharacterCatalog catalog = LoadCatalog();
        foreach (CharacterCatalogEntry entry in catalog.Characters)
        {
            CharacterDefinition definition = LoadDefinition(entry);
            var controller = definition.Controller as AnimatorController;

            foreach (ChildAnimatorState child in controller.layers[0].stateMachine.states)
            {
                var clip = child.state.motion as AnimationClip;
                Assert.IsNotNull(clip, $"\"{entry.id}\"'s state \"{child.state.name}\" has no AnimationClip.");

                EditorCurveBinding binding = AnimationUtility.GetObjectReferenceCurveBindings(clip).First();
                ObjectReferenceKeyframe[] keys = AnimationUtility.GetObjectReferenceCurve(clip, binding);
                Assert.AreEqual(ExpectedFrameCount, keys.Length, $"\"{entry.id}\"'s \"{child.state.name}\" clip should have {ExpectedFrameCount} sprite keys.");

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
}
