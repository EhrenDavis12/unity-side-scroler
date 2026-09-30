using System.Linq;
using UnityEditor;
using UnityEngine;

public static class BuildKnightBodyMaterial
{
    private const string MaterialPath = "Assets/Characters/knight_3d/KnightBodyFlat.mat";

    public static string Run()
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        var texture = AssetDatabase.LoadAllAssetsAtPath("Assets/Characters/knight_3d/knight_rigged.glb")
            .OfType<Texture2D>().FirstOrDefault();

        Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null)
        {
            material = new Material(shader) { name = "KnightBodyFlat" };
            AssetDatabase.CreateAsset(material, MaterialPath);
        }
        material.SetTexture("_BaseMap", texture);
        material.SetFloat("_Smoothness", 0f);
        material.SetFloat("_SpecularHighlights", 0f);
        material.SetFloat("_EnvironmentReflections", 0f);
        EditorUtility.SetDirty(material);
        AssetDatabase.SaveAssets();
        return $"OK tex={(texture != null ? texture.name : "NULL")}";
    }
}
