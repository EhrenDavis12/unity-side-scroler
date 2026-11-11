using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Annie;
using Annie.BattleScene1;

// Builder for the BattleScene1 job. Run with:
//   unity command run_script --file AnnieBuild/BattleScene1.cs --entry BattleScene1Build.Build --args '["<mipmaps 0|1>"]'
//   unity command run_script --file AnnieBuild/BattleScene1.cs --entry BattleScene1Build.Record --args '["name|scroll 0/1|frames|outDir|offLayersCsv"]'
public static class BattleScene1Build
{
    const string Job = "Assets/Annie/BattleScene1/";
    const string TexDir = Job + "Textures/";
    const string MatDir = Job + "Materials/";
    const string ScenePath = Job + "Scenes/BattleScene1.unity";

    static readonly string[] Kinds = { "idle", "idle_headturn", "idle_shift", "attack", "hit" };

    static void Import(string path, bool lane, bool sheet, bool mips, bool clamp)
    {
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        ti.textureType = TextureImporterType.Default; ti.sRGBTexture = true;
        ti.alphaSource = TextureImporterAlphaSource.FromInput; ti.alphaIsTransparency = !lane;
        ti.mipmapEnabled = mips; ti.wrapMode = clamp ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
        ti.filterMode = mips ? FilterMode.Trilinear : FilterMode.Bilinear;
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
    static Texture2D T(string n) { return AssetDatabase.LoadAssetAtPath<Texture2D>(TexDir + n + ".png"); }

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
        bool mips = a == "1";
        foreach (var d in new[] { MatDir, Job + "Scenes/" }) Directory.CreateDirectory(d);
        AssetDatabase.Refresh();
        var data = JsonUtility.FromJson<BsData>(File.ReadAllText(Job + "Data/scene.json"));
        // textures: lanes (repeat, no mips), sky/haze (clamp), sheets/arrow/spark (clamp; mips as asked)
        foreach (var f in Directory.GetFiles(TexDir, "*.png"))
        {
            string path = f.Replace('\\', '/'), n = Path.GetFileNameWithoutExtension(path);
            bool lane = n.StartsWith("L") && n.Length > 3 && char.IsDigit(n[1]);
            bool clamp = !lane || n == "L01_sky" || n == "L12_ground_haze";
            Import(path, lane, !lane, !lane && mips, clamp);
        }
        var sc = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var camGo = new GameObject("Main Camera"); camGo.tag = "MainCamera"; var cam = camGo.AddComponent<Camera>();
        cam.orthographic = true; cam.orthographicSize = 540; cam.transform.position = new Vector3(960, -540, -10); cam.nearClipPlane = 0.1f; cam.farClipPlane = 100;
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black; cam.allowHDR = false; cam.allowMSAA = false;
        var root = new GameObject("BattleScene1");
        var lay = root.AddComponent<AnnieLayers>(); lay.layers = new List<AnnieLayers.Layer>();
        var dir = root.AddComponent<BsDirector>(); var cap = root.AddComponent<AnnieCapture>();
        Func<string, Transform> L = nm => { var g = new GameObject(nm); g.transform.SetParent(root.transform, false); lay.layers.Add(new AnnieLayers.Layer { name = nm, target = g, on = true }); return g.transform; };

        var sky = L("L01_Sky"); Quad(sky, "sky", Mat("Mat_L01_sky", T("L01_sky"), false), 1000, 960, 540, 1920, 1080);
        var names = new Dictionary<string, string> {
            {"L02_clouds_far","L02_CloudsFar"},{"L03_clouds_mid","L03_CloudsMid"},{"L04_clouds_near","L04_CloudsNear"},{"L05_mountains_back","L05_MountainsBack"},{"L06_mountains_front","L06_MountainsFront"},
            {"L07_hills","L07_Hills"},{"L08_trees_far","L08_HorizonTreesFar"},{"L09_trees_mid","L09_HorizonTreesMid"},{"L10_trees_near","L10_HorizonTreesNear"},{"L11_ground","L11_Ground"},
            {"L12_ground_haze","L12_GroundHaze"},{"L13_k2_band","L13_K2Band"},{"L14_planted_trees","L14_PlantedTrees"},{"L15_bushes","L15_BushesRocksGrass"},{"L17_clumps","L17_ForegroundClumps"} };
        // layer 16 sits between 15 and 17: build lane layers in order, inserting the archers after L15
        var laneR = new Renderer[data.laneList.Count]; var layerT = new Dictionary<string, Transform>();
        Transform archers = null;
        for (int li = 0; li < data.laneList.Count; li++)
        {
            var ln = data.laneList[li]; int num = int.Parse(ln.name.Substring(1, 2));
            if (num > 16 && archers == null) { archers = L("L16_Archers"); }
            var t = L(names[ln.name]); bool haze = ln.name == "L12_ground_haze";
            var mat = Mat("Mat_" + ln.name, T(ln.name), !haze);
            laneR[li] = Quad(t, ln.name, mat, num * 1000, 960, ln.y0 + ln.rows / 2.0, 1920, ln.rows);
        }
        if (archers == null) archers = L("L16_Archers");
        var layer18 = L("L18_ArrowsSparks");
        // archers
        var bodyMats = new Material[5]; var weapMats = new Material[5];
        for (int k = 0; k < 5; k++) { bodyMats[k] = Mat("Mat_body_" + Kinds[k], T(Kinds[k] + "_body_forest"), false); weapMats[k] = Mat("Mat_weapon_" + Kinds[k], T("robes_" + Kinds[k] + "_weapon_wood"), false); }
        var bodyR = new Renderer[30]; var weapR = new Renderer[30];
        for (int i = 0; i < 30; i++)
        {
            var f = data.figs[i]; string nm = f.side + "_r" + f.row + "_c" + f.col;
            bodyR[i] = Quad(archers, "body_" + nm, bodyMats[0], 16100 + i * 2, 0, 0, 1, 1); weapR[i] = Quad(archers, "weapon_" + nm, weapMats[0], 16101 + i * 2, 0, 0, 1, 1);
        }
        // shots: arrows first, then sparks (one quad per shot each)
        int ns = data.plan.shots.Count; var arrR = new Renderer[ns]; var spkR = new Renderer[ns];
        var arrMat = Mat("Mat_arrow_wood", T("arrow_wood"), false); var spkMat = Mat("Mat_hit_spark", T("hit_spark_sheet"), false);
        for (int q = 0; q < ns; q++) { arrR[q] = Quad(layer18, "arrow_" + q, arrMat, 18000 + q, 0, 0, 1, 1); arrR[q].enabled = false; }
        for (int q = 0; q < ns; q++) { spkR[q] = Quad(layer18, "spark_" + q, spkMat, 18100 + q, 0, 0, 1, 1); spkR[q].enabled = false; }

        dir.sceneJson = AssetDatabase.LoadAssetAtPath<TextAsset>(Job + "Data/scene.json");
        dir.laneRenderers = laneR; dir.bodyR = bodyR; dir.weaponR = weapR; dir.bodyMats = bodyMats; dir.weaponMats = weapMats; dir.arrowR = arrR; dir.sparkR = spkR;
        dir.scroll = false; dir.startTick = -1; dir.ticks = 1728;
        cap.cam = cam; cap.fps = 48; cap.frameCount = 1728; cap.replayLayersOnStart = false; cap.autoStart = true; cap.caption = "TEST · Unity battle scene 1";
        EditorSceneManager.MarkSceneDirty(sc);
        EditorSceneManager.SaveScene(sc, ScenePath);
        AssetDatabase.SaveAssets();
        return "built " + lay.layers.Count + " layers; mips=" + mips;
    }

