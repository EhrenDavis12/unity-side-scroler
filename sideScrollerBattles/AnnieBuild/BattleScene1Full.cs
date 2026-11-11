using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Annie;
using Annie.BattleScene1;
using Annie.BattleScene1Wind;
using Annie.BattleScene1GroundBlend;
using Annie.BattleScene1Full;

// Builder for BattleScene1Full (queue #64): approved intro take B + the fight on the wind scene, v2 hit effects, health bars. TEST.
//   unity command run_script --file AnnieBuild/BattleScene1Full.cs --entry BattleScene1FullBuild.Build
//   unity command run_script --file AnnieBuild/BattleScene1Full.cs --entry BattleScene1FullBuild.Record --args '["name|frames|outDir|caption|startTick"]'
public static class BattleScene1FullBuild
{
    const string Job = "Assets/Annie/BattleScene1Full/";
    const string Appr = "Assets/Annie/BattleScene1/";
    const string IntroJob = "Assets/Annie/BattleScene1Intro/";
    const string Base = "Assets/Annie/BattleScene1Wind/Scenes/BattleScene1Wind.unity";
    const string ScenePath = Job + "Scenes/BattleScene1Full.unity";
    const string TexDir = Job + "Textures/";
    const string MatDir = Job + "Materials/";
    const string Loadouts = "Assets/Characters/archer_loadouts/";
    const string PlanDir = "../../asset-animation-demo/local_runs/unity/battle_scene1_full/plan/";

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
    static Material Mat(string name, Texture2D tex)
    {
        string p = MatDir + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(p);
        if (m == null) { m = new Material(Shader.Find("Annie/BsUnlit")); AssetDatabase.CreateAsset(m, p); }
        m.shader = Shader.Find("Annie/BsUnlit"); m.mainTexture = tex; m.SetFloat("_Scroll", 0); m.SetFloat("_LaneW", 3840); m.SetVector("_Rect", new Vector4(0, 0, 1, 1));
        EditorUtility.SetDirty(m); return m;
    }
    static Texture2D Find(string n)
    {
        foreach (var d in new[] { TexDir, IntroJob + "Textures/", Appr + "Textures/" })
        {
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>(d + n + ".png"); if (t != null) return t;
        }
        return null;
    }
    static Renderer Quad(Transform parent, string name, Material m, int order)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad); go.name = name; UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false); go.transform.position = Vector3.zero; go.transform.localScale = Vector3.one;
        var r = go.GetComponent<MeshRenderer>(); r.sharedMaterial = m; r.sortingOrder = order;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false; r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off; r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        return r;
    }
    static SfEffect MakeFx(Transform parent, string name)
    {
        var pf = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Annie/ArcherVsArcher/Prefabs/Fx_Spark.prefab");
        var inst = (GameObject)PrefabUtility.InstantiatePrefab(pf, parent);
        PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        inst.name = name;
        var av = inst.GetComponent("AvaEffect") as MonoBehaviour;
        var sf = inst.AddComponent<SfEffect>();
        foreach (var f in typeof(SfEffect).GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            var af = av.GetType().GetField(f.Name);
            if (af != null && af.FieldType == f.FieldType) f.SetValue(sf, af.GetValue(av));
        }
        UnityEngine.Object.DestroyImmediate(av);
        sf.W = 100f;
        return sf;
    }

    public static string Build(string a)
    {
        foreach (var dd in new[] { TexDir, MatDir, Job + "Scenes/", Job + "Data/" }) Directory.CreateDirectory(dd);
        // byte-identical copies of the death sheets; the originals are never reimported
        foreach (var n in new[] { "death_body_forest.png", "robes_death_weapon_wood.png" }) File.Copy(Loadouts + n, TexDir + n, true);
        string planName = string.IsNullOrEmpty(a) ? "full_plan" : a;
        File.Copy(PlanDir + planName + ".json", Job + "Data/" + planName + ".json", true);
        AssetDatabase.Refresh();
        foreach (var n in new[] { "death_body_forest", "robes_death_weapon_wood" }) Import(TexDir + n + ".png");
        var plan = AssetDatabase.LoadAssetAtPath<TextAsset>(Job + "Data/" + planName + ".json");
        var sceneJson = AssetDatabase.LoadAssetAtPath<TextAsset>(Appr + "Data/scene.json");
        var data = JsonUtility.FromJson<SfSceneData>(sceneJson.text);

        if (File.Exists(ScenePath)) AssetDatabase.DeleteAsset(ScenePath);
        if (!AssetDatabase.CopyAsset(Base, ScenePath)) return "copy failed";
        var sc = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var dir0 = UnityEngine.Object.FindAnyObjectByType<BsDirector>(); var lay = dir0.GetComponent<AnnieLayers>(); var root = dir0.gameObject;
        var laneR = dir0.laneRenderers; var laneNames = laneR.Select(r => r.name).ToArray();
        var archers = dir0.transform.Find("L16_Archers");
        for (int c = archers.childCount - 1; c >= 0; c--) UnityEngine.Object.DestroyImmediate(archers.GetChild(c).gameObject);
        // the old arrow / spark quads (the sheet-drawn sparks) are replaced by v2's effect
        foreach (var l in lay.layers) if (l.name == "L18_ArrowsSparks") { l.on = false; if (l.target != null) l.target.SetActive(false); }
        UnityEngine.Object.DestroyImmediate(dir0);

        string[] bodyN = { "run_body_forest", "run_stop_body_forest", "idle_body_forest", "idle_headturn_body_forest", "idle_shift_body_forest", "attack_body_forest", "hit_body_forest", "death_body_forest" };
        string[] weapN = { "robes_run_weapon_wood", "robes_run_stop_weapon_wood", "robes_idle_weapon_wood", "robes_idle_headturn_weapon_wood", "robes_idle_shift_weapon_wood", "robes_attack_weapon_wood", "robes_hit_weapon_wood", "robes_death_weapon_wood" };
        var bodyMats = new Material[8]; var weapMats = new Material[8];
        for (int k = 0; k < 8; k++)
        {
            var bt = Find(bodyN[k]); var wt = Find(weapN[k]);
            if (bt == null || wt == null) return "texture missing " + bodyN[k] + " / " + weapN[k];
            bodyMats[k] = Mat("Mat_full_body_" + k, bt); weapMats[k] = Mat("Mat_full_weapon_" + k, wt);
        }
        int nF = data.figs.Count; var bodyR = new Renderer[nF]; var weapR = new Renderer[nF];
        var rowBase = new int[nF]; var kRow = new float[nF];
        for (int i = 0; i < nF; i++)
        {
            var f = data.figs[i]; int j = (f.side == "player" ? 0 : 3) + f.col;
            int rb = 16100 + (5 - f.row) * 100; rowBase[i] = rb; kRow[i] = (float)(404.0 * f.s / 240.0);
            string nm = f.side + "_r" + f.row + "_c" + f.col;
            bodyR[i] = Quad(archers, "body_" + nm, bodyMats[2], rb + 2 * j); weapR[i] = Quad(archers, "weapon_" + nm, weapMats[2], rb + 2 * j + 1);
        }
        // health bars: above everything
        var barPar = new GameObject("L19_HealthBars"); barPar.transform.SetParent(root.transform, false);
        lay.layers.Add(new AnnieLayers.Layer { name = "L19_HealthBars", target = barPar, on = true });
        var barR = new Renderer[nF]; var barMat = Mat("Mat_full_bar", Find("idle_body_forest"));
        for (int i = 0; i < nF; i++) barR[i] = Quad(barPar.transform, "bar_" + data.figs[i].side + "_r" + data.figs[i].row + "_c" + data.figs[i].col, barMat, 30000);
        // the v2 hit effect pool
        var fxPar = new GameObject("L18b_HitEffectsV2"); fxPar.transform.SetParent(root.transform, false);
        lay.layers.Add(new AnnieLayers.Layer { name = "L18b_HitEffectsV2", target = fxPar, on = true });
        var pool = new SfEffect[10];
        for (int i = 0; i < pool.Length; i++) pool[i] = MakeFx(fxPar.transform, "Fx_Spark_" + i);

        var d = root.AddComponent<SfDirector>();
        d.sceneJson = sceneJson; d.planJson = plan; d.startTick = -2; d.windShift = planName.EndsWith("_f") ? 108 : planName.EndsWith("_e") ? 60 : planName.EndsWith("_d") ? 12 : 0;
        d.laneRenderers = laneR; d.laneNames = laneNames; d.bodyR = bodyR; d.weaponR = weapR; d.bodyMats = bodyMats; d.weaponMats = weapMats;
        d.pool = pool; d.barR = barR; d.rowBase = rowBase; d.kRow = kRow; d.arrowTex = AssetDatabase.LoadAssetAtPath<Texture2D>(Appr + "Textures/arrow_wood.png");
        var sw = root.GetComponent<GbPatternSwitch>(); sw.pattern = GbPattern.M1;
        var w = root.GetComponent<WwWind>(); w.take = 2; w.startTick = -2;
        EditorSceneManager.MarkSceneDirty(sc); EditorSceneManager.SaveScene(sc, ScenePath);
        AssetDatabase.SaveAssets();
        return "built; layers=" + lay.layers.Count;
    }

    // a = name|frames|outDir|caption|startTick
    public static string Record(string a)
    {
        var p = a.Split('|');
        string name = p[0]; int frames = int.Parse(p[1]); string outDir = p[2]; string cap0 = p[3]; int st = p.Length > 4 ? int.Parse(p[4]) : -2;
        AssetDatabase.Refresh();
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var d = UnityEngine.Object.FindAnyObjectByType<SfDirector>(); var cap = d.GetComponent<AnnieCapture>(); var lay = d.GetComponent<AnnieLayers>();
        d.GetComponent<GbPatternSwitch>().pattern = GbPattern.M1;
        var w = d.GetComponent<WwWind>(); w.take = 2; w.startTick = st;
        foreach (var l in lay.layers) if (l.name == "L18_ArrowsSparks") l.on = false;
        lay.Apply();
        Directory.CreateDirectory(outDir);
        w.logPath = Path.Combine(outDir, "wind.json");
        d.startTick = st; d.framesDir = Path.Combine(outDir, "frames"); d.logPath = Path.Combine(outDir, "unity_log.txt");
        cap.outDir = outDir; cap.fps = 48; cap.frameCount = frames; cap.autoStart = true; cap.replayLayersOnStart = false; cap.caption = cap0;
        EditorUtility.SetDirty(d); EditorUtility.SetDirty(cap); EditorUtility.SetDirty(lay); EditorUtility.SetDirty(w);
        EditorApplication.EnterPlaymode();
        return "armed " + name;
    }
    public static string OpenInteractive() { EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single); return "opened"; }
}
