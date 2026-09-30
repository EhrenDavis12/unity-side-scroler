using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class BuildShowcaseB
{
    public static string Run()
    {
        Scene scene = EditorSceneManager.OpenScene("Assets/Scenes/KnightShowcases.unity", OpenSceneMode.Single);

        GameObject placeholder = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "Showcase B - 3D in 2D (placeholder)");
        if (placeholder == null) return "FAILED: placeholder not found";

        // Re-running this builder (e.g. after a shader fix) replaces its own prior output rather
        // than duplicating it.
        for (int i = placeholder.transform.childCount - 1; i >= 0; i--)
        {
            Object.DestroyImmediate(placeholder.transform.GetChild(i).gameObject);
        }

        var knightPrefabRoot = AssetDatabase.LoadMainAssetAtPath("Assets/Characters/knight_3d/knight_rigged.glb") as GameObject;
        var avatar = AssetDatabase.LoadAssetAtPath<Avatar>("Assets/Characters/knight_3d/KnightAvatar.asset");
        var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/Characters/knight_3d/KnightAnimator.controller");
        var toonShader = Shader.Find("Showcase/ToonOutline");
        var knightMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Characters/knight_3d/KnightToon.mat");
        // The custom toon+outline shader above renders correctly on ordinary MeshRenderers (the
        // shield, the procedural sword) but not on this project's SkinnedMeshRenderer — see the
        // dispatch report for the investigation. The knight's own body uses a flattened standard
        // Lit material instead (same texture, zero smoothness/specular/reflections) so it still
        // reads flatter than a normally-lit model, without silently failing to render at all.
        var knightBodyMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Characters/knight_3d/KnightBodyFlat.mat");
        var shieldPrefabRoot = AssetDatabase.LoadMainAssetAtPath("Assets/Characters/knight_3d/shield.glb") as GameObject;

        if (knightPrefabRoot == null || avatar == null || controller == null || knightMaterial == null ||
            knightBodyMaterial == null || shieldPrefabRoot == null)
        {
            return "FAILED: missing one of knightPrefabRoot/avatar/controller/knightMaterial/knightBodyMaterial/shieldPrefabRoot";
        }

        // ---- Knight instance ----
        var knight = (GameObject)PrefabUtility.InstantiatePrefab(knightPrefabRoot);
        knight.transform.SetParent(placeholder.transform, false);
        knight.name = "Knight";
        // Three-quarter yaw facing screen-right (enemies come from the right — see the dispatch
        // note). The glb's own forward is +Z-ish toward what ends up screen-left at yaw 0, so a
        // positive yaw here (not a small negative one) is what actually turns the face toward camera.
        knight.transform.localRotation = Quaternion.Euler(0f, 145f, 0f);
        // The knight's own root already carries a 0.01 import scale (see InspectKnightBounds),
        // giving a ~1.2m-tall character — noticeably smaller on screen than Showcase A's sprite
        // under the same camera, so scale the whole placeholder up to match that framing.
        placeholder.transform.localScale = Vector3.one * 3.5f;
        placeholder.transform.localPosition = new Vector3(0f, -0.8f, 0f);

        if (GameObject.Find("Directional Light") == null)
        {
            var lightGo = new GameObject("Directional Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = Color.white;
            light.intensity = 1.8f;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }

        var animator = knight.AddComponent<Animator>();
        animator.avatar = avatar;
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;
        knight.AddComponent<KnightAttackTrigger>();

        var smr = knight.GetComponentInChildren<SkinnedMeshRenderer>();
        if (smr != null) smr.sharedMaterial = knightBodyMaterial;

        Transform FindDeep(Transform root, string boneName)
        {
            if (root.name == boneName) return root;
            foreach (Transform child in root)
            {
                Transform found = FindDeep(child, boneName);
                if (found != null) return found;
            }
            return null;
        }

        Transform leftHand = FindDeep(knight.transform, "mixamorig:LeftHand");
        Transform rightHand = FindDeep(knight.transform, "mixamorig:RightHand");
        if (leftHand == null || rightHand == null) return "FAILED: hand bones not found on instantiated knight";

        // ---- Shield: parented to the left hand, facing outward, sized to match the knight ----
        var shield = (GameObject)PrefabUtility.InstantiatePrefab(shieldPrefabRoot);
        shield.name = "Shield";
        shield.transform.SetParent(leftHand, false);
        shield.transform.localPosition = new Vector3(0f, -0.05f, 0.05f);
        shield.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
        shield.transform.localScale = Vector3.one * 0.55f;
        foreach (var renderer in shield.GetComponentsInChildren<MeshRenderer>())
        {
            renderer.sharedMaterial = knightMaterial;
        }

        // ---- Procedural longsword: parented to the right hand ----
        GameObject sword = BuildSword(toonShader);
        sword.transform.SetParent(rightHand, false);
        sword.transform.localPosition = new Vector3(0f, 0.02f, 0f);
        sword.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        return "OK: Showcase B built";
    }

    /// <summary>Blade, crossguard, grip and pommel from primitives, sized so the grip sits in the
    /// fist when parented to the right hand bone with the blade pointing along +Y (local up).
    /// </summary>
    private static GameObject BuildSword(Shader toonShader)
    {
        var silver = new Material(toonShader) { name = "SwordSilver" };
        silver.SetColor("_BaseColor", new Color(0.75f, 0.78f, 0.82f));
        var leather = new Material(toonShader) { name = "SwordLeather" };
        leather.SetColor("_BaseColor", new Color(0.25f, 0.16f, 0.1f));

        var root = new GameObject("Sword");

        GameObject MakePart(string name, PrimitiveType type, Vector3 localPos, Vector3 localScale, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = localScale;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        // Grip at the origin (this is what sits inside the fist), blade extending up (+Y) from
        // the crossguard, pommel below the grip.
        MakePart("Grip", PrimitiveType.Cylinder, new Vector3(0f, 0.075f, 0f), new Vector3(0.02f, 0.075f, 0.02f), leather);
        MakePart("Pommel", PrimitiveType.Sphere, new Vector3(0f, -0.01f, 0f), new Vector3(0.035f, 0.035f, 0.035f), silver);
        MakePart("Crossguard", PrimitiveType.Cube, new Vector3(0f, 0.155f, 0f), new Vector3(0.16f, 0.015f, 0.03f), silver);
        MakePart("Blade", PrimitiveType.Cube, new Vector3(0f, 0.46f, 0f), new Vector3(0.035f, 0.6f, 0.012f), silver);

        return root;
    }
}
