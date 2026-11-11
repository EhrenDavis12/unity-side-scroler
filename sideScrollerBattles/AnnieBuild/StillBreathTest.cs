using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Annie;
using Annie.StillBreathTest;

// Builder for the StillBreathTest job.
//   unity command run_script --file AnnieBuild/StillBreathTest.cs --entry SbBuild.Build
//   unity command run_script --file AnnieBuild/StillBreathTest.cs --entry SbBuild.Record --args '["take|frames|outDir|rest 0/1|tickOffset|peakPx|periodSec|startCell"]'
public static class SbBuild
{
    const string Job = "Assets/Annie/StillBreathTest/";
    const string ScenePath = Job + "Scenes/StillBreathTest.unity";
    const string Src = "../../asset-animation-demo/local_runs/";
    const string RealIdle = "Assets/Characters/archer_versions/idle_v2.png";

    static void Import(string path, bool srgb)
    {
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        ti.textureType = TextureImporterType.Default; ti.sRGBTexture = srgb;
        ti.alphaSource = srgb ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None; ti.alphaIsTransparency = srgb;
        ti.mipmapEnabled = false; ti.wrapMode = TextureWrapMode.Clamp; ti.filterMode = FilterMode.Bilinear;
        ti.textureCompression = TextureImporterCompression.Uncompressed; ti.npotScale = TextureImporterNPOTScale.None;
        ti.maxTextureSize = 8192; ti.isReadable = false;
        var ps = ti.GetDefaultPlatformTextureSettings(); ps.maxTextureSize = 8192; ps.textureCompression = TextureImporterCompression.Uncompressed; ps.format = TextureImporterFormat.RGBA32; ti.SetPlatformTextureSettings(ps);
        ti.SaveAndReimport();
    }

