using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Annie;
using Annie.BattleScene1Intro;

// Builder for the BattleScene1Intro job (battle scene 1 intro v4, TEST). Reads the approved BattleScene1 job (textures, shader, scene.json) and changes none of it. Run with:
//   unity command run_script --file AnnieBuild/BattleScene1Intro.cs --entry BattleScene1IntroBuild.Build --args '[""]'
//   unity command run_script --file AnnieBuild/BattleScene1Intro.cs --entry BattleScene1IntroBuild.Record --args '["name|take A/B|frames|outDir|offLayersCsv|startTick"]'
public static class BattleScene1IntroBuild
{
    const string Job = "Assets/Annie/BattleScene1Intro/";
    const string Appr = "Assets/Annie/BattleScene1/";          // approved job: read only
    const string TexDir = Job + "Textures/";
    const string MatDir = Job + "Materials/";
    const string ScenePath = Job + "Scenes/BattleScene1Intro.unity";

    // identical to the approved builder's Import (lane=false, mips off, clamp)
    static void Import(string path)
    {
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        ti.textureType = TextureImporterType.Default; ti.sRGBTexture = true;
        ti.alphaSource = TextureImporterAlphaSource.FromInput; ti.alphaIsTransparency = true;
        ti.mipmapEnabled = false; ti.wrapMode = TextureWrapMode.Clamp; ti.filterMode = FilterMode.Bilinear;
        ti.textureCompression = TextureImporterCompression.Uncompressed; ti.npotScale = TextureImporterNPOTScale.None;
        ti.maxTextureSize = 8192; ti.isReadable = false;
        var ps = ti.GetDefaultPlatformTextureSettings(); ps.maxTextureSize = 8192; ps.textureCompression = TextureImporterCompression.Uncompressed; ps.format = TextureImporterFormat.RGBA32; ti.SetPlatformTextureSettings(ps);
        ti.SaveAndReimport();
    }

    static Material Mat(string name, Texture2D tex, bool scroll)
    {
        string p = MatDir + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(p);
        if (m == null) { m = new Material(Shader.Find("Annie/BsUnlit")); AssetDatabase.CreateAsset(m, p); }
        m.shader = Shader.Find("Annie/BsUnlit"); m.mainTexture = tex; m.SetFloat("_Scroll", scroll ? 1 : 0); m.SetFloat("_LaneW", 3840); m.SetVector("_Rect", new Vector4(0, 0, 1, 1));
        EditorUtility.SetDirty(m); return m;
    }
    static Texture2D T(string n)
    {
        var t = AssetDatabase.LoadAssetAtPath<Texture2D>(TexDir + n + ".png");      // this job's own import (run, stop sheets)
        if (t == null) t = AssetDatabase.LoadAssetAtPath<Texture2D>(Appr + "Textures/" + n + ".png");   // the approved scene's textures, referenced
        return t;
    }

