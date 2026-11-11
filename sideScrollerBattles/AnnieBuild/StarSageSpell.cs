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
using Annie.StarSageSpell;

// Builder for the StarSageSpell job. Run with:
//   unity command run_script --file AnnieBuild/StarSageSpell.cs --entry StarSageSpellBuild.Build
//   unity command run_script --file AnnieBuild/StarSageSpell.cs --entry StarSageSpellBuild.Record --args "[\"name|full or one|offLayersCsv|outDir|frames\"]"
public static class StarSageSpellBuild
{
    const string Job = "Assets/Annie/StarSageSpell/";
    const string TexDir = Job + "Textures/";
    const string MatDir = Job + "Materials/";
    const string PrefDir = Job + "Prefabs/";
    const string ScenePath = Job + "Scenes/StarSageSpell.unity";
    const string LoadDir = "Assets/Characters/archer_loadouts/";
    const string RunDir = "/Users/ehrendavis/work/local-RED-dev/src/asset-animation-demo/local_runs/unity/star_sage_spell/";

    static Material unlit, additive;
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

    class PsSpec
    {
        public Texture2D tex; public int cols = 2, rows = 2; public float frameMin = 0f, frameMax = 0.99f;
        public int burst; public float rate; public float duration = 0.2f; public List<Vector2> extraBursts = new List<Vector2>();
        public Vector2 speed = new Vector2(1, 2), life = new Vector2(0.5f, 0.8f), size = new Vector2(0.3f, 0.4f);
        public Color c0 = Color.white, c1 = Color.white; public float grav;
        public Vector3 circScale = Vector3.one; public string shape = "cone"; public float cone = 30f, radius = 0.05f, thickness = 1f; public Vector3 boxScale = Vector3.one;
        public float endSize = 1f; public bool spin = true; public int order = 100; public uint seed = 1; public float fadeStart = 0.55f, fadeIn = 0f;
    }

