using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Annie;
using Annie.ScrollUnroll;

// Builder for the ScrollUnroll job.
//   unity command run_script --file AnnieBuild/ScrollUnroll.cs --entry SuBuild.Build
//   unity command run_script --file AnnieBuild/ScrollUnroll.cs --entry SuBuild.Record --args '["A|<outDir>"]'
public static class SuBuild
{
    const string Job = "Assets/Annie/ScrollUnroll/";
    const string ScenePath = Job + "Scenes/ScrollUnroll.unity";
    const string Runs = "../../asset-animation-demo/local_runs/";
    const string Art = Runs + "scenes/ui_parchment/scroll_v3/R04_OneRod/";
    const string Pcs = Runs + "unity/scroll_unroll/pieces/";

    static void Import(string path)
    {
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        ti.textureType = TextureImporterType.Default; ti.sRGBTexture = false;
        ti.alphaSource = TextureImporterAlphaSource.FromInput; ti.alphaIsTransparency = false;
        ti.mipmapEnabled = false; ti.wrapMode = TextureWrapMode.Clamp; ti.filterMode = FilterMode.Point;
        ti.textureCompression = TextureImporterCompression.Uncompressed; ti.npotScale = TextureImporterNPOTScale.None;
        ti.maxTextureSize = 8192; ti.isReadable = false;
        var ps = ti.GetDefaultPlatformTextureSettings(); ps.maxTextureSize = 8192; ps.textureCompression = TextureImporterCompression.Uncompressed; ps.format = TextureImporterFormat.RGBA32; ti.SetPlatformTextureSettings(ps);
        ti.SaveAndReimport();
    }

    static Texture2D T(string n) { return AssetDatabase.LoadAssetAtPath<Texture2D>(Job + "Textures/" + n); }

