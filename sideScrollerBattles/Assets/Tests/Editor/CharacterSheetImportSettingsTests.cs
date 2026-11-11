using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Pins per-sheet TextureImporter settings CharacterSheetPostprocessor is responsible for as
/// cell sizes grow past the fixed 256px/7x6 assumption: spritePixelsPerUnit must track the
/// sheet's own frameWidth from characters.json rather than a hardcoded constant, maxTextureSize
/// must be raised high enough that idle's 4608px-wide 9x9 grid of 512px cells isn't downsampled
/// on import, and sprites must import with a Tight mesh so the atlas packs silhouettes rather
/// than full cells. Restores characters.json in a finally block so a failed assertion never
/// leaves the repo's tracked JSON edited.
/// </summary>
public class CharacterSheetImportSettingsTests
{
    private const string CatalogJsonPath = "Assets/Characters/characters.json";
    private const string TexturePath = "Assets/Characters/knight/hurt.png";

    [Test]
    public void FrameWidthChangeInJsonDrivesSpritePixelsPerUnit()
    {
        // Targets specifically knight's "hurt" animation's frame grid: non-greedy matching walks
        // from the "knight" id to the first "hurt" object after it, then through frameWidth
        // (captured, to read the original PPU rather than hardcode it), frameHeight, columns,
        // rows and frameCount in the order they appear in that one object.
        var knightHurtGrid = new Regex(
            "(\"id\"\\s*:\\s*\"knight\"[\\s\\S]*?\"hurt\"\\s*:\\s*\\{[\\s\\S]*?\"frameWidth\"\\s*:\\s*)(\\d+)" +
            "([\\s\\S]*?\"frameHeight\"\\s*:\\s*)\\d+" +
            "([\\s\\S]*?\"columns\"\\s*:\\s*)\\d+" +
            "([\\s\\S]*?\"rows\"\\s*:\\s*)\\d+" +
            "([\\s\\S]*?\"frameCount\"\\s*:\\s*)\\d+");

        string originalJson = File.ReadAllText(CatalogJsonPath);
        Match originalMatch = knightHurtGrid.Match(originalJson);
        Assert.IsTrue(originalMatch.Success,
            "Could not find knight's \"hurt\" frame grid in characters.json — its shape may have changed.");
        float originalPixelsPerUnit = float.Parse(originalMatch.Groups[2].Value);

        var importerBefore = AssetImporter.GetAtPath(TexturePath) as TextureImporter;
        Assert.IsNotNull(importerBefore, $"{TexturePath} has no TextureImporter.");
        // The edited grid must fully tile the real sheet — read its actual size rather than
        // assuming one, since a mismatched grid would leave part of the sheet unsliced.
        importerBefore.GetSourceTextureWidthAndHeight(out int textureWidth, out int textureHeight);

        const int frameWidth = 128;
        int columns = textureWidth / frameWidth;
        int rows = textureHeight / frameWidth;
        int frameCount = columns * rows;

        try
        {
            string modifiedJson = knightHurtGrid.Replace(originalJson,
                $"${{1}}{frameWidth}${{3}}{frameWidth}${{4}}{columns}${{5}}{rows}${{6}}{frameCount}");
            Assert.AreNotEqual(originalJson, modifiedJson,
                "Could not find knight's \"hurt\" frame grid in characters.json — its shape may have changed.");

            File.WriteAllText(CatalogJsonPath, modifiedJson);
            AssetDatabase.ImportAsset(CatalogJsonPath, ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset(TexturePath, ImportAssetOptions.ForceUpdate);

            var importer = AssetImporter.GetAtPath(TexturePath) as TextureImporter;
            Assert.IsNotNull(importer, $"{TexturePath} has no TextureImporter.");
            Assert.AreEqual((float)frameWidth, importer.spritePixelsPerUnit,
                $"{TexturePath}'s spritePixelsPerUnit should follow characters.json's frameWidth ({frameWidth}), " +
                "not a fixed constant.");
        }
        finally
        {
            File.WriteAllText(CatalogJsonPath, originalJson);
            AssetDatabase.ImportAsset(CatalogJsonPath, ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset(TexturePath, ImportAssetOptions.ForceUpdate);
        }

        // Outside the finally: only reached if the edited-PPU assertion above passed, and
        // confirms reverting the JSON also reimports the importer back to its original PPU.
        var restoredImporter = AssetImporter.GetAtPath(TexturePath) as TextureImporter;
        Assert.AreEqual(originalPixelsPerUnit, restoredImporter.spritePixelsPerUnit,
            $"{TexturePath}'s spritePixelsPerUnit should revert to {originalPixelsPerUnit} once " +
            "characters.json is restored.");
    }

    [Test]
    public void ImportedSheetsAllowAtLeast8192MaxTextureSize()
    {
        AssetDatabase.ImportAsset(TexturePath, ImportAssetOptions.ForceUpdate);

        var importer = AssetImporter.GetAtPath(TexturePath) as TextureImporter;
        Assert.IsNotNull(importer, $"{TexturePath} has no TextureImporter.");
        Assert.GreaterOrEqual(importer.maxTextureSize, 8192,
            $"{TexturePath}'s maxTextureSize should be at least 8192 so idle's 4608px-wide " +
            "9x9 grid of 512px cells isn't downsampled on import.");

        // The default platform setting alone isn't enough — an overridden iOS platform setting
        // (every sheet's .meta currently carries one, at 2048 with ASTC compression) would still
        // shrink the sheet on the shipping platform regardless of what the default allows.
        TextureImporterPlatformSettings iosSettings = importer.GetPlatformTextureSettings("iPhone");
        Assert.IsTrue(!iosSettings.overridden || iosSettings.maxTextureSize >= 8192,
            $"{TexturePath}'s iOS platform override should either be removed or allow at least " +
            "8192, so idle's 4608px-wide 9x9 grid of 512px cells isn't downsampled on the " +
            "shipping platform.");
    }

    [Test]
    public void ImportedSheetsUseTightSpriteMeshType()
    {
        AssetDatabase.ImportAsset(TexturePath, ImportAssetOptions.ForceUpdate);

        var importer = AssetImporter.GetAtPath(TexturePath) as TextureImporter;
        Assert.IsNotNull(importer, $"{TexturePath} has no TextureImporter.");

        var settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        Assert.AreEqual(SpriteMeshType.Tight, settings.spriteMeshType,
            $"{TexturePath}'s sprites should import with a Tight mesh, not FullRect, so the " +
            "atlas packs silhouettes rather than full cells.");
    }
}