    static Material Mat(string name, Texture main, float grid)
    {
        string mp = Job + "Materials/" + name + ".mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(mp);
        if (mat == null) { mat = new Material(Shader.Find("Annie/SbBreath")); AssetDatabase.CreateAsset(mat, mp); }
        mat.shader = Shader.Find("Annie/SbBreath");
        mat.SetTexture("_MainTex", main); mat.SetFloat("_Grid", grid); mat.SetFloat("_Cell", 0); mat.SetFloat("_UseMaps", 0); mat.SetFloat("_Push", 0);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    // quad whose figure (bbox centre x, bottom row of the figure, from the top of the cell) sits at colX with its feet on baseY (px, y up)
    static GameObject Fig(string name, Transform parent, Material mat, float cell, float scale, float figCx, float bottomRow, float colX, float baseY)
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Quad); g.name = name; g.transform.SetParent(parent, false);
        UnityEngine.Object.DestroyImmediate(g.GetComponent<Collider>());
        float cx = colX - (figCx - cell / 2f) * scale;
        float cy = baseY + (bottomRow - cell / 2f) * scale;
        g.transform.position = new Vector3(cx * 0.01f, cy * 0.01f, 0);
        g.transform.localScale = new Vector3(cell * scale * 0.01f, cell * scale * 0.01f, 1);
        var r = g.GetComponent<MeshRenderer>(); r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
        return g;
    }

    public static string Build(string a)
    {
        foreach (var d in new[] { "Textures", "Materials", "Scenes" }) Directory.CreateDirectory(Job + d);
        string M = Src + "unity/still_breath_test/maps/";
        var files = new[] {
            (Src + "characters/rogue/keys/K0_rogue.png", "K0_rogue.png", true),
            (Src + "archer/approve/K0_start_pose.png", "K0_start_pose.png", true),
            (M + "P_breath.png", "P_breath.png", false), (M + "M_rogue_A.png", "M_rogue_A.png", false), (M + "M_rogue_B.png", "M_rogue_B.png", false),
            (M + "M_archer_A.png", "M_archer_A.png", false), (M + "M_archer_B.png", "M_archer_B.png", false) };
        foreach (var f in files) File.Copy(f.Item1, Job + "Textures/" + f.Item2, true);
        AssetDatabase.Refresh();
        foreach (var f in files) Import(Job + "Textures/" + f.Item2, f.Item3);
        Func<string, Texture2D> T = n => AssetDatabase.LoadAssetAtPath<Texture2D>(Job + "Textures/" + n);
        var real = AssetDatabase.LoadAssetAtPath<Texture2D>(RealIdle);
        if (real == null) return "real idle missing";

        var mReal = Mat("Mat_ArcherRealIdle", real, 8);
        var mArcher = Mat("Mat_ArcherFake", T("K0_start_pose.png"), 1);
        var mRogue = Mat("Mat_RogueFake", T("K0_rogue.png"), 1);
        AssetDatabase.SaveAssets();

        var sc = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var camGo = new GameObject("Main Camera"); camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>();
        cam.orthographic = true; cam.orthographicSize = 5.4f; cam.transform.position = new Vector3(0, 0, -10);
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.80f, 0.78f, 0.74f, 1f);
        cam.nearClipPlane = 0.1f; cam.farClipPlane = 50f;

        var root = new GameObject("StillBreath");
        float H = 502f, baseY = -251f;
        float sReal = H / 400f, sArcher = H / 795f, sRogue = H / 802f;
        var gReal = Fig("Archer_RealIdle", root.transform, mReal, 512, sReal, 264.5f, 429, -640, baseY);
        var gArcher = Fig("Archer_Fake", root.transform, mArcher, 1024, sArcher, 529.5f, 857, 0, baseY);
        var gRogue = Fig("Assassin_Fake", root.transform, mRogue, 1024, sRogue, 517f, 864, 640, baseY);

        var drv = root.AddComponent<SbBreathDriver>();
        drv.pTex = T("P_breath.png");
        drv.figures.Add(new SbBreathDriver.Figure { name = "Archer_RealIdle", renderer = gReal.GetComponent<Renderer>(), sheet = true });
        drv.figures.Add(new SbBreathDriver.Figure { name = "Archer_Fake", renderer = gArcher.GetComponent<Renderer>(), useMaps = true, peakPx = 11f, mapA = T("M_archer_A.png"), mapB = T("M_archer_B.png") });
        drv.figures.Add(new SbBreathDriver.Figure { name = "Assassin_Fake", renderer = gRogue.GetComponent<Renderer>(), useMaps = true, peakPx = 11f, mapA = T("M_rogue_A.png"), mapB = T("M_rogue_B.png") });

        // toggles the capture logs: which take's maps are in use, and which figures are drawn
        var lay = root.AddComponent<AnnieLayers>();
        foreach (var n in new[] { "Take_A", "Take_B" }) { var g = new GameObject(n); g.transform.SetParent(root.transform, false); lay.layers.Add(new AnnieLayers.Layer { name = n, target = g, on = n == "Take_A" }); }
        lay.layers.Add(new AnnieLayers.Layer { name = "Archer_RealIdle", target = gReal, on = true });
        lay.layers.Add(new AnnieLayers.Layer { name = "Archer_Fake", target = gArcher, on = true });
        lay.layers.Add(new AnnieLayers.Layer { name = "Assassin_Fake", target = gRogue, on = true });

        var cap = root.AddComponent<AnnieCapture>(); cap.cam = cam; cap.autoStart = false; cap.replayLayersOnStart = false; cap.frameCount = 240; cap.fps = 30;
        EditorSceneManager.MarkSceneDirty(sc); EditorSceneManager.SaveScene(sc, ScenePath);
        AssetDatabase.SaveAssets();
        return "built";
    }

    // a = take|frames|outDir|rest|tickOffset|peakPx|periodSec|startCell   (round 2: 14/11 px, 2.0 s, cell 8)
    public static string Record(string a)
    {
        var p = a.Split('|');
        char take = p[0][0]; int frames = int.Parse(p[1]); string outDir = p[2]; bool rest = p[3] == "1"; float off = float.Parse(p[4]); float peak = float.Parse(p[5]); float period = float.Parse(p[6]); int startCell = int.Parse(p[7]);
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var root = GameObject.Find("StillBreath");
        var drv = root.GetComponent<SbBreathDriver>(); var lay = root.GetComponent<AnnieLayers>(); var cap = root.GetComponent<AnnieCapture>();
        drv.take = take; drv.freezeRest = rest; drv.tickOffset = off; drv.period = period; drv.sheetStartCell = startCell;
        foreach (var f in drv.figures) if (f.useMaps) f.peakPx = peak;
        foreach (var l in lay.layers) { if (l.name == "Take_A") l.on = take == 'A'; if (l.name == "Take_B") l.on = take == 'B'; }
        lay.Apply();
        Directory.CreateDirectory(outDir);
        cap.outDir = outDir; cap.frameCount = frames; cap.fps = 30; cap.autoStart = true; cap.replayLayersOnStart = false;
        float disp = peak * 502f / 802f; // display px = source px x the Assassin's scale (502/802); the Archer fake's is 502/795
        cap.caption = "TEST · still + grey breath layers · take " + take + (rest ? " (push 0)" : "") + " · peak " + disp.ToString("0.0") + " px on screen (" + peak.ToString("0") + " px source) · breath " + period.ToString("0.0") + " s";
        EditorApplication.EnterPlaymode();
        return "armed " + take;
    }
}
