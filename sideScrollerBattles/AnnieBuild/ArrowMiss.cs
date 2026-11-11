using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Annie;
using Annie.ArrowMiss;

// Builder for the ArrowMiss job. Run with:
//   unity command run_script --file AnnieBuild/ArrowMiss.cs --entry ArrowMissBuild.Build
//   unity command run_script --file AnnieBuild/ArrowMiss.cs --entry ArrowMissBuild.Record --args "[\"name|long or short|offLayersCsv|outDir|frames\"]"
public static class ArrowMissBuild
{
    const string Job = "Assets/Annie/ArrowMiss/";
    const string TexDir = Job + "Textures/";
    const string MatDir = Job + "Materials/";
    const string PrefDir = Job + "Prefabs/";
    const string ScenePath = Job + "Scenes/ArrowMiss.unity";
    const string LoadDir = "Assets/Characters/archer_loadouts/";
    const string RunDir = "/Users/ehrendavis/work/local-RED-dev/src/asset-animation-demo/local_runs/unity/arrow_miss/";

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

    class PsSpec
    {
        public Texture2D tex; public int cols = 1, rows = 1; public Transform plane; public float dampen = 1.0f, bounce = 0.0f;
        public bool frameOverLife; public float frameStart = 0f, frameEnd = 1f; public bool easeSize;
        public int burst; public float duration = 0.2f;
        public Vector2 speed = new Vector2(1, 2), life = new Vector2(0.5f, 0.8f), size = new Vector2(0.3f, 0.4f);
        public Color color = Color.white; public float grav; public float cone = 20f, radius = 0.05f;
        public bool box; public Vector3 boxScale = Vector3.one; public float endSize = 1f; public bool spin = true;
        public int order = 100; public uint seed = 1; public float fadeStart = 0.55f;
        public List<Vector2> extraBursts = new List<Vector2>();   // (time, count)
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
        main.startColor = s.color; main.gravityModifier = s.grav; main.maxParticles = 12;
        main.startRotation = s.spin ? new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f) : new ParticleSystem.MinMaxCurve(0f);
        main.scalingMode = ParticleSystemScalingMode.Local;
        var em = ps.emission; em.rateOverTime = 0; em.rateOverDistance = 0;
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
            if (s.frameOverLife) { ts.frameOverTime = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, s.frameStart, 1, s.frameEnd)); ts.startFrame = new ParticleSystem.MinMaxCurve(0f); }
            else { ts.frameOverTime = new ParticleSystem.MinMaxCurve(0f); ts.startFrame = new ParticleSystem.MinMaxCurve(0f, 0.99f); }
        }
        var col = ps.colorOverLifetime; col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                  new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, s.fadeStart), new GradientAlphaKey(0f, 1f) });
        col.color = g;
        var sz = ps.sizeOverLifetime; sz.enabled = true;
        sz.size = new ParticleSystem.MinMaxCurve(1f, s.easeSize ? new AnimationCurve(new Keyframe(0f, 1.15f), new Keyframe(0.2f, 1.4f), new Keyframe(1f, s.endSize)) : AnimationCurve.Linear(0, 1f, 1, s.endSize));
        if (s.plane != null)
        {
            var cl = ps.collision; cl.enabled = true; cl.type = ParticleSystemCollisionType.Planes; cl.mode = ParticleSystemCollisionMode.Collision3D;
            cl.AddPlane(s.plane); cl.dampen = s.dampen; cl.bounce = s.bounce; cl.lifetimeLoss = 0f; cl.radiusScale = 0.2f; cl.minKillSpeed = 0f;
        }
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Billboard; r.sortingOrder = s.order;
        r.sharedMaterial = Mat("Mat_" + s.tex.name, s.tex);
        ps.useAutoRandomSeed = false; ps.randomSeed = s.seed;
        return ps;
    }

    static SpriteRenderer SR(Transform parent, string name, int order)
    {
        var go = new GameObject(name); go.transform.SetParent(parent, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sharedMaterial = unlit; sr.sortingOrder = order;
        return sr;
    }

    // ---- tunables found by trial (see the entry file)
    const float ClodGrav = 2.6f, ClodSpeed0 = 3.0f, ClodSpeed1 = 5.4f, ClodCone = 32f, ClodLife0 = 0.56f, ClodLife1 = 0.62f, ClodSz0 = 0.12f, ClodSz1 = 0.23f, ClodFade = 0.82f;
    const int LingerOrder = 99;   // behind the bodies: soft dust goes grey over dark bodies, and must stay off them

    static GameObject BuildFx()
    {
        var root = new GameObject("Fx_ArrowMiss");
        var fx = root.AddComponent<AmEffect>(); var lay = root.AddComponent<AnnieLayers>();
        fx.layers = lay; fx.arrowTex = T("arrow_wood"); fx.clumpTex = T("dirt_clump");
        fx.arrow = SR(root.transform, "Arrow", 1900);

        var l2 = new GameObject("L2_Core"); l2.transform.SetParent(root.transform, false);
        fx.mound = new[] { SR(l2.transform, "mound0", 106), SR(l2.transform, "mound1", 106) };
        fx.puffTex = T("dust_puff_white");
        fx.moundOffX = new[] { -3f, 5f }; fx.moundOffY = new[] { 1f, 2f }; fx.moundPx = new[] { 40f, 46f };
        fx.moundColor = new[] { new Color(0.715f, 0.56f, 0.45f, 0.97f), new Color(0.725f, 0.57f, 0.455f, 0.97f) };

        var l3f = new GameObject("L3_ParticlesFront"); l3f.transform.SetParent(root.transform, false);
        var gp = new GameObject("GroundPlane"); gp.transform.SetParent(l3f.transform, false); fx.groundPlane = gp.transform;
        fx.front = PS(l3f.transform, "clods", new PsSpec
        {
            tex = T("dirt_clump"), cols = 2, rows = 2, burst = 7, duration = 0.2f, cone = ClodCone, radius = 0.06f,
            speed = new Vector2(ClodSpeed0, ClodSpeed1), life = new Vector2(ClodLife0, ClodLife1), size = new Vector2(ClodSz0, ClodSz1), color = Color.white, grav = ClodGrav, plane = gp.transform,
            endSize = 1.0f, spin = true, order = 108, seed = 71, fadeStart = ClodFade
        });

        var l5t = new GameObject("L5_Trail"); l5t.transform.SetParent(root.transform, false);
        var head = new GameObject("TrailHead"); head.transform.SetParent(l5t.transform, false); fx.trailHead = head.transform;
        var tr = head.AddComponent<TrailRenderer>();
        tr.sharedMaterial = Mat("Mat_wind_trail_white_trail", T("wind_trail_white"));
        tr.time = 0.2f; tr.minVertexDistance = 0.02f; tr.widthMultiplier = 0.05f; tr.textureMode = LineTextureMode.Stretch;
        tr.widthCurve = new AnimationCurve(new Keyframe(0, 1), new Keyframe(1, 0.15f));
        tr.sortingOrder = 1890; tr.emitting = false; tr.alignment = LineAlignment.View;
        fx.trail = tr; fx.trailColor = Color.white;

        var l5l = new GameObject("L5_Linger"); l5l.transform.SetParent(root.transform, false);
        fx.linger = PS(l5l.transform, "dust", new PsSpec
        {
            tex = T("dust_puff_white"), cols = 4, rows = 4, frameOverLife = true, frameStart = 0.3f, frameEnd = 0.55f, easeSize = true, burst = 3, duration = 0.2f, extraBursts = { new Vector2(0.135f, 2) },
            box = true, boxScale = new Vector3(0.5f, 0.05f, 0.02f), speed = new Vector2(0.05f, 0.3f), life = new Vector2(0.45f, 0.6f), size = new Vector2(0.55f, 0.85f),
            color = new Color(1f, 0.95f, 0.85f, 0.85f), grav = -0.02f, endSize = 1.35f, spin = true, order = LingerOrder, seed = 72, fadeStart = 0.2f
        });

        lay.layers = new List<AnnieLayers.Layer>
        {
            new AnnieLayers.Layer { name = "Core", target = l2 }, new AnnieLayers.Layer { name = "ParticlesFront", target = l3f },
            new AnnieLayers.Layer { name = "Trail", target = l5t }, new AnnieLayers.Layer { name = "Linger", target = l5l },
        };
        var pf = PrefabUtility.SaveAsPrefabAsset(root, PrefDir + "Fx_ArrowMiss.prefab");
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
        var pf = BuildFx();

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

        var rootGo = new GameObject("ArrowMissScene");
        var dir = rootGo.AddComponent<AmDirector>();
        var capture = rootGo.AddComponent<AnnieCapture>();
        capture.cam = cam; capture.outDir = ""; capture.caption = ""; capture.frameCount = 120; capture.fps = 30; capture.replayLayersOnStart = false; capture.autoStart = false;
        dir.sheets = new AmSheets
        {
            idleBody = Sheet("idle_body_forest.png"), idleBow = Sheet("robes_idle_weapon_wood.png"),
            attackBody = Sheet("attack_body_forest.png"), attackBow = Sheet("robes_attack_weapon_wood.png")
        };
        var inst = (GameObject)PrefabUtility.InstantiatePrefab(pf, rootGo.transform);
        dir.fx = inst.GetComponent<AmEffect>();
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        return MeshCoverage(dir.sheets);
    }

    static string MeshCoverage(AmSheets sh)
    {
        var all = new List<Sprite>(); foreach (var a in new[] { sh.idleBody, sh.idleBow, sh.attackBody, sh.attackBow }) all.AddRange(a);
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

    // args: name|long or short|offLayersCsv|outDir|frames
    public static string Record(string a)
    {
        var p = a.Split('|');
        string name = p[0], take = p[1], off = p[2], outDir = p[3]; int frames = int.Parse(p[4]);
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var dir = UnityEngine.Object.FindAnyObjectByType<AmDirector>();
        var cap = dir.GetComponent<AnnieCapture>();
        dir.take = take;
        var fx = dir.fx;
        var offSet = new HashSet<string>(off.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
        foreach (var l in fx.layers.layers) l.on = !offSet.Contains(l.name);
        fx.layers.Apply();
        EditorUtility.SetDirty(fx.layers); PrefabUtility.RecordPrefabInstancePropertyModifications(fx.layers);
        EditorUtility.SetDirty(dir); EditorUtility.SetDirty(cap);
        if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
        Directory.CreateDirectory(outDir);
        cap.outDir = outDir; cap.caption = "TEST"; cap.autoStart = true; cap.replayLayersOnStart = false; cap.fps = 30; cap.frameCount = frames;
        EditorApplication.EnterPlaymode();
        return "armed " + name + " take=" + take + " layersOff=[" + off + "]";
    }

    public static string OpenScene() { EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single); return "opened"; }
}
