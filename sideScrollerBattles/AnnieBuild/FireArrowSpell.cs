using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Annie;
using Annie.FireArrowSpell;

// Builder for the FireArrowSpell job. Run with:
//   unity command run_script --file AnnieBuild/FireArrowSpell.cs --entry FireArrowSpellBuild.Build
//   unity command run_script --file AnnieBuild/FireArrowSpell.cs --entry FireArrowSpellBuild.Record --args "[\"name|full or one or bg|offLayersCsv|outDir|frames\"]"
public static class FireArrowSpellBuild
{
    const string Job = "Assets/Annie/FireArrowSpell/";
    const string TexDir = Job + "Textures/";
    const string MatDir = Job + "Materials/";
    const string PrefDir = Job + "Prefabs/";
    const string ScenePath = Job + "Scenes/FireArrowSpell.unity";
    const string LoadDir = "Assets/Characters/archer_loadouts/";
    static Material unlit;
    static Texture2D T(string n) { return AssetDatabase.LoadAssetAtPath<Texture2D>(TexDir + n + ".png"); }

    static void ImportAll()
    {
        foreach (var f in Directory.GetFiles(TexDir, "*.png"))
        {
            string path = f.Replace('\\', '/');
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            string n = Path.GetFileNameWithoutExtension(path);
            bool isSprite = n == "backdrop" || n == "foreground" || n == "K0_starsage";
            ti.textureType = isSprite ? TextureImporterType.Sprite : TextureImporterType.Default;
            if (isSprite)
            {
                ti.spriteImportMode = SpriteImportMode.Single; ti.spritePixelsPerUnit = 100;
                var st = new TextureImporterSettings(); ti.ReadTextureSettings(st);
                st.spriteMeshType = SpriteMeshType.FullRect; st.spriteAlignment = (int)SpriteAlignment.Center; ti.SetTextureSettings(st);
            }
            ti.alphaSource = TextureImporterAlphaSource.FromInput; ti.alphaIsTransparency = true;
            ti.mipmapEnabled = false; ti.wrapMode = TextureWrapMode.Clamp; ti.filterMode = FilterMode.Bilinear;
            ti.textureCompression = TextureImporterCompression.Uncompressed; ti.npotScale = TextureImporterNPOTScale.None;
            ti.maxTextureSize = 4096; ti.isReadable = false;
            ti.SaveAndReimport();
        }
    }

    static Material Mat(string name, Texture2D tex, string shader = "Sprites/Default")
    {
        string p = MatDir + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(p);
        if (m == null) { m = new Material(Shader.Find(shader)); AssetDatabase.CreateAsset(m, p); }
        m.shader = Shader.Find(shader); m.mainTexture = tex; m.name = name;
        EditorUtility.SetDirty(m);
        return m;
    }

