using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Annie;
using Annie.BattleScene1;
using Annie.BattleScene1GroundBlend;

// Builder for the BattleScene1GroundBlend job (a copy of approved battle scene 1 whose L11 ground is lerp(grass, soil, mask)).
//   unity command run_script --file AnnieBuild/BattleScene1GroundBlend.cs --entry GroundBlendBuild.Build
//   unity command run_script --file AnnieBuild/BattleScene1GroundBlend.cs --entry GroundBlendBuild.Record --args '["name|approved or blend|Off/M1/M2/M3/GrassOnly|frames|outDir"]'
public static class GroundBlendBuild
{
    const string Job = "Assets/Annie/BattleScene1GroundBlend/";
    const string Approved = "Assets/Annie/BattleScene1/Scenes/BattleScene1.unity";
    const string SoilPath = "Assets/Annie/BattleScene1/Textures/L11_ground.png";
    const string ScenePath = Job + "Scenes/BattleScene1GroundBlend.unity";
    const string Src = "../../asset-animation-demo/local_runs/scenes/battle1/ground_blend/";

    static void Import(string path, bool mask)
    {
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        ti.textureType = TextureImporterType.Default; ti.sRGBTexture = !mask;
        ti.alphaSource = TextureImporterAlphaSource.None; ti.alphaIsTransparency = false;
        ti.mipmapEnabled = false; ti.wrapMode = TextureWrapMode.Repeat; ti.filterMode = FilterMode.Bilinear;
        ti.textureCompression = TextureImporterCompression.Uncompressed; ti.npotScale = TextureImporterNPOTScale.None;
        ti.maxTextureSize = 8192; ti.isReadable = false;
        var ps = ti.GetDefaultPlatformTextureSettings(); ps.maxTextureSize = 8192; ps.textureCompression = TextureImporterCompression.Uncompressed; ps.format = TextureImporterFormat.RGBA32; ti.SetPlatformTextureSettings(ps);
        ti.SaveAndReimport();
    }

    public static string Build(string a)
    {
        Directory.CreateDirectory(Job + "Textures"); Directory.CreateDirectory(Job + "Materials"); Directory.CreateDirectory(Job + "Scenes");
        var files = new[] { ("grass/G_s12_v2.png", "G_grass.png", false), ("masks/M1_s111.png", "M1_patches.png", true), ("masks/M2_s102_v2.png", "M2_bleed.png", true), ("masks/M3_s106.png", "M3_mostly_grass.png", true) };
        foreach (var f in files) { File.Copy(Src + f.Item1, Job + "Textures/" + f.Item2, true); }
        AssetDatabase.Refresh();
        foreach (var f in files) Import(Job + "Textures/" + f.Item2, f.Item3);
        Func<string, Texture2D> T = n => AssetDatabase.LoadAssetAtPath<Texture2D>(Job + "Textures/" + n);

        string mp = Job + "Materials/Mat_L11_ground_blend.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(mp);
        if (mat == null) { mat = new Material(Shader.Find("Annie/GbMix")); AssetDatabase.CreateAsset(mat, mp); }
        mat.shader = Shader.Find("Annie/GbMix");
        mat.SetTexture("_SoilTex", AssetDatabase.LoadAssetAtPath<Texture2D>(SoilPath));
        mat.SetTexture("_GrassTex", T("G_grass.png")); mat.SetTexture("_MaskTex", T("M1_patches.png"));
        mat.SetFloat("_MaskMul", 0); mat.SetFloat("_MaskAdd", 1); mat.SetFloat("_LaneW", 3840); mat.SetFloat("_Scroll", 1);
        EditorUtility.SetDirty(mat); AssetDatabase.SaveAssets();

        if (File.Exists(ScenePath)) AssetDatabase.DeleteAsset(ScenePath);
        if (!AssetDatabase.CopyAsset(Approved, ScenePath)) return "copy failed";
        var sc = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var dir = UnityEngine.Object.FindAnyObjectByType<BsDirector>(); var lay = dir.GetComponent<AnnieLayers>();
        Renderer ground = null;
        foreach (var r in dir.laneRenderers) if (r.name == "L11_ground") ground = r;
        if (ground == null) return "L11_ground not found";
        ground.sharedMaterial = mat;
        // the third layer: five pattern children under one parent, wired into AnnieLayers so capture.json records the choice
        var par = new GameObject("L11b_GroundPattern"); par.transform.SetParent(dir.transform, false);
        foreach (var n in new[] { "Pattern_Off", "Pattern_M1", "Pattern_M2", "Pattern_M3", "Pattern_GrassOnly" })
        {
            var g = new GameObject(n); g.transform.SetParent(par.transform, false);
            lay.layers.Add(new AnnieLayers.Layer { name = n, target = g, on = n == "Pattern_Off" });
        }
        var sw = dir.gameObject.AddComponent<GbPatternSwitch>();
        sw.groundRenderer = ground; sw.layers = lay; sw.pattern = GbPattern.Off;
        sw.m1 = T("M1_patches.png"); sw.m2 = T("M2_bleed.png"); sw.m3 = T("M3_mostly_grass.png");
        EditorSceneManager.MarkSceneDirty(sc); EditorSceneManager.SaveScene(sc, ScenePath);
        AssetDatabase.SaveAssets();
        return "built; layers=" + lay.layers.Count;
    }

    // a = name|approved/blend|pattern|frames|outDir|caption
    public static string Record(string a)
    {
        var p = a.Split('|');
        string name = p[0]; bool blend = p[1] == "blend"; var pat = (GbPattern)Enum.Parse(typeof(GbPattern), p[2]); int frames = int.Parse(p[3]); string outDir = p[4]; string cap0 = p[5];
        EditorSceneManager.OpenScene(blend ? ScenePath : Approved, OpenSceneMode.Single);
        var dir = UnityEngine.Object.FindAnyObjectByType<BsDirector>(); var cap = dir.GetComponent<AnnieCapture>();
        if (blend) dir.GetComponent<GbPatternSwitch>().pattern = pat;
        if (p.Length > 6 && p[6].Length > 0)   // layers to switch off, csv (e.g. L16_Archers for the archer-mask reference)
        {
            var offSet = new System.Collections.Generic.HashSet<string>(p[6].Split(','));
            var lay = dir.GetComponent<AnnieLayers>(); foreach (var l in lay.layers) if (offSet.Contains(l.name)) l.on = false; lay.Apply();
        }
        Directory.CreateDirectory(outDir);
        dir.scroll = true; dir.startTick = -2; dir.ticks = 1728; dir.logPath = Path.Combine(outDir, "unity_log.txt");
        cap.outDir = outDir; cap.fps = 48; cap.frameCount = frames; cap.autoStart = true; cap.replayLayersOnStart = false; cap.caption = cap0;
        EditorApplication.EnterPlaymode();
        return "armed " + name;
    }
}
