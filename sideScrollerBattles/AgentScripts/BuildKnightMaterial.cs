using System.Linq;
using UnityEditor;
using UnityEngine;

public static class BuildKnightMaterial
{
    private const string MaterialPath = "Assets/Characters/knight_3d/KnightToon.mat";

    public static string Run()
    {
        var shader = Shader.Find("Showcase/ToonOutline");
        if (shader == null) return "FAILED: shader not found";

        if (ShaderUtil.ShaderHasError(shader)) return "FAILED: shader has compile errors (see Console)";

        var texture = AssetDatabase.LoadAllAssetsAtPath("Assets/Characters/knight_3d/knight_rigged.glb")
            .OfType<Texture2D>().FirstOrDefault();

        var material = new Material(shader) { name = "KnightToon" };
        if (texture != null) material.SetTexture("_BaseMap", texture);

        AssetDatabase.CreateAsset(material, MaterialPath);
        AssetDatabase.SaveAssets();
        return $"OK: material saved to {MaterialPath}, texture={(texture == null ? "NONE" : texture.name)}";
    }
}