    // a Particle System used as a renderer only: FaPs sets every particle each frame (simulation speed 0, emission off)
    static ParticleSystem PS(Transform parent, string name, Texture2D tex, int cols, int rows, float frameMin, float frameMax, bool animate, int order, uint seed)
    {
        var go = new GameObject(name); go.transform.SetParent(parent, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.playOnAwake = true; main.loop = true; main.duration = 5f; main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = 1f; main.startSpeed = 0f; main.startSize = 0.3f; main.maxParticles = 256; main.simulationSpeed = 0f;
        main.scalingMode = ParticleSystemScalingMode.Local;
        var em = ps.emission; em.enabled = false;
        var sh = ps.shape; sh.enabled = false;
        var ts = ps.textureSheetAnimation; ts.enabled = true; ts.mode = ParticleSystemAnimationMode.Grid;
        ts.numTilesX = cols; ts.numTilesY = rows; ts.animation = ParticleSystemAnimationType.WholeSheet;
        ts.frameOverTime = animate ? new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0, 1, 1)) : new ParticleSystem.MinMaxCurve(0f);
        ts.cycleCount = 1;
        ts.startFrame = frameMin == frameMax ? new ParticleSystem.MinMaxCurve(frameMin) : new ParticleSystem.MinMaxCurve(frameMin, frameMax);
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Billboard; r.sortingOrder = order;
        r.sharedMaterial = Mat("Mat_" + tex.name, tex);
        ps.useAutoRandomSeed = false; ps.randomSeed = seed;
        return ps;
    }

    static SpriteRenderer SR(Transform parent, string name, int order)
    {
        var go = new GameObject(name); go.transform.SetParent(parent, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sharedMaterial = unlit; sr.sortingOrder = order; sr.enabled = false;
        return sr;
    }
    static GameObject Child(Transform parent, string name) { var g = new GameObject(name); g.transform.SetParent(parent, false); return g; }
    static Light2D Light(Transform parent, float outer, Color c)
    {
        var lt = parent.gameObject.AddComponent<Light2D>(); lt.lightType = Light2D.LightType.Point; lt.intensity = 0f; lt.color = c;
        lt.pointLightOuterRadius = outer; lt.pointLightInnerRadius = 0f; lt.falloffIntensity = 0.5f; lt.shadowsEnabled = false; lt.enabled = false;
        return lt;
    }
    static AnnieLayers.Layer L(string n, GameObject g) { return new AnnieLayers.Layer { name = n, target = g }; }

    static GameObject BuildCast()
    {
        var root = new GameObject("Fx_FireCast");
        var fx = root.AddComponent<FaCast>(); var lay = root.AddComponent<AnnieLayers>(); fx.layers = lay;
        fx.glowTex = T("glow_soft_white"); fx.wispTex = T("fire_wisp"); fx.wispWhiteTex = T("fire_wisp_white"); fx.emberWhiteTex = T("ember_white");
        var l2 = Child(root.transform, "L2_Core");
        fx.glowOrange = SR(l2.transform, "glow_orange", 1910);
        fx.flames = new[] { SR(l2.transform, "flame_a", 1911), SR(l2.transform, "flame_b", 1912), SR(l2.transform, "flame_c", 1913), SR(l2.transform, "flame_d", 1917) };
        fx.centreY = SR(l2.transform, "centre_yellow", 1914); fx.centreW = SR(l2.transform, "centre_white", 1915);
        var l3f = Child(root.transform, "L3_ParticlesFront");
        fx.wispsPs = PS(l3f.transform, "wisps_in", T("fire_wisp_white"), 4, 4, 0f, 1f, true, 1916, fx.seedFront);
        var l5 = Child(root.transform, "L5_Linger");
        fx.embersPs = PS(l5.transform, "embers_drop", T("ember_white"), 2, 1, 0f, 0.99f, false, 98, fx.seedLinger);
        var l6 = Child(root.transform, "L6_Light"); fx.light2d = Light(l6.transform, 2.0f, FaUtil.C(255, 170, 80));
        lay.layers = new List<AnnieLayers.Layer> { L("Core", l2), L("ParticlesFront", l3f), L("Linger", l5), L("Light", l6) };
        var pf = PrefabUtility.SaveAsPrefabAsset(root, PrefDir + "Fx_FireCast.prefab");
        UnityEngine.Object.DestroyImmediate(root);
        return pf;
    }

    static GameObject BuildArrow()
    {
        var root = new GameObject("Fx_FireArrow");
        var fx = root.AddComponent<FaArrow>(); var lay = root.AddComponent<AnnieLayers>(); fx.layers = lay;
        fx.coreTex = T("fire_arrow_core"); fx.coreWhiteTex = T("fire_arrow_core_white"); fx.glowTex = T("glow_soft_white"); fx.wispWhiteTex = T("fire_wisp_white"); fx.emberWhiteTex = T("ember_white");
        var l2 = Child(root.transform, "L2_Core");
        fx.halo = SR(l2.transform, "head_halo", 1920); fx.head = SR(l2.transform, "head", 1921); fx.headGlow = SR(l2.transform, "head_glow", 1922);
        var l5 = Child(root.transform, "L5_Trail");
        var head = Child(l5.transform, "TrailHead"); fx.trailHead = head.transform;
        var tr = head.AddComponent<TrailRenderer>();
        tr.sharedMaterial = Mat("Mat_light_beam_white_trail", T("light_beam_white"));
        tr.time = FaArrow.TrailTime; tr.minVertexDistance = 0.02f; tr.widthMultiplier = 0.32f; tr.textureMode = LineTextureMode.Stretch;
        tr.widthCurve = new AnimationCurve(new Keyframe(0, 1), new Keyframe(0.6f, 0.55f), new Keyframe(1, 0f));
        tr.sortingOrder = 1925; tr.emitting = false; tr.alignment = LineAlignment.View;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(FaUtil.YellowOrange, 0), new GradientColorKey(FaUtil.Orange, 0.45f), new GradientColorKey(FaUtil.RedOrange, 1) },
                  new[] { new GradientAlphaKey(0.8f, 0f), new GradientAlphaKey(0.6f, 0.5f), new GradientAlphaKey(0.3f, 0.85f), new GradientAlphaKey(0f, 1f) });
        tr.colorGradient = g; fx.trail = tr;
        fx.strandAPs = PS(l5.transform, "strand_a", T("fire_wisp_white"), 4, 4, 0f, 1f, true, 1926, fx.seedStrandA);
        fx.strandBPs = PS(l5.transform, "strand_b", T("fire_wisp_white"), 4, 4, 0f, 1f, true, 1927, fx.seedStrandB);
        var l5l = Child(root.transform, "L5_Linger");
        fx.lingerPs = PS(l5l.transform, "bits_shed", T("ember_white"), 2, 1, 0f, 0.99f, false, 99, fx.seedLinger);
        var l6 = Child(root.transform, "L6_Light"); fx.light2d = Light(l6.transform, 1.2f, FaUtil.C(255, 170, 80));
        lay.layers = new List<AnnieLayers.Layer> { L("Core", l2), L("Trail", l5), L("Linger", l5l), L("Light", l6) };
        var pf = PrefabUtility.SaveAsPrefabAsset(root, PrefDir + "Fx_FireArrow.prefab");
        UnityEngine.Object.DestroyImmediate(root);
        return pf;
    }

    static GameObject BuildImpact()
    {
        var root = new GameObject("Fx_FireImpact");
        var fx = root.AddComponent<FaImpact>(); var lay = root.AddComponent<AnnieLayers>(); fx.layers = lay;
        fx.burstTex = T("fire_burst"); fx.burstWhiteTex = T("fire_burst_white"); fx.glowTex = T("glow_soft_white"); fx.wispTex = T("fire_wisp"); fx.wispWhiteTex = T("fire_wisp_white"); fx.sparkTex = T("spark_bits_white"); fx.emberWhiteTex = T("ember_white");
        var l1 = Child(root.transform, "L1_Flash"); fx.flash = SR(l1.transform, "flash", 1950);
        var l2 = Child(root.transform, "L2_Core");
        fx.coreGlow = SR(l2.transform, "burst_glow", 1930);
        fx.burst = SR(l2.transform, "burst_flipbook", 1931);
        fx.coreCentreY = SR(l2.transform, "burst_centre_yellow", 1938); fx.coreCentreW = SR(l2.transform, "burst_centre_white", 1939);
        var l3f = Child(root.transform, "L3_ParticlesFront");
        fx.frontWispPs = PS(l3f.transform, "front_wisps", T("fire_wisp_white"), 4, 4, 0f, 1f, true, 1940, fx.seedFront);
        fx.frontSparkPs = PS(l3f.transform, "front_sparks", T("spark_bits_white"), 2, 2, 0.3f, 0.3f, false, 1941, fx.seedFront);
        var l3b = Child(root.transform, "L3_ParticlesBack");
        fx.backWispPs = PS(l3b.transform, "back_wisps", T("fire_wisp_white"), 4, 4, 0f, 1f, true, 97, fx.seedBack);
        fx.backSparkPs = PS(l3b.transform, "back_sparks", T("spark_bits_white"), 2, 2, 0.3f, 0.3f, false, 96, fx.seedBack);
        var l5 = Child(root.transform, "L5_Linger");
        fx.lingerPs = PS(l5.transform, "embers", T("ember_white"), 2, 1, 0f, 0.99f, false, 98, fx.seedLinger);
        var l6 = Child(root.transform, "L6_Light"); fx.light2d = Light(l6.transform, 4.05f, new Color(1f, 0.78f, 0.5f));
        lay.layers = new List<AnnieLayers.Layer> { L("Flash", l1), L("Core", l2), L("ParticlesFront", l3f), L("ParticlesBack", l3b), L("Linger", l5), L("Light", l6) };
        var pf = PrefabUtility.SaveAsPrefabAsset(root, PrefDir + "Fx_FireImpact.prefab");
        UnityEngine.Object.DestroyImmediate(root);
        return pf;
    }

    static Sprite[] Sheet(string file) { return AssetDatabase.LoadAllAssetsAtPath(LoadDir + file).OfType<Sprite>().OrderBy(s => s.name).ToArray(); }

    public static string Build()
    {
        foreach (var d in new[] { Job, TexDir, MatDir, PrefDir, Job + "Scenes/" }) if (!AssetDatabase.IsValidFolder(d.TrimEnd('/'))) Directory.CreateDirectory(d);
        AssetDatabase.Refresh();
        ImportAll();
        unlit = Mat("Mat_SpriteUnlit", null);
        var pCast = BuildCast(); var pArrow = BuildArrow(); var pImpact = BuildImpact();

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var camGo = new GameObject("Main Camera"); camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>(); cam.orthographic = true; cam.orthographicSize = 5.4f; cam.transform.position = new Vector3(0, 0, -10);
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black; cam.nearClipPlane = 0.1f; cam.farClipPlane = 50f;
        var cd = camGo.AddComponent<UniversalAdditionalCameraData>(); cd.renderPostProcessing = true; cd.antialiasing = AntialiasingMode.None;
        var gl = new GameObject("GlobalLight2D"); var g2 = gl.AddComponent<Light2D>(); g2.lightType = Light2D.LightType.Global; g2.intensity = 1f;
        var bd = new GameObject("Backdrop"); var bsr = bd.AddComponent<SpriteRenderer>();
        bsr.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(TexDir + "backdrop.png"); bsr.sortingOrder = -100;
        var fg = new GameObject("Foreground"); var fsr = fg.AddComponent<SpriteRenderer>();
        fsr.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(TexDir + "foreground.png"); fsr.sortingOrder = 1000;

        var profPath = Job + "FaBloomProfile.asset";
        AssetDatabase.DeleteAsset(profPath);
        var prof = ScriptableObject.CreateInstance<VolumeProfile>();
        AssetDatabase.CreateAsset(prof, profPath);
        var bloom = prof.Add<Bloom>(true);
        bloom.threshold.Override(0.97f); bloom.intensity.Override(0.7f); bloom.scatter.Override(0.5f); bloom.active = true;
        EditorUtility.SetDirty(prof); AssetDatabase.SaveAssets();
        var vg = new GameObject("Bloom"); var vol = vg.AddComponent<Volume>(); vol.isGlobal = true; vol.priority = 10; vol.sharedProfile = prof;

        var rootGo = new GameObject("FireArrowScene");
        var dir = rootGo.AddComponent<FaDirector>();
        var sceneLayers = rootGo.AddComponent<AnnieLayers>();
        sceneLayers.layers = new List<AnnieLayers.Layer> { new AnnieLayers.Layer { name = "Bloom", target = vg, on = false } }; vg.SetActive(false);
        var capture = rootGo.AddComponent<AnnieCapture>();
        capture.cam = cam; capture.outDir = ""; capture.caption = ""; capture.frameCount = 250; capture.fps = 30; capture.replayLayersOnStart = false; capture.autoStart = false;

        var cg = new GameObject("StarSage_still"); cg.transform.SetParent(rootGo.transform, false);
        var csr = cg.AddComponent<SpriteRenderer>(); csr.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(TexDir + "K0_starsage.png"); csr.sharedMaterial = unlit; csr.sortingOrder = 100;
        cg.transform.position = FaGeo.PxToWorld(FaGeo.CasterCentre); cg.transform.localScale = Vector3.one * FaGeo.CasterScale;

        dir.sheets = new FaSheets
        {
            idleBody = Sheet("idle_body_forest.png"), idleBow = Sheet("robes_idle_weapon_wood.png"),
            hurtBody = Sheet("hit_body_forest.png"), hurtBow = Sheet("robes_hit_weapon_wood.png")
        };
        var fxParent = new GameObject("Effects"); fxParent.transform.SetParent(rootGo.transform, false);
        dir.cast = ((GameObject)PrefabUtility.InstantiatePrefab(pCast, fxParent.transform)).GetComponent<FaCast>();
        dir.arrow = ((GameObject)PrefabUtility.InstantiatePrefab(pArrow, fxParent.transform)).GetComponent<FaArrow>();
        dir.impact = ((GameObject)PrefabUtility.InstantiatePrefab(pImpact, fxParent.transform)).GetComponent<FaImpact>();
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        return "built";
    }

    // args: name|full or one or bg|offLayersCsv|outDir|frames   (an off layer name is switched off on every prefab that has it)
    public static string Record(string a)
    {
        var p = a.Split('|');
        string name = p[0], take = p[1], off = p[2], outDir = p[3]; int frames = int.Parse(p[4]);
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var dir = UnityEngine.Object.FindAnyObjectByType<FaDirector>();
        var cap = dir.GetComponent<AnnieCapture>();
        dir.take = take;
        var offSet = new HashSet<string>(off.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
        foreach (var lay in new[] { dir.cast.layers, dir.arrow.layers, dir.impact.layers })
        {
            foreach (var l in lay.layers) l.on = !offSet.Contains(l.name);
            lay.Apply(); EditorUtility.SetDirty(lay); PrefabUtility.RecordPrefabInstancePropertyModifications(lay);
        }
        var sl = dir.GetComponent<AnnieLayers>();
        foreach (var l in sl.layers) l.on = false;   // Bloom is built and off in every take
        sl.Apply(); EditorUtility.SetDirty(sl);
        EditorUtility.SetDirty(dir); EditorUtility.SetDirty(cap);
        if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
        Directory.CreateDirectory(outDir);
        cap.outDir = outDir; cap.caption = "TEST"; cap.autoStart = true; cap.replayLayersOnStart = false; cap.fps = 30; cap.frameCount = frames;
        EditorApplication.EnterPlaymode();
        return "armed " + name + " take=" + take + " layersOff=[" + off + "]";
    }

    public static string OpenScene() { EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single); return "opened"; }
}
