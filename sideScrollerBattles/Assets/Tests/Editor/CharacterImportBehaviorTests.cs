using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Pins two promises about Characters/Import Sheets that CharacterImportTests' OneTimeSetUp
/// doesn't check on its own: that the menu item reports success rather than being fired and
/// ignored, and that editing characters.json alone — no PNG touched on disk — reaches the
/// generated sprites and clip through CharacterSheetPostprocessor's DependsOnSourceAsset
/// declaration. Each test that mutates characters.json restores it in a finally block so a
/// failed assertion still leaves the repo's tracked JSON untouched.
/// </summary>
public class CharacterImportBehaviorTests
{
    private const string CatalogJsonPath = "Assets/Characters/characters.json";

    [Test]
    public void ImportSheetsMenuItemReportsSuccess()
    {
        bool executed = EditorApplication.ExecuteMenuItem("Characters/Import Sheets");
        Assert.IsTrue(executed, "Characters/Import Sheets should report success — a renamed or " +
            "missing menu item must fail this test rather than silently no-op.");
    }

    [Test]
    public void FrameCountChangeInJsonReachesImportedSprites()
    {
        const string texturePath = "Assets/Characters/archer/hurt.png";
        const string clipPath = "Assets/Characters/archer/hurt.anim";
        // Targets specifically archer's "hurt" animation's frameCount: non-greedy matching walks
        // from the "archer" id to the first "hurt" object after it, then to the first
        // "frameCount" inside that object — the only one "hurt" itself declares.
        var archerHurtFrameCount = new Regex(
            "(\"id\"\\s*:\\s*\"archer\"[\\s\\S]*?\"hurt\"\\s*:\\s*\\{[\\s\\S]*?\"frameCount\"\\s*:\\s*)\\d+");

        string originalJson = File.ReadAllText(CatalogJsonPath);
        try
        {
            string modifiedJson = archerHurtFrameCount.Replace(originalJson, "${1}37");
            Assert.AreNotEqual(originalJson, modifiedJson,
                "Could not find archer's \"hurt\" frameCount in characters.json — its shape may have changed.");

            File.WriteAllText(CatalogJsonPath, modifiedJson);
            AssetDatabase.ImportAsset(CatalogJsonPath, ImportAssetOptions.ForceUpdate);
            EditorApplication.ExecuteMenuItem("Characters/Import Sheets");

            AssertSpritesAndClipKeys(texturePath, clipPath, 37);
        }
        finally
        {
            File.WriteAllText(CatalogJsonPath, originalJson);
            AssetDatabase.ImportAsset(CatalogJsonPath, ImportAssetOptions.ForceUpdate);
            EditorApplication.ExecuteMenuItem("Characters/Import Sheets");
        }

        // Outside the finally: only reached if the 37-frame assertions above passed, and
        // confirms the round trip back to the original 38 frames also reimports correctly —
        // shrink-then-regrow assigns the recreated sprite a new GUID, so this checks counts,
        // never a byte-identical file.
        AssertSpritesAndClipKeys(texturePath, clipPath, 38);
    }

    private static void AssertSpritesAndClipKeys(string texturePath, string clipPath, int expectedFrameCount)
    {
        int spriteCount = AssetDatabase.LoadAllAssetsAtPath(texturePath).OfType<Sprite>().Count();
        Assert.AreEqual(expectedFrameCount, spriteCount,
            $"{texturePath} should slice into {expectedFrameCount} sprite sub-assets.");

        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
        Assert.IsNotNull(clip, $"{clipPath} not found.");
        EditorCurveBinding binding = AnimationUtility.GetObjectReferenceCurveBindings(clip).First();
        ObjectReferenceKeyframe[] keys = AnimationUtility.GetObjectReferenceCurve(clip, binding);
        Assert.AreEqual(expectedFrameCount, keys.Length,
            $"{clipPath} should have {expectedFrameCount} sprite keys.");
    }
}
