using UnityEditor;
public static class ImportKnight3D
{
    public static string Run()
    {
        AssetDatabase.ImportAsset("Assets/Characters/knight_3d/knight_rigged.glb", ImportAssetOptions.ForceUpdate);
        AssetDatabase.ImportAsset("Assets/Characters/knight_3d/shield.glb", ImportAssetOptions.ForceUpdate);
        AssetDatabase.ImportAsset("Assets/Characters/knight_3d/motion_idle.fbx", ImportAssetOptions.ForceUpdate);
        AssetDatabase.ImportAsset("Assets/Characters/knight_3d/motion_attack.fbx", ImportAssetOptions.ForceUpdate);
        AssetDatabase.ImportAsset("Assets/Characters/knight_3d/apose.png", ImportAssetOptions.ForceUpdate);
        return "imported";
    }
}