    static ParticleSystem PS(Transform parent, string name, PsSpec s)
    {
        var go = new GameObject(name); go.transform.SetParent(parent, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.playOnAwake = false; main.loop = false; main.duration = s.duration; main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(s.life.x, s.life.y);
        main.startSpeed = new ParticleSystem.MinMaxCurve(s.speed.x, s.speed.y);
        main.startSize = new ParticleSystem.MinMaxCurve(s.size.x, s.size.y);
        main.startColor = new ParticleSystem.MinMaxGradient(s.c0, s.c1); main.gravityModifier = s.grav; main.maxParticles = 30;
        main.startRotation = s.spin ? new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f) : new ParticleSystem.MinMaxCurve(0f);
        main.scalingMode = ParticleSystemScalingMode.Local;
        var em = ps.emission; em.rateOverTime = s.rate; em.rateOverDistance = 0;
        var bursts = new List<ParticleSystem.Burst>();
        if (s.burst > 0) bursts.Add(new ParticleSystem.Burst(0f, (short)s.burst));
        foreach (var b in s.extraBursts) bursts.Add(new ParticleSystem.Burst(b.x, (short)b.y));
        em.SetBursts(bursts.ToArray());
        var sh = ps.shape;
        if (s.shape == "none") sh.enabled = false;
        else
        {
            sh.enabled = true;
            if (s.shape == "box") { sh.shapeType = ParticleSystemShapeType.Box; sh.scale = s.boxScale; }
            else if (s.shape == "circle") { sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = s.radius; sh.radiusThickness = s.thickness; sh.scale = s.circScale; }
            else { sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = s.cone; sh.radius = s.radius; sh.radiusThickness = 1f; }
        }
        var ts = ps.textureSheetAnimation; ts.enabled = true; ts.mode = ParticleSystemAnimationMode.Grid;
        ts.numTilesX = s.cols; ts.numTilesY = s.rows; ts.animation = ParticleSystemAnimationType.WholeSheet;
        ts.frameOverTime = new ParticleSystem.MinMaxCurve(0f);
        ts.startFrame = s.frameMin == s.frameMax ? new ParticleSystem.MinMaxCurve(s.frameMin) : new ParticleSystem.MinMaxCurve(s.frameMin, s.frameMax);
        var col = ps.colorOverLifetime; col.enabled = true;
        var g = new Gradient();
        var ak = new List<GradientAlphaKey>();
        if (s.fadeIn > 0f) { ak.Add(new GradientAlphaKey(0f, 0f)); ak.Add(new GradientAlphaKey(1f, s.fadeIn)); } else ak.Add(new GradientAlphaKey(1f, 0f));
        ak.Add(new GradientAlphaKey(1f, s.fadeStart)); ak.Add(new GradientAlphaKey(0f, 1f));
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, ak.ToArray());
        col.color = g;
        var sz = ps.sizeOverLifetime; sz.enabled = true;
        sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 1f, 1, s.endSize));
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Billboard; r.sortingOrder = s.order;
        r.sharedMaterial = Mat("Mat_" + s.tex.name, s.tex);
        ps.useAutoRandomSeed = false; ps.randomSeed = s.seed;
        return ps;
    }

    static SpriteRenderer SR(Transform parent, string name, int order, Material m)
    {
        var go = new GameObject(name); go.transform.SetParent(parent, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sharedMaterial = m; sr.sortingOrder = order; sr.enabled = false;
        return sr;
    }

    static GameObject Child(Transform parent, string name) { var g = new GameObject(name); g.transform.SetParent(parent, false); return g; }

    static Light2D Light(Transform parent, float outer)
    {
        var lt = parent.gameObject.AddComponent<Light2D>(); lt.lightType = Light2D.LightType.Point; lt.intensity = 0f; lt.color = SsUtil.Cyan;
        lt.pointLightOuterRadius = outer; lt.pointLightInnerRadius = 0f; lt.falloffIntensity = 0.5f; lt.shadowsEnabled = false; lt.enabled = false;
        return lt;
    }

    static AnnieLayers.Layer L(string n, GameObject g) { return new AnnieLayers.Layer { name = n, target = g }; }

    const int SparkTile = 0;   // set by trial: tile 1 (top right) is the four-point star

    static PsSpec Inward(Texture2D bits, Color a, Color b, int count, int order, uint seed)
    {
        return new PsSpec { tex = bits, shape = "circle", radius = 0.8f, thickness = 0.25f, rate = count / 0.2f, duration = 0.2f, speed = new Vector2(-3.5f, -3.5f), life = new Vector2(0.2f, 0.2f),
            size = new Vector2(0.28f, 0.36f), c0 = a, c1 = b, frameMin = 0.3f, frameMax = 0.3f, fadeIn = 0.15f, fadeStart = 0.7f, endSize = 0.7f, order = order, seed = seed };
    }

    static GameObject BuildCast()
    {
        var root = new GameObject("Fx_StarCast");
        var fx = root.AddComponent<SsCast>(); var lay = root.AddComponent<AnnieLayers>(); fx.layers = lay; fx.glowTex = T("glow_soft_white");
        var bits = T("spark_bits_white");
        var l1 = Child(root.transform, "L1_Flash"); fx.flash = SR(l1.transform, "flash", 1950, additive);
        var l2 = Child(root.transform, "L2_Core");
        fx.glowOuter = SR(l2.transform, "glow_cyan", 1910, additive); fx.glowInner = SR(l2.transform, "glow_white", 1911, additive);
        var l3f = Child(root.transform, "L3_ParticlesFront");
        fx.front = PS(l3f.transform, "sparkles_in", Inward(bits, SsUtil.Gold, Color.white, 13, 1912, fx.seedFront));
        var l3b = Child(root.transform, "L3_ParticlesBack");
        fx.back = PS(l3b.transform, "sparkles_violet", Inward(bits, SsUtil.Violet, SsUtil.Violet, 8, 99, fx.seedBack));
        var l5 = Child(root.transform, "L5_Linger");
        fx.linger = PS(l5.transform, "motes_gold", new PsSpec { tex = bits, burst = 5, duration = 0.1f, cone = 80f, radius = 0.05f, speed = new Vector2(0.9f, 1.5f), life = new Vector2(0.55f, 0.65f),
            size = new Vector2(0.22f, 0.28f), c0 = SsUtil.Gold, c1 = Color.white, frameMin = 0.3f, frameMax = 0.3f, grav = -0.1f, fadeStart = 0.5f, endSize = 0.6f, order = 1905, seed = fx.seedLinger });
        var l6 = Child(root.transform, "L6_Light"); fx.light2d = Light(l6.transform, 3.0f);
        lay.layers = new List<AnnieLayers.Layer> { L("Flash", l1), L("Core", l2), L("ParticlesFront", l3f), L("ParticlesBack", l3b), L("Linger", l5), L("Light", l6) };
        var pf = PrefabUtility.SaveAsPrefabAsset(root, PrefDir + "Fx_StarCast.prefab");
        UnityEngine.Object.DestroyImmediate(root);
        return pf;
    }

    static GameObject BuildBolt()
    {
        var root = new GameObject("Fx_StarBolt");
        var fx = root.AddComponent<SsBolt>(); var lay = root.AddComponent<AnnieLayers>(); fx.layers = lay; fx.starTex = T("star_core");
        var bits = T("spark_bits_white");
        var l2 = Child(root.transform, "L2_Core"); fx.core = SR(l2.transform, "star", 1920, unlit);
        var l5 = Child(root.transform, "L5_Trail");
        var head = Child(l5.transform, "TrailHead"); fx.trailHead = head.transform;
        var tr = head.AddComponent<TrailRenderer>();
        tr.sharedMaterial = Mat("Mat_wind_trail_white_trail", T("wind_trail_white"));
        tr.time = 0.0502f; tr.minVertexDistance = 0.02f; tr.widthMultiplier = 0.2f; tr.textureMode = LineTextureMode.Stretch;
        tr.widthCurve = new AnimationCurve(new Keyframe(0, 1), new Keyframe(1, 0.25f));
        tr.sortingOrder = 1915; tr.emitting = false; tr.alignment = LineAlignment.View;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(SsUtil.Cyan, 0), new GradientColorKey(SsUtil.Violet, 1) },
                  new[] { new GradientAlphaKey(0.35f, 0f), new GradientAlphaKey(0.2f, 0.5f), new GradientAlphaKey(0f, 1f) });
        tr.colorGradient = g; fx.trail = tr;
        var l3f = Child(root.transform, "L3_ParticlesFront");
        fx.sparkles = PS(l3f.transform, "sparkles_shed", new PsSpec { tex = bits, shape = "none", rate = 0, duration = 1f, speed = new Vector2(0, 0), life = new Vector2(0.33f, 0.43f),
            size = new Vector2(0.2f, 0.28f), c0 = SsUtil.Gold, c1 = SsUtil.Gold, frameMin = 0.3f, frameMax = 0.3f, fadeStart = 0.5f, endSize = 0.6f, order = 1922, seed = fx.seedSparkles });
        var l6 = Child(root.transform, "L6_Light"); fx.light2d = Light(l6.transform, 2.0f);
        lay.layers = new List<AnnieLayers.Layer> { L("Core", l2), L("Trail", l5), L("ParticlesFront", l3f), L("Light", l6) };
        var pf = PrefabUtility.SaveAsPrefabAsset(root, PrefDir + "Fx_StarBolt.prefab");
        UnityEngine.Object.DestroyImmediate(root);
        return pf;
    }

    static GameObject BuildImpact()
    {
        var root = new GameObject("Fx_StarImpact");
        var fx = root.AddComponent<SsImpact>(); var lay = root.AddComponent<AnnieLayers>(); fx.layers = lay;
        fx.glowTex = T("glow_soft_white"); fx.starTex = T("star_core"); fx.ringTex = T("shockwave_ring_white");
        var bits = T("spark_bits_white");
        var l1 = Child(root.transform, "L1_Flash"); fx.flash = SR(l1.transform, "flash", 1950, additive);
        var l2 = Child(root.transform, "L2_Core"); fx.core = SR(l2.transform, "burst", 1920, unlit);
        var l3f = Child(root.transform, "L3_ParticlesFront");
        fx.front = PS(l3f.transform, "shards_front", new PsSpec { tex = bits, burst = 10, duration = 0.1f, cone = 30f, radius = 0.08f, speed = new Vector2(1.9f, 3.1f), life = new Vector2(0.28f, 0.30f),
            size = new Vector2(0.26f, 0.38f), c0 = SsUtil.Gold, c1 = Color.white, fadeStart = 0.55f, endSize = 0.7f, order = 1925, seed = fx.seedFront });
        var l3b = Child(root.transform, "L3_ParticlesBack");
        fx.back = PS(l3b.transform, "shards_back", new PsSpec { tex = bits, burst = 9, duration = 0.1f, cone = 28f, radius = 0.12f, speed = new Vector2(2.4f, 4.0f), life = new Vector2(0.29f, 0.31f),
            size = new Vector2(0.26f, 0.38f), c0 = SsUtil.Cyan, c1 = SsUtil.Cyan, fadeStart = 0.55f, endSize = 0.7f, order = 99, seed = fx.seedBack });
        var l4 = Child(root.transform, "L4_Shockwave"); fx.ring = SR(l4.transform, "ring", 1905, unlit);
        var l5 = Child(root.transform, "L5_Linger");
        fx.linger = PS(l5.transform, "motes", new PsSpec { tex = bits, burst = 4, duration = 0.4f, extraBursts = { new Vector2(0.1f, 3), new Vector2(0.2f, 3), new Vector2(0.3f, 2) },
            shape = "circle", radius = 1.2f, thickness = 0.3f, circScale = new Vector3(1f, 0.8f, 1f), speed = new Vector2(0.1f, 0.25f), life = new Vector2(0.7f, 0.95f),
            size = new Vector2(0.18f, 0.24f), c0 = SsUtil.Gold, c1 = Color.white, frameMin = 0.3f, frameMax = 0.3f, grav = -0.2f, fadeStart = 0.82f, endSize = 0.8f, order = 1900, seed = fx.seedLinger });
        var l6 = Child(root.transform, "L6_Light"); fx.light2d = Light(l6.transform, 4.0f);
        lay.layers = new List<AnnieLayers.Layer> { L("Flash", l1), L("Core", l2), L("ParticlesFront", l3f), L("ParticlesBack", l3b), L("Shockwave", l4), L("Linger", l5), L("Light", l6) };
        var pf = PrefabUtility.SaveAsPrefabAsset(root, PrefDir + "Fx_StarImpact.prefab");
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
        additive = unlit;   // alpha blend: the legacy additive shader dimmed the white flash on the tan ground
        var pCast = BuildCast(); var pBolt = BuildBolt(); var pImpact = BuildImpact();

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

        var profPath = Job + "SsBloomProfile.asset";
        AssetDatabase.DeleteAsset(profPath);
        var prof = ScriptableObject.CreateInstance<VolumeProfile>();
        AssetDatabase.CreateAsset(prof, profPath);
        var bloom = prof.Add<Bloom>(true);
        bloom.threshold.Override(0.97f); bloom.intensity.Override(0.7f); bloom.scatter.Override(0.5f); bloom.active = true;
        EditorUtility.SetDirty(prof); AssetDatabase.SaveAssets();
        var vg = new GameObject("Bloom"); var vol = vg.AddComponent<Volume>(); vol.isGlobal = true; vol.priority = 10; vol.sharedProfile = prof;

        var rootGo = new GameObject("StarSageScene");
        var dir = rootGo.AddComponent<SsDirector>();
        var sceneLayers = rootGo.AddComponent<AnnieLayers>();
        sceneLayers.layers = new List<AnnieLayers.Layer> { new AnnieLayers.Layer { name = "Bloom", target = vg, on = false } }; vg.SetActive(false);
        var capture = rootGo.AddComponent<AnnieCapture>();
        capture.cam = cam; capture.outDir = ""; capture.caption = ""; capture.frameCount = 90; capture.fps = 30; capture.replayLayersOnStart = false; capture.autoStart = false;

        // the caster: the user-picked relaxed still, feet at (430, 1040), scaled to the Archer's opaque height
        var cg = new GameObject("StarSage_still"); cg.transform.SetParent(rootGo.transform, false);
        var csr = cg.AddComponent<SpriteRenderer>(); csr.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(TexDir + "K0_starsage.png"); csr.sharedMaterial = unlit; csr.sortingOrder = 100;
        cg.transform.position = SsGeo.PxToWorld(SsGeo.CasterCentre); cg.transform.localScale = Vector3.one * SsGeo.CasterScale;

        dir.sheets = new SsSheets
        {
            idleBody = Sheet("idle_body_forest.png"), idleBow = Sheet("robes_idle_weapon_wood.png"),
            hurtBody = Sheet("hit_body_forest.png"), hurtBow = Sheet("robes_hit_weapon_wood.png")
        };
        var fxParent = new GameObject("Effects"); fxParent.transform.SetParent(rootGo.transform, false);
        dir.cast = ((GameObject)PrefabUtility.InstantiatePrefab(pCast, fxParent.transform)).GetComponent<SsCast>();
        dir.bolt = ((GameObject)PrefabUtility.InstantiatePrefab(pBolt, fxParent.transform)).GetComponent<SsBolt>();
        dir.impact = ((GameObject)PrefabUtility.InstantiatePrefab(pImpact, fxParent.transform)).GetComponent<SsImpact>();
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        return MeshCoverage(dir.sheets);
    }

    static string MeshCoverage(SsSheets sh)
    {
        var all = new List<Sprite>(); foreach (var a in new[] { sh.idleBody, sh.idleBow, sh.hurtBody, sh.hurtBow }) all.AddRange(a);
        float min = 9; int bad = 0;
        foreach (var s in all)
        {
            var v = s.vertices; var tri = s.triangles; float area = 0;
            for (int i = 0; i < tri.Length; i += 3)
            {
                Vector2 a = v[tri[i]], b = v[tri[i + 1]], c = v[tri[i + 2]];
                area += Mathf.Abs((b.x - a.x) * (c.y - a.y) - (c.x - a.x) * (b.y - a.y)) * 0.5f;
            }
            float full = s.rect.width * s.rect.height / s.pixelsPerUnit / s.pixelsPerUnit;
            float cov = area / full; min = Mathf.Min(min, cov); if (cov < 0.999f) bad++;
        }
        string r = "mesh coverage: sprites=" + all.Count + " min=" + min.ToString("F4") + " below0.999=" + bad;
        Directory.CreateDirectory(RunDir);
        File.WriteAllText(RunDir + "mesh_coverage.txt", r + "\n");
        return r;
    }

    // args: name|full or one|offLayersCsv|outDir|frames   (an off layer name is switched off on every prefab that has it)
    public static string Record(string a)
    {
        var p = a.Split('|');
        string name = p[0], take = p[1], off = p[2], outDir = p[3]; int frames = int.Parse(p[4]);
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var dir = UnityEngine.Object.FindAnyObjectByType<SsDirector>();
        var cap = dir.GetComponent<AnnieCapture>();
        dir.take = take;
        var offSet = new HashSet<string>(off.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
        foreach (var lay in new[] { dir.cast.layers, dir.bolt.layers, dir.impact.layers })
        {
            foreach (var l in lay.layers) l.on = !offSet.Contains(l.name);
            lay.Apply(); EditorUtility.SetDirty(lay); PrefabUtility.RecordPrefabInstancePropertyModifications(lay);
        }
        var sl = dir.GetComponent<AnnieLayers>();
        foreach (var l in sl.layers) l.on = false;   // Bloom is off in every take
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