    static Material Mat(string name)
    {
        string mp = Job + "Materials/" + name + ".mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(mp);
        if (mat == null) { mat = new Material(Shader.Find("Annie/ScrollPiece")); AssetDatabase.CreateAsset(mat, mp); }
        mat.shader = Shader.Find("Annie/ScrollPiece");
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static SuDriver.Piece Quad(string name, Transform parent, Material mat)
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Quad); g.name = name; g.transform.SetParent(parent, false);
        UnityEngine.Object.DestroyImmediate(g.GetComponent<Collider>());
        var r = g.GetComponent<MeshRenderer>(); r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
        return new SuDriver.Piece { r = r };
    }

    static GameObject Layer(string name, Transform parent)
    {
        var g = new GameObject(name); g.transform.SetParent(parent, false); return g;
    }

    public static string Build(string a)
    {
        foreach (var d in new[] { "Textures", "Materials", "Scenes" }) Directory.CreateDirectory(Job + d);
        var files = new (string src, string dst)[] {
            (Art + "page.png", "page.png"),
            (Pcs + "page_no_panel.png", "page_no_panel.png"),
            (Pcs + "flat_modal.png", "flat_modal.png"), (Pcs + "flat_page.png", "flat_page.png"),
            (Pcs + "panel_rgba.png", "panel_rgba.png"), (Pcs + "rod_gap_shadow_rgba.png", "rod_gap_shadow_rgba.png"),
            (Pcs + "knob_top_rgba.png", "knob_top_rgba.png"), (Pcs + "knob_bottom_rgba.png", "knob_bottom_rgba.png"), (Pcs + "rod_body_rgba.png", "rod_body_rgba.png"),
            (Pcs + "panel_page_rgba.png", "panel_page_rgba.png"), (Pcs + "rod_gap_shadow_page_rgba.png", "rod_gap_shadow_page_rgba.png"),
            (Pcs + "knob_top_page_rgba.png", "knob_top_page_rgba.png"), (Pcs + "knob_bottom_page_rgba.png", "knob_bottom_page_rgba.png"), (Pcs + "rod_body_page_rgba.png", "rod_body_page_rgba.png") };
        foreach (var f in files) File.Copy(f.src, Job + "Textures/" + f.dst, true);
        AssetDatabase.Refresh();
        foreach (var f in files) Import(Job + "Textures/" + f.dst);

        var mPage = Mat("Mat_Page"); var mDim = Mat("Mat_Dim"); var mPanel = Mat("Mat_Panel"); var mGap = Mat("Mat_GapShadow"); var mContact = Mat("Mat_ContactShadow");
        var mKT = Mat("Mat_KnobTop"); var mKB = Mat("Mat_KnobBottom"); var mRod = Mat("Mat_RodBody"); var mHold = Mat("Mat_HoldFlat");
        mPage.SetTexture("_MainTex", T("page.png")); mPanel.SetTexture("_MainTex", T("panel_rgba.png")); mContact.SetTexture("_MainTex", T("panel_rgba.png"));
        mGap.SetTexture("_MainTex", T("rod_gap_shadow_rgba.png")); mKT.SetTexture("_MainTex", T("knob_top_rgba.png")); mKB.SetTexture("_MainTex", T("knob_bottom_rgba.png"));
        mRod.SetTexture("_MainTex", T("rod_body_rgba.png")); mHold.SetTexture("_MainTex", T("flat_modal.png"));
        mDim.SetFloat("_Mode", 3);
        AssetDatabase.SaveAssets();

        var sc = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var camGo = new GameObject("Main Camera"); camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>();
        cam.orthographic = true; cam.orthographicSize = 5.4f; cam.transform.position = new Vector3(0, 0, -10);
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black;
        cam.nearClipPlane = 0.1f; cam.farClipPlane = 50f; cam.allowHDR = false; cam.allowMSAA = false;
        var cd = camGo.AddComponent<UniversalAdditionalCameraData>(); cd.renderPostProcessing = false; cd.antialiasing = AntialiasingMode.None;

        var root = new GameObject("ScrollUnroll");
        var lPage = Layer("L0_Page", root.transform); var lDim = Layer("L1_Dim", root.transform); var lPanel = Layer("L2_Panel", root.transform);
        var lShadow = Layer("L3_RodShadow", root.transform); var lRod = Layer("L4_Rod", root.transform); var lHold = Layer("L5_HoldFlat", root.transform);

        var drv = root.AddComponent<SuDriver>();
        drv.page = Quad("Page", lPage.transform, mPage);
        drv.dim = Quad("Dim", lDim.transform, mDim);
        drv.panel = Quad("Panel", lPanel.transform, mPanel);
        drv.gap = Quad("GapShadow", lShadow.transform, mGap);
        drv.contact = Quad("ContactShadow", lShadow.transform, mContact);
        drv.knobTop = Quad("KnobTop", lRod.transform, mKT);
        drv.knobBot = Quad("KnobBottom", lRod.transform, mKB);
        drv.rod = Quad("RodBody", lRod.transform, mRod);
        drv.hold = Quad("HoldFlat", lHold.transform, mHold);
        drv.modalSet = new SuDriver.TexSet { page = T("page.png"), panel = T("panel_rgba.png"), gap = T("rod_gap_shadow_rgba.png"), knobTop = T("knob_top_rgba.png"), knobBot = T("knob_bottom_rgba.png"), rod = T("rod_body_rgba.png"), hold = T("flat_modal.png") };
        drv.pageSet = new SuDriver.TexSet { page = T("page_no_panel.png"), panel = T("panel_page_rgba.png"), gap = T("rod_gap_shadow_page_rgba.png"), knobTop = T("knob_top_page_rgba.png"), knobBot = T("knob_bottom_page_rgba.png"), rod = T("rod_body_page_rgba.png"), hold = T("flat_page.png") };

        var lay = root.AddComponent<AnnieLayers>();
        foreach (var l in new[] { lPage, lDim, lPanel, lShadow, lRod, lHold }) lay.layers.Add(new AnnieLayers.Layer { name = l.name, target = l, on = true });

        var cap = root.AddComponent<AnnieCapture>(); cap.cam = cam; cap.autoStart = false; cap.replayLayersOnStart = false; cap.frameCount = 203; cap.fps = 60;
        cap.blendSpace = AnnieCapture.BlendSpace.Gamma;
        drv.capture = cap;
        EditorSceneManager.MarkSceneDirty(sc); EditorSceneManager.SaveScene(sc, ScenePath);
        AssetDatabase.SaveAssets();
        return "built";
    }

    // a = take|outDir   (take A, B or C)
    public static string Record(string a)
    {
        var p = a.Split('|'); char take = p[0][0]; string outDir = p[1];
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var root = GameObject.Find("ScrollUnroll");
        var drv = root.GetComponent<SuDriver>(); var lay = root.GetComponent<AnnieLayers>(); var cap = root.GetComponent<AnnieCapture>();
        drv.take = take; drv.recording = true;
        foreach (var l in lay.layers) l.on = !(take == 'C' && l.name == "L1_Dim");
        lay.Apply();
        if (Directory.Exists(outDir + "/frames")) Directory.Delete(outDir + "/frames", true);
        Directory.CreateDirectory(outDir);
        if (File.Exists(outDir + "/capture.json")) File.Delete(outDir + "/capture.json");
        cap.outDir = outDir; cap.frameCount = take == 'C' ? 227 : 203; cap.fps = 60; cap.autoStart = true; cap.replayLayersOnStart = false;
        cap.blendSpace = AnnieCapture.BlendSpace.Gamma;
        string what = take == 'A' ? "modal, rod rolls (judged take)" : take == 'B' ? "modal, rod stays, panel slides out (alternate)" : "page-size panel, no dim";
        cap.caption = "TEST · ScrollUnroll take " + take + " · " + what + " · " + cap.frameCount + " frames at 60 fps";
        EditorApplication.EnterPlaymode();
        return "armed " + take;
    }
}