    static Renderer Quad(Transform parent, string name, Material m, int order, double cx, double cy, double w, double h)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad); go.name = name; UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false); go.transform.position = new Vector3((float)cx, (float)-cy, 0); go.transform.localScale = new Vector3((float)w, (float)h, 1);
        var r = go.GetComponent<MeshRenderer>(); r.sharedMaterial = m; r.sortingOrder = order;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false; r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off; r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        return r;
    }

    public static string Build(string a)
    {
        foreach (var d in new[] { MatDir, Job + "Scenes/" }) Directory.CreateDirectory(d);
        AssetDatabase.Refresh();
        foreach (var n in new[] { "run_body_forest", "robes_run_weapon_wood", "run_stop_body_forest", "robes_run_stop_weapon_wood" }) Import(TexDir + n + ".png");
        var data = JsonUtility.FromJson<BiScene>(File.ReadAllText(Appr + "Data/scene.json"));
        var sc = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var camGo = new GameObject("Main Camera"); camGo.tag = "MainCamera"; var cam = camGo.AddComponent<Camera>();
        cam.orthographic = true; cam.orthographicSize = 540; cam.transform.position = new Vector3(960, -540, -10); cam.nearClipPlane = 0.1f; cam.farClipPlane = 100;
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black; cam.allowHDR = false; cam.allowMSAA = false;
        var root = new GameObject("BattleScene1Intro");
        var lay = root.AddComponent<AnnieLayers>(); lay.layers = new List<AnnieLayers.Layer>();
        var dir = root.AddComponent<BiDirector>(); var cap = root.AddComponent<AnnieCapture>();
        Func<string, Transform> L = nm => { var g = new GameObject(nm); g.transform.SetParent(root.transform, false); lay.layers.Add(new AnnieLayers.Layer { name = nm, target = g, on = true }); return g.transform; };

        var sky = L("L01_Sky"); Quad(sky, "sky", Mat("Mat_L01_sky", T("L01_sky"), false), 1000, 960, 540, 1920, 1080);
        var names = new Dictionary<string, string> {
            {"L02_clouds_far","L02_CloudsFar"},{"L03_clouds_mid","L03_CloudsMid"},{"L04_clouds_near","L04_CloudsNear"},{"L05_mountains_back","L05_MountainsBack"},{"L06_mountains_front","L06_MountainsFront"},
            {"L07_hills","L07_Hills"},{"L08_trees_far","L08_HorizonTreesFar"},{"L09_trees_mid","L09_HorizonTreesMid"},{"L10_trees_near","L10_HorizonTreesNear"},{"L11_ground","L11_Ground"},
            {"L12_ground_haze","L12_GroundHaze"},{"L13_k2_band","L13_K2Band"},{"L14_planted_trees","L14_PlantedTrees"},{"L15_bushes","L15_BushesRocksGrass"},{"L17_clumps","L17_ForegroundClumps"} };
        var laneR = new Renderer[data.laneList.Count];
        Transform archL = null, archR = null;
        for (int li = 0; li < data.laneList.Count; li++)
        {
            var ln = data.laneList[li]; int num = int.Parse(ln.name.Substring(1, 2));
            if (num > 16 && archL == null) { archL = L("L16_ArchersLeft"); archR = L("L16_ArchersRight"); }
            var t = L(names[ln.name]); bool haze = ln.name == "L12_ground_haze";
            var mat = Mat("Mat_" + ln.name, T(ln.name), !haze);
            laneR[li] = Quad(t, ln.name, mat, num * 1000, 960, ln.y0 + ln.rows / 2.0, 1920, ln.rows);
        }
        if (archL == null) { archL = L("L16_ArchersLeft"); archR = L("L16_ArchersRight"); }
        string[] Kinds = { "run", "run_stop", "idle" };
        var bodyMats = new Material[3]; var weapMats = new Material[3];
        for (int k = 0; k < 3; k++) { bodyMats[k] = Mat("Mat_body_" + Kinds[k], T(Kinds[k] + "_body_forest"), false); weapMats[k] = Mat("Mat_weapon_" + Kinds[k], T("robes_" + Kinds[k] + "_weapon_wood"), false); }
        int nF = data.figs.Count; var bodyR = new Renderer[nF]; var weapR = new Renderer[nF];
        for (int i = 0; i < nF; i++)
        {
            var f = data.figs[i]; string nm = f.side + "_r" + f.row + "_c" + f.col; var par = f.side == "player" ? archL : archR;
            bodyR[i] = Quad(par, "body_" + nm, bodyMats[2], 16100 + i * 2, 0, 0, 1, 1); weapR[i] = Quad(par, "weapon_" + nm, weapMats[2], 16101 + i * 2, 0, 0, 1, 1);
        }
        dir.sceneJson = AssetDatabase.LoadAssetAtPath<TextAsset>(Appr + "Data/scene.json");
        dir.planJson = AssetDatabase.LoadAssetAtPath<TextAsset>(Job + "Data/intro_A.json");
        dir.laneRenderers = laneR; dir.bodyR = bodyR; dir.weaponR = weapR; dir.bodyMats = bodyMats; dir.weaponMats = weapMats; dir.startTick = -2;
        cap.cam = cam; cap.fps = 48; cap.frameCount = 228; cap.replayLayersOnStart = false; cap.autoStart = true; cap.caption = "TEST";
        EditorSceneManager.MarkSceneDirty(sc);
        EditorSceneManager.SaveScene(sc, ScenePath);
        AssetDatabase.SaveAssets();
        return "built " + lay.layers.Count + " layers";
    }

    public static string Record(string a)
    {
        var p = a.Split('|');
        string name = p[0]; string take = p[1]; int frames = int.Parse(p[2]); string outDir = p[3]; string off = p.Length > 4 ? p[4] : ""; int st = p.Length > 5 ? int.Parse(p[5]) : -2;
        AssetDatabase.Refresh();
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var dir = UnityEngine.Object.FindAnyObjectByType<BiDirector>(); var cap = dir.GetComponent<AnnieCapture>(); var lay = dir.GetComponent<AnnieLayers>();
        var plan = AssetDatabase.LoadAssetAtPath<TextAsset>(Job + "Data/intro_" + take + ".json"); dir.planJson = plan;
        var pd = JsonUtility.FromJson<BiPlan>(plan.text);
        var offSet = new HashSet<string>(off.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
        foreach (var l in lay.layers) l.on = !offSet.Contains(l.name);
        lay.Apply();
        Directory.CreateDirectory(outDir);
        dir.startTick = st; dir.logPath = Path.Combine(outDir, "unity_log.txt");
        cap.outDir = outDir; cap.fps = 48; cap.frameCount = frames > 0 ? frames : pd.nt; cap.autoStart = true; cap.replayLayersOnStart = false; cap.caption = pd.caption;
        EditorUtility.SetDirty(dir); EditorUtility.SetDirty(cap); EditorUtility.SetDirty(lay);
        EditorApplication.EnterPlaymode();
        return "armed " + name + " nt " + pd.nt;
    }
    public static string OpenInteractive() { EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single); return "opened"; }
}
