using UnityEditor;
using UnityEngine;

public static class ConfigureMotionFbx
{
    public static string Run()
    {
        string result = "";
        result += Configure("Assets/Characters/knight_3d/motion_idle.fbx", loop: true) + "\n";
        result += Configure("Assets/Characters/knight_3d/motion_attack.fbx", loop: false);
        return result;
    }

    private static string Configure(string path, bool loop)
    {
        var importer = (ModelImporter)AssetImporter.GetAtPath(path);
        if (importer == null) return $"{path}: FAILED no importer";

        importer.animationType = ModelImporterAnimationType.Human;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;

        ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
        for (int i = 0; i < clips.Length; i++)
        {
            clips[i].loopTime = loop;
            clips[i].lockRootPositionXZ = true;
            clips[i].lockRootHeightY = true;
            clips[i].keepOriginalPositionXZ = false;
        }
        importer.clipAnimations = clips;

        importer.SaveAndReimport();

        var allAssets = AssetDatabase.LoadAllAssetsAtPath(path);
        Avatar avatar = System.Array.Find(allAssets, o => o is Avatar) as Avatar;
        bool humanoidValid = avatar != null && avatar.isValid && avatar.isHuman;
        return $"{path}: humanoidAvatarValid={humanoidValid} clipCount={clips.Length}";
    }
}