    public static string Record(string a)
    {
        var p = a.Split('|');
        string name = p[0]; bool scroll = p[1] == "1"; int frames = int.Parse(p[2]); string outDir = p[3]; string off = p.Length > 4 ? p[4] : "";
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var dir = UnityEngine.Object.FindAnyObjectByType<BsDirector>(); var cap = dir.GetComponent<AnnieCapture>(); var lay = dir.GetComponent<AnnieLayers>();
        var offSet = new HashSet<string>(off.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
        foreach (var l in lay.layers) l.on = !offSet.Contains(l.name);
        lay.Apply();
        Directory.CreateDirectory(outDir);
        dir.scroll = scroll; dir.startTick = -2; dir.ticks = 1728; dir.logPath = Path.Combine(outDir, "unity_log.txt");
        cap.outDir = outDir; cap.fps = 48; cap.frameCount = frames; cap.autoStart = true; cap.replayLayersOnStart = false; cap.caption = "TEST · Unity battle scene 1 · " + (dir.scroll ? "scroll ON (t = tick / 48)" : "scroll OFF (backdrop frozen at t = 13.1 s)") + " · 48 fps";
        EditorUtility.SetDirty(dir); EditorUtility.SetDirty(cap); EditorUtility.SetDirty(lay);
        EditorApplication.EnterPlaymode();
        return "armed " + name;
    }
    public static string OpenInteractive() { EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single); return "opened"; }
}
