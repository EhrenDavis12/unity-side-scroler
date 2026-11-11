using System.IO;
using UnityEditor;

public static class ReimportMatcherSheets
{
    public static string Run()
    {
        foreach (var name in new[] { "idle_v9.png", "fidget_glance_v2.png", "fidget_regrip_v2.png" })
        {
            string assetPath = "Assets/Characters/knight_versions/" + name;
            string metaPath = assetPath + ".meta";
            if (File.Exists(metaPath)) File.Delete(metaPath);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
        }
        return "reimported";
    }
}
