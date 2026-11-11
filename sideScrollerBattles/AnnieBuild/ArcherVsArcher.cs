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
using Annie.ArcherVsArcher;

// Builder for the ArcherVsArcher job. Run with:
//   unity command run_script --file AnnieBuild/ArcherVsArcher.cs --entry ArcherVsArcherBuild.Build
public static class ArcherVsArcherBuild
{
    const string Job = "Assets/Annie/ArcherVsArcher/";
    const string TexDir = Job + "Textures/";
    const string MatDir = Job + "Materials/";
    const string PrefDir = Job + "Prefabs/";
    const string ScenePath = Job + "Scenes/ArcherVsArcher.unity";
    const string LoadDir = "Assets/Characters/archer_loadouts/";
    const string RunDir = "/Users/ehrendavis/work/local-RED-dev/src/asset-animation-demo/local_runs/unity/archer_vs_archer/";

    static Material unlit;
    static Texture2D T(string n) { return AssetDatabase.LoadAssetAtPath<Texture2D>(TexDir + n + ".png"); }

    // ------------------------------------------------------------------ textures
    static void ImportAll()
    {
        foreach (var f in Directory.GetFiles(TexDir, "*.png"))
        {
            string path = f.Replace('\\', '/');
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            string n = Path.GetFileNameWithoutExtension(path);
            bool isBg = n == "backdrop" || n == "foreground";
            ti.textureType = isBg ? TextureImporterType.Sprite : TextureImporterType.Default;
            if (isBg)
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

    static Material Mat(string name, Texture2D tex)
    {
        string p = MatDir + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(p);
        if (m == null) { m = new Material(Shader.Find("Sprites/Default")); AssetDatabase.CreateAsset(m, p); }
        m.shader = Shader.Find("Sprites/Default"); m.mainTexture = tex; m.name = name;
        EditorUtility.SetDirty(m);
        return m;
    }

    // ------------------------------------------------------------------ particle helper
    class PsSpec
    {
        public Texture2D tex; public int cols = 1, rows = 1; public bool frameOverLife;
        public int burst; public float rateDist, rateTime, duration = 0.5f;
        public Vector2 speed = new Vector2(1, 2), life = new Vector2(0.5f, 0.8f), size = new Vector2(0.33f, 0.51f);
        public Color color = Color.white; public float grav; public float cone = 20f, radius = 0.05f;
        public bool box; public Vector3 boxScale = Vector3.one; public float endSize = 0.3f; public bool spin = true;
        public int order = 2010; public uint seed = 1; public bool alignLocal; 
        public List<Vector2> extraBursts = new List<Vector2>(); // (time, count)
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
        main.startColor = s.color; main.gravityModifier = s.grav; main.maxParticles = 300;
        main.startRotation = s.spin ? new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f) : new ParticleSystem.MinMaxCurve(0f);
        main.scalingMode = ParticleSystemScalingMode.Local;
        var em = ps.emission; em.rateOverTime = s.rateTime; em.rateOverDistance = s.rateDist;
        var bursts = new List<ParticleSystem.Burst>();
        if (s.burst > 0) bursts.Add(new ParticleSystem.Burst(0f, (short)s.burst));
        foreach (var b in s.extraBursts) bursts.Add(new ParticleSystem.Burst(b.x, (short)b.y));
        em.SetBursts(bursts.ToArray());
        var sh = ps.shape; sh.enabled = true;
        if (s.box) { sh.shapeType = ParticleSystemShapeType.Box; sh.scale = s.boxScale; }
        else { sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = s.cone; sh.radius = s.radius; sh.radiusThickness = 1f; }
        if (s.cols * s.rows > 1)
        {
            var ts = ps.textureSheetAnimation; ts.enabled = true; ts.mode = ParticleSystemAnimationMode.Grid;
            ts.numTilesX = s.cols; ts.numTilesY = s.rows; ts.animation = ParticleSystemAnimationType.WholeSheet;
            if (s.frameOverLife) { ts.frameOverTime = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0, 1, 1)); ts.startFrame = new ParticleSystem.MinMaxCurve(0f); }
            else { ts.frameOverTime = new ParticleSystem.MinMaxCurve(0f); ts.startFrame = new ParticleSystem.MinMaxCurve(0f, 0.99f); }
        }
        var col = ps.colorOverLifetime; col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                  new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.55f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
        var sz = ps.sizeOverLifetime; sz.enabled = true;
        sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 1f, 1, s.endSize));
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Billboard; r.sortingOrder = s.order;
        r.sharedMaterial = Mat("Mat_" + s.tex.name, s.tex);
        if (s.alignLocal) r.alignment = ParticleSystemRenderSpace.Local;
        ps.useAutoRandomSeed = false; ps.randomSeed = s.seed;
        return ps;
    }

    static SpriteRenderer SR(Transform parent, string name, Texture2D tex, int order)
    {
        var go = new GameObject(name); go.transform.SetParent(parent, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sharedMaterial = unlit; sr.sortingOrder = order;
        return sr;
    }

    // ------------------------------------------------------------------ effect prefab
    class FxSpec
    {
        public string name;
        public PsSpec front, back, path; public PsSpec[] linger; public bool lingerAtFeet, pathOnGround;
        public Texture2D coreTex; public int coreCols = 1, coreRows = 1, coreFrames = 1, coreMode; public float coreScale = 0.594f; public int shards;
        public Texture2D shardTex;
        public Texture2D trailTex; public Color trailColor = Color.white; // null trailTex = no TrailRenderer
        public Color ring = Color.white, light = Color.white; public float lightPeak = 1f;
    }

    static PsSpec Bits(Texture2D tex, int cols, int rows, Color c, bool front, uint seed, float gravity = 0.3f, Vector2? size = null)
    {
        return new PsSpec
        {
            tex = tex, cols = cols, rows = rows, color = c, burst = front ? 18 : 22, duration = 0.1f, cone = front ? 24f : 18f,
            speed = front ? new Vector2(5.5f, 9f) : new Vector2(6.5f, 10f), life = front ? new Vector2(0.55f, 0.8f) : new Vector2(0.6f, 0.85f),
            size = size ?? new Vector2(0.33f, 0.57f), grav = gravity, endSize = 0.25f, order = front ? 2010 : 95, seed = seed
        };
    }

    static GameObject BuildFx(FxSpec f)
    {
        var root = new GameObject("Fx_" + f.name);
        var fx = root.AddComponent<AvaEffect>(); var lay = root.AddComponent<AnnieLayers>();
        fx.layers = lay; fx.typeName = f.name;
        fx.arrowTex = T("arrow_wood"); fx.flashTex = T("spark_bits_white"); fx.ringTex = T("shockwave_ring_white");
        fx.coreTex = f.coreTex; fx.coreCols = f.coreCols; fx.coreRows = f.coreRows; fx.coreFrames = f.coreFrames; fx.coreMode = f.coreMode; fx.coreScale = f.coreScale;
        fx.shardTex = f.shardTex;
        fx.ringColor = f.ring; fx.lightColor = f.light; fx.lightPeak = f.lightPeak; fx.bodyThick = 0.8f;

        fx.arrow = SR(root.transform, "Arrow", fx.arrowTex, 1900);
        var l1 = SR(root.transform, "L1_Flash", fx.flashTex, 2100); fx.flash = l1;
        var l2 = SR(root.transform, "L2_Core", f.coreTex, 2000); fx.core = l2;
        if (f.shards > 0)
        {
            var arr = new SpriteRenderer[f.shards];
            for (int i = 0; i < f.shards; i++) arr[i] = SR(l2.transform, "shard" + i, f.shardTex, 2000);
            fx.shards = arr;
        }
        var l3f = new GameObject("L3_ParticlesFront"); l3f.transform.SetParent(root.transform, false);
        var l3b = new GameObject("L3_ParticlesBack"); l3b.transform.SetParent(root.transform, false);
        fx.front = PS(l3f.transform, "front", f.front); fx.back = PS(l3b.transform, "back", f.back);
        var l4 = SR(root.transform, "L4_Shockwave", fx.ringTex, 1950); fx.ring = l4;
        var l5t = new GameObject("L5_Trail"); l5t.transform.SetParent(root.transform, false);
        var head = new GameObject("TrailHead"); head.transform.SetParent(l5t.transform, false); fx.trailHead = head.transform;
        if (f.trailTex != null)
        {
            var tr = head.AddComponent<TrailRenderer>();
            tr.sharedMaterial = Mat("Mat_" + f.trailTex.name + "_trail", f.trailTex);
            tr.time = 0.2f; tr.minVertexDistance = 0.02f; tr.widthMultiplier = AvaEffect.TrailWidth; tr.textureMode = LineTextureMode.Stretch;
            tr.widthCurve = new AnimationCurve(new Keyframe(0, 1), new Keyframe(1, 0.15f));
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(f.trailColor, 0), new GradientColorKey(f.trailColor, 1) },
                      new[] { new GradientAlphaKey(0.35f, 0f), new GradientAlphaKey(0.2f, 0.5f), new GradientAlphaKey(0f, 1f) });
            tr.colorGradient = g; tr.sortingOrder = 1890; tr.emitting = false; tr.alignment = LineAlignment.View;
            fx.trailRenderer = tr;
        }
        if (f.path != null) { fx.pathPs = PS(l5t.transform, "path", f.path); fx.pathOnGround = f.pathOnGround; }
        var l5l = new GameObject("L5_Linger"); l5l.transform.SetParent(root.transform, false);
        var lingers = new List<ParticleSystem>();
        int li = 0; foreach (var ls in f.linger) lingers.Add(PS(l5l.transform, "linger" + (li++), ls));
        fx.linger = lingers.ToArray(); fx.lingerAtFeet = f.lingerAtFeet;
        var l6 = new GameObject("L6_Light"); l6.transform.SetParent(root.transform, false);
        var lt = l6.AddComponent<Light2D>(); lt.lightType = Light2D.LightType.Point; lt.intensity = 0f; lt.color = f.light;
        lt.pointLightOuterRadius = 7.2f; lt.pointLightInnerRadius = 0f; lt.falloffIntensity = 0.5f; lt.shadowsEnabled = false;
        fx.light2d = lt;

        lay.layers = new List<AnnieLayers.Layer>
        {
            new AnnieLayers.Layer{ name="Flash", target=l1.gameObject}, new AnnieLayers.Layer{ name="Core", target=l2.gameObject},
            new AnnieLayers.Layer{ name="ParticlesFront", target=l3f}, new AnnieLayers.Layer{ name="ParticlesBack", target=l3b},
            new AnnieLayers.Layer{ name="Shockwave", target=l4.gameObject}, new AnnieLayers.Layer{ name="Trail", target=l5t},
            new AnnieLayers.Layer{ name="Linger", target=l5l}, new AnnieLayers.Layer{ name="Light", target=l6},
        };
        var path = PrefDir + "Fx_" + f.name + ".prefab";
        var pf = PrefabUtility.SaveAsPrefabAsset(root, path);
        UnityEngine.Object.DestroyImmediate(root);
        return pf;
    }

    static FxSpec[] Specs()
    {
        var sparkBits = T("spark_bits_white");
        var spark = new FxSpec
        {
            name = "Spark", coreTex = T("hit_spark_sheet"), coreCols = 6, coreRows = 1, coreFrames = 6, coreMode = 0, coreScale = 0.55f,
            front = Bits(sparkBits, 2, 2, new Color(1f, 0.95f, 0.8f), true, 11), back = Bits(sparkBits, 2, 2, new Color(1f, 0.95f, 0.8f), false, 12),
            path = new PsSpec { tex = T("wind_streak_white"), cols = 1, rows = 2, rateDist = 3f, duration = 1f, speed = new Vector2(0, 0.1f), life = new Vector2(0.15f, 0.2f), size = new Vector2(0.18f, 0.26f), color = new Color(1, 1, 1, 0.3f), spin = false, alignLocal = true, order = 1895, seed = 13, endSize = 1f },
            trailTex = T("wind_trail_white"), trailColor = Color.white,
            linger = new[] { new PsSpec { tex = T("dust_puff_white"), cols = 4, rows = 4, frameOverLife = true, duration = 0.4f, rateTime = 0, burst = 4, extraBursts = { new Vector2(0.15f, 3), new Vector2(0.3f, 3) }, box = true, boxScale = new Vector3(2.4f, 2.0f, 0.1f), speed = new Vector2(0.05f, 0.3f), life = new Vector2(0.33f, 0.54f), size = new Vector2(0.3f, 0.54f), color = new Color(1f, 0.95f, 0.85f, 0.75f), endSize = 1.3f, order = 2005, seed = 14, grav = -0.02f } },
            ring = new Color(1f, 0.92f, 0.65f), light = new Color(1f, 0.93f, 0.78f), lightPeak = 1.0f
        };
        var fire = new FxSpec
        {
            name = "Fire", coreTex = T("fire_wisp_painted"), coreMode = 1, coreScale = 1.7f,
            front = Bits(T("ember"), 2, 1, Color.white, true, 21, 0.15f, new Vector2(0.27f, 0.45f)), back = Bits(T("ember"), 2, 1, Color.white, false, 22, 0.15f, new Vector2(0.27f, 0.45f)),
            path = new PsSpec { tex = T("fire_wisp_white"), cols = 4, rows = 4, frameOverLife = true, rateDist = 2f, duration = 1f, speed = new Vector2(0, 0.1f), life = new Vector2(0.15f, 0.2f), size = new Vector2(0.1f, 0.16f), color = new Color(1f, 0.62f, 0.22f, 0.4f), order = 1895, seed = 23, endSize = 0.4f },
            trailTex = T("wind_trail_white"), trailColor = new Color(1f, 0.5f, 0.15f),
            linger = new[] {
                new PsSpec { tex = T("fire_wisp_white"), cols = 4, rows = 4, frameOverLife = true, duration = 0.4f, burst = 3, extraBursts = { new Vector2(0.15f, 3), new Vector2(0.3f, 2) }, box = true, boxScale = new Vector3(2.0f, 1.8f, 0.1f), speed = new Vector2(0.1f, 0.4f), life = new Vector2(0.5f, 0.85f), size = new Vector2(0.15f, 0.27f), color = new Color(1f, 0.62f, 0.25f, 0.95f), grav = -0.1f, order = 2005, seed = 24, endSize = 0.3f },
                new PsSpec { tex = T("smoke_puff_white"), cols = 2, rows = 2, duration = 0.4f, burst = 3, extraBursts = { new Vector2(0.2f, 2) }, box = true, boxScale = new Vector3(1.6f, 1.2f, 0.1f), speed = new Vector2(0.1f, 0.35f), life = new Vector2(0.7f, 1.0f), size = new Vector2(0.36f, 0.66f), color = new Color(0.4f, 0.37f, 0.36f, 0.4f), grav = -0.12f, order = 2004, seed = 25, endSize = 1.6f } },
            ring = new Color(1f, 0.55f, 0.15f), light = new Color(1f, 0.55f, 0.2f), lightPeak = 1.1f
        };
        var ice = new FxSpec
        {
            name = "Ice", coreTex = T("ice_shard"), shardTex = T("ice_shard"), shards = 6, coreMode = 2, coreScale = 0.34f, coreFrames = 1,
            front = Bits(T("ice_shard"), 2, 2, Color.white, true, 31, 0.3f, new Vector2(0.33f, 0.51f)), back = Bits(T("ice_shard"), 2, 2, Color.white, false, 32, 0.3f, new Vector2(0.33f, 0.51f)),
            path = new PsSpec { tex = T("snowflake_white"), cols = 2, rows = 1, rateDist = 2f, duration = 1f, speed = new Vector2(0.05f, 0.2f), life = new Vector2(0.2f, 0.3f), size = new Vector2(0.1f, 0.16f), color = new Color(0.75f, 0.92f, 1f, 0.5f), order = 1895, seed = 33, endSize = 0.5f },
            trailTex = T("wind_trail_white"), trailColor = new Color(0.7f, 0.9f, 1f),
            linger = new[] { new PsSpec { tex = T("snowflake_white"), cols = 2, rows = 1, duration = 0.4f, burst = 4, extraBursts = { new Vector2(0.15f, 3), new Vector2(0.3f, 3) }, box = true, boxScale = new Vector3(2.4f, 1.6f, 0.1f), speed = new Vector2(0.05f, 0.25f), life = new Vector2(0.7f, 1.0f), size = new Vector2(0.15f, 0.24f), color = new Color(0.8f, 0.95f, 1f, 0.95f), grav = 0.08f, order = 2005, seed = 34, endSize = 0.6f } },
            ring = new Color(0.7f, 0.95f, 1f), light = new Color(0.6f, 0.92f, 1f), lightPeak = 1.0f
        };
        var dirt = new FxSpec
        {
            name = "Dirt", coreTex = T("hit_spark_sheet"), coreCols = 6, coreRows = 1, coreFrames = 6, coreMode = 0, coreScale = 0.55f,
            front = Bits(T("dirt_clump"), 2, 2, Color.white, true, 41, 1.0f, new Vector2(0.33f, 0.54f)), back = Bits(T("dirt_clump"), 2, 2, Color.white, false, 42, 0.9f, new Vector2(0.33f, 0.54f)),
            path = new PsSpec { tex = T("dust_puff_white"), cols = 4, rows = 4, frameOverLife = true, rateDist = 2f, duration = 1f, speed = new Vector2(0f, 0.1f), life = new Vector2(0.25f, 0.35f), size = new Vector2(0.16f, 0.26f), color = new Color(0.95f, 0.84f, 0.66f, 0.4f), order = 1895, seed = 43, endSize = 1.3f }, pathOnGround = true,
            trailTex = T("wind_trail_white"), trailColor = new Color(0.95f, 0.84f, 0.66f),
            linger = new[] { new PsSpec { tex = T("dust_puff_white"), cols = 4, rows = 4, frameOverLife = true, duration = 0.4f, burst = 3, extraBursts = { new Vector2(0.15f, 3), new Vector2(0.3f, 2) }, box = true, boxScale = new Vector3(1.8f, 0.2f, 0.1f), speed = new Vector2(0.05f, 0.3f), life = new Vector2(0.6f, 0.9f), size = new Vector2(0.48f, 0.78f), color = new Color(0.95f, 0.84f, 0.66f, 0.95f), order = 2005, seed = 44, endSize = 1.4f } },
            lingerAtFeet = true,
            ring = new Color(0.85f, 0.66f, 0.42f), light = new Color(0.85f, 0.65f, 0.4f), lightPeak = 0.6f
        };
        return new[] { spark, fire, ice, dirt };
    }

    // ------------------------------------------------------------------ scene
    static Sprite[] Sheet(string file)
    {
        return AssetDatabase.LoadAllAssetsAtPath(LoadDir + file).OfType<Sprite>().OrderBy(s => s.name).ToArray();
    }

    public static string Build()
    {
        foreach (var d in new[] { Job, TexDir, MatDir, PrefDir, Job + "Scenes/" }) if (!AssetDatabase.IsValidFolder(d.TrimEnd('/'))) Directory.CreateDirectory(d);
        AssetDatabase.Refresh();
        ImportAll();
        unlit = Mat("Mat_SpriteUnlit", null);
        var prefabs = Specs().Select(BuildFx).ToArray();

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

        // bloom volume
        var profPath = Job + "AvaBloomProfile.asset";
        AssetDatabase.DeleteAsset(profPath);
        var prof = ScriptableObject.CreateInstance<VolumeProfile>();
        AssetDatabase.CreateAsset(prof, profPath);
        var bloom = prof.Add<Bloom>(true);
        bloom.threshold.Override(0.97f); bloom.intensity.Override(0.7f); bloom.scatter.Override(0.5f); bloom.active = true;
        EditorUtility.SetDirty(prof); AssetDatabase.SaveAssets();
        var vg = new GameObject("Bloom"); var vol = vg.AddComponent<Volume>(); vol.isGlobal = true; vol.priority = 10; vol.sharedProfile = prof;

        var rootGo = new GameObject("AvaScene");
        var dir = rootGo.AddComponent<AvaDirector>();
        var sceneLayers = rootGo.AddComponent<AnnieLayers>();
        sceneLayers.layers = new List<AnnieLayers.Layer> { new AnnieLayers.Layer { name = "Bloom", target = vg, on = false } }; vg.SetActive(false);
        var capture = rootGo.AddComponent<AnnieCapture>();
        capture.cam = cam; capture.outDir = ""; capture.caption = ""; capture.frameCount = 90; capture.fps = 30; capture.replayLayersOnStart = false; capture.autoStart = false;
        dir.sceneLayers = sceneLayers;
        dir.sheets = new AvaSheets
        {
            idleBody = Sheet("idle_body_forest.png"), idleBow = Sheet("robes_idle_weapon_wood.png"),
            attackBody = Sheet("attack_body_forest.png"), attackBow = Sheet("robes_attack_weapon_wood.png"),
            hurtBody = Sheet("hit_body_forest.png"), hurtBow = Sheet("robes_hit_weapon_wood.png")
        };
        var fxParent = new GameObject("Effects"); fxParent.transform.SetParent(rootGo.transform, false);
        var list = new List<AvaEffect>();
        foreach (var pf in prefabs)
        {
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(pf, fxParent.transform);
            list.Add(inst.GetComponent<AvaEffect>());
        }
        dir.effectTypes = list.ToArray();
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        return MeshCoverage(dir.sheets);
    }

    static string MeshCoverage(AvaSheets sh)
    {
        var all = new List<Sprite>(); foreach (var a in new[] { sh.idleBody, sh.idleBow, sh.attackBody, sh.attackBow, sh.hurtBody, sh.hurtBow }) all.AddRange(a);
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
        File.WriteAllText(RunDir + "mesh_coverage.txt", r + "\n");
        return r;
    }

    // ------------------------------------------------------------------ recording setup
    // args: name|script|typeIndex|offLayersCsv|perSide|crowdSeed|outDir|frames(optional)
    public static string Record(string a)
    {
        var p = a.Split('|');
        string name = p[0], script = p[1]; int type = int.Parse(p[2]); string off = p[3]; int perSide = int.Parse(p[4]); int cseed = int.Parse(p[5]); string outDir = p[6];
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var dir = UnityEngine.Object.FindAnyObjectByType<AvaDirector>();
        var cap = dir.GetComponent<AnnieCapture>();
        string[] typeNames = { "Spark", "Fire", "Ice", "Dirt" };
        for (int i = 0; i < dir.effectTypes.Length; i++) if (i != type) { UnityEngine.Object.DestroyImmediate(dir.effectTypes[i].gameObject); dir.effectTypes[i] = null; }
        dir.script = script; dir.recType = type; dir.recPerSide = perSide; dir.crowdSeed = cseed; dir.poolSize = 1;
        var offSet = new HashSet<string>(off.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
        var fx = dir.effectTypes[type];
        foreach (var l in fx.layers.layers) l.on = !offSet.Contains(l.name);
        foreach (var l in dir.sceneLayers.layers) l.on = !offSet.Contains(l.name) && l.name != "Bloom";   // v2: bloom is off by default
        fx.layers.Apply(); dir.sceneLayers.Apply();
        EditorUtility.SetDirty(fx.layers); PrefabUtility.RecordPrefabInstancePropertyModifications(fx.layers);
        EditorUtility.SetDirty(dir); EditorUtility.SetDirty(dir.sceneLayers); EditorUtility.SetDirty(cap);
        Directory.CreateDirectory(outDir);
        cap.outDir = outDir; cap.caption = "TEST · " + typeNames[type]; cap.autoStart = true; cap.replayLayersOnStart = false; cap.fps = 30; cap.frameCount = 60;
        EditorApplication.EnterPlaymode();
        return "armed " + name;
    }

    /// <summary>Open the scene for interactive use (panel screenshot).</summary>
    public static string OpenInteractive()
    {
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        return "opened";
    }
}
