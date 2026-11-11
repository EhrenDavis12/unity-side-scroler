using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Annie;
using Annie.BattleScene1;
using Annie.BattleScene1Wind;
using Annie.BattleScene1GroundBlend;

// Builder for BattleScene1Wind (a copy of BattleScene1GroundBlend with wind on the close trees and grass).
//   unity command run_script --file AnnieBuild/BattleScene1Wind.cs --entry WindBuild.Build
//   unity command run_script --file AnnieBuild/BattleScene1Wind.cs --entry WindBuild.Record --args '["name|A or B|windOn(1/0)|frames|outDir|caption|startPatch"]'
public static class WindBuild
{
    const string Job = "Assets/Annie/BattleScene1Wind/";
    const string Src = "../../asset-animation-demo/local_runs/unity/battle_scene1_wind/maps/";
    const string Base = "Assets/Annie/BattleScene1GroundBlend/Scenes/BattleScene1GroundBlend.unity";
    const string ScenePath = Job + "Scenes/BattleScene1Wind.unity";
    // lane, mask file, peak A px, peak B px
    static readonly (string lane, string mask, float a, float b)[] Lanes = {
        ("L10_trees_near", "M_L10_trees_near.png", 10f, 16f),
        ("L13_k2_band", "M_L13_k2_band.png", 5f, 8f),
        ("L14_planted_trees", "M_L14_planted_trees.png", 10f, 16f),
        ("L15_bushes", "M_L15_bushes.png", 5f, 8f),
        ("L17_clumps", "M_L17_clumps.png", 3f, 5f),
    };

    static void Import(string path)
    {
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        ti.textureType = TextureImporterType.Default; ti.sRGBTexture = false;
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
        var names = new System.Collections.Generic.List<string> { "P_wind.png" };
        foreach (var l in Lanes) names.Add(l.mask);
        foreach (var n in names) File.Copy(Src + n, Job + "Textures/" + n, true);
        AssetDatabase.Refresh();
        foreach (var n in names) Import(Job + "Textures/" + n);
        Func<string, Texture2D> T = n => AssetDatabase.LoadAssetAtPath<Texture2D>(Job + "Textures/" + n);

        if (File.Exists(ScenePath)) AssetDatabase.DeleteAsset(ScenePath);
        if (!AssetDatabase.CopyAsset(Base, ScenePath)) return "copy failed";
        var sc = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var dir = UnityEngine.Object.FindAnyObjectByType<BsDirector>(); var lay = dir.GetComponent<AnnieLayers>();
        var shader = Shader.Find("Annie/WwWind");
        if (shader == null) return "shader missing";
        var par = new GameObject("L18_Wind"); par.transform.SetParent(dir.transform, false);
        var wl = new System.Collections.Generic.List<WwWind.Lane>();
        foreach (var l in Lanes)
        {
            Renderer r = null;
            foreach (var x in dir.laneRenderers) if (x.name == l.lane) r = x;
            if (r == null) return "lane not found " + l.lane;
            string mp = Job + "Materials/Mat_" + l.lane + "_wind.mat";
            AssetDatabase.DeleteAsset(mp);
            var mat = new Material(r.sharedMaterial); mat.shader = shader;   // copy of the approved lane material, approved asset untouched
            mat.SetTexture("_MainTex", r.sharedMaterial.GetTexture("_MainTex"));
            mat.SetTexture("_MTex", T(l.mask)); mat.SetTexture("_PTex", T("P_wind.png")); mat.SetFloat("_Peak", 0);
            AssetDatabase.CreateAsset(mat, mp);
            r.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(mp);
            string ln = "Wind_" + l.lane;
            var g = new GameObject(ln); g.transform.SetParent(par.transform, false);
            lay.layers.Add(new AnnieLayers.Layer { name = ln, target = g, on = true });
            wl.Add(new WwWind.Lane { layerName = ln, renderer = r, peakA = l.a, peakB = l.b });
        }
        var w = dir.gameObject.AddComponent<WwWind>();
        w.layers = lay; w.lanes = wl.ToArray(); w.take = 1; w.startTick = -2;
        EditorSceneManager.MarkSceneDirty(sc); EditorSceneManager.SaveScene(sc, ScenePath);
        AssetDatabase.SaveAssets();
        return "built; layers=" + lay.layers.Count;
    }

    // a = name|A or B|windOn|frames|outDir|caption   (windOn 0 switches every Wind_* layer off)
    public static string Record(string a)
    {
        var p = a.Split('|');
        string name = p[0]; int take = p[1] == "B" ? 2 : 1; bool on = p[2] == "1"; int frames = int.Parse(p[3]); string outDir = p[4]; string cap0 = p[5];
        bool scroll = p.Length < 7 || p[6] != "noscroll";
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var dir = UnityEngine.Object.FindAnyObjectByType<BsDirector>(); var cap = dir.GetComponent<AnnieCapture>(); var lay = dir.GetComponent<AnnieLayers>();
        dir.GetComponent<GbPatternSwitch>().pattern = GbPattern.M1;   // the approved ground blend
        var w = dir.GetComponent<WwWind>(); w.take = take;
        foreach (var l in lay.layers) if (l.name.StartsWith("Wind_")) l.on = on;
        lay.Apply();
        Directory.CreateDirectory(outDir);
        w.logPath = Path.Combine(outDir, "wind.json");
        dir.scroll = scroll; dir.startTick = -2; dir.ticks = 1728; dir.logPath = Path.Combine(outDir, "unity_log.txt");
        cap.outDir = outDir; cap.fps = 48; cap.frameCount = frames; cap.autoStart = true; cap.replayLayersOnStart = false; cap.caption = cap0;
        EditorApplication.EnterPlaymode();
        return "armed " + name;
    }
}
