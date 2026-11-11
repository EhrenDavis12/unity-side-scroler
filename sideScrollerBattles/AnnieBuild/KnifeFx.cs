using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Annie;
using Annie.KnifeFx;

// Builder for the KnifeFx job. Run with:
//   unity command run_script --file AnnieBuild/KnifeFx.cs --entry KnifeFxBuild.Build
//   unity command run_script --file AnnieBuild/KnifeFx.cs --entry KnifeFxBuild.Record --args "name|hit|offLayersCsv|outDir|frames"
public static class KnifeFxBuild
{
    const string Job = "Assets/Annie/KnifeFx/";
    const string TexDir = Job + "Textures/";
    const string MatDir = Job + "Materials/";
    const string PrefDir = Job + "Prefabs/";
    const string ScenePath = Job + "Scenes/KnifeFx.unity";
    const string LoadDir = "Assets/Characters/archer_loadouts/";
    const string RunDir = "/Users/ehrendavis/work/local-RED-dev/src/asset-animation-demo/local_runs/unity/knife_fx/";
    const float AssassinX = 780f, HitArcherX = 932f, MissArcherX = 1170f;

    static Material unlit;
    static Texture2D T(string n) { return AssetDatabase.LoadAssetAtPath<Texture2D>(TexDir + n + ".png"); }

    // ------------------------------------------------------------------ textures
#pragma warning disable 618
    static void ImportAll()
    {
        foreach (var f in Directory.GetFiles(TexDir, "*.png"))
        {
            string path = f.Replace('\\', '/');
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            string n = Path.GetFileNameWithoutExtension(path);
            bool isBg = n == "backdrop" || n == "foreground";
            bool isSheet = n.StartsWith("assassin_");
            if (isBg) ti.textureType = TextureImporterType.Sprite;
            else if (isSheet) ti.textureType = TextureImporterType.Sprite;
            else ti.textureType = TextureImporterType.Default;
            if (isBg)
            {
                ti.spriteImportMode = SpriteImportMode.Single; ti.spritePixelsPerUnit = 100;
                var st = new TextureImporterSettings(); ti.ReadTextureSettings(st);
                st.spriteMeshType = SpriteMeshType.FullRect; st.spriteAlignment = (int)SpriteAlignment.Center; ti.SetTextureSettings(st);
            }
            if (isSheet)
            {
                int cols = n.Contains("attack") ? 5 : 8, rows = n.Contains("attack") ? 4 : 6;
                ti.spriteImportMode = SpriteImportMode.Multiple; ti.spritePixelsPerUnit = 512;
                var metas = new List<SpriteMetaData>();
                for (int i = 0; i < cols * rows; i++)
                {
                    int c = i % cols, r = i / cols;
                    metas.Add(new SpriteMetaData { name = "frame_" + i.ToString("D2"), rect = new Rect(c * 512, (rows - 1 - r) * 512, 512, 512), alignment = (int)SpriteAlignment.BottomCenter, pivot = new Vector2(0.5f, 0f) });
                }
                ti.spritesheet = metas.ToArray();
                var st = new TextureImporterSettings(); ti.ReadTextureSettings(st);
                st.spriteMeshType = SpriteMeshType.FullRect; ti.SetTextureSettings(st);
            }
            ti.alphaSource = TextureImporterAlphaSource.FromInput; ti.alphaIsTransparency = true;
            ti.mipmapEnabled = false; ti.wrapMode = TextureWrapMode.Clamp; ti.filterMode = FilterMode.Bilinear;
            ti.textureCompression = TextureImporterCompression.Uncompressed; ti.npotScale = TextureImporterNPOTScale.None;
            ti.maxTextureSize = 4096; ti.isReadable = false;
            ti.SaveAndReimport();
        }
    }
#pragma warning restore 618

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
        public Texture2D tex; public int cols = 1, rows = 1; public bool frameOverLife; public float frameStart = 0f, frameEnd = 1f;
        public int burst; public float duration = 0.1f;
        public Vector2 speed = new Vector2(1, 2), life = new Vector2(0.5f, 0.8f), size = new Vector2(0.3f, 0.4f);
        public bool size3D; public Vector2 sizeX, sizeY;
        public Color color = Color.white; public float grav; public float cone = 20f, radius = 0.02f;
        public float endSize = 1f; public bool spin;
        public int order = 100; public uint seed = 1; public bool alignLocal;
        public Vector3 shapeRot = Vector3.zero;
        public float fadeStart = 0.5f;
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
        if (s.size3D)
        {
            main.startSize3D = true;
            main.startSizeX = new ParticleSystem.MinMaxCurve(s.sizeX.x, s.sizeX.y);
            main.startSizeY = new ParticleSystem.MinMaxCurve(s.sizeY.x, s.sizeY.y);
            main.startSizeZ = new ParticleSystem.MinMaxCurve(1f);
        }
        else main.startSize = new ParticleSystem.MinMaxCurve(s.size.x, s.size.y);
        main.startColor = s.color; main.gravityModifier = s.grav; main.maxParticles = 100;
        main.startRotation = s.spin ? new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f) : new ParticleSystem.MinMaxCurve(0f);
        main.scalingMode = ParticleSystemScalingMode.Local;
        var em = ps.emission; em.rateOverTime = 0; em.rateOverDistance = 0;
        var bursts = new List<ParticleSystem.Burst>();
        if (s.burst > 0) bursts.Add(new ParticleSystem.Burst(0f, (short)s.burst));
        foreach (var b in s.extraBursts) bursts.Add(new ParticleSystem.Burst(b.x, (short)b.y));
        em.SetBursts(bursts.ToArray());
        var sh = ps.shape; sh.enabled = true;
        sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = s.cone; sh.radius = s.radius; sh.radiusThickness = 1f; sh.rotation = s.shapeRot;
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
        sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 1f, 1, s.endSize));
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Billboard; r.sortingOrder = s.order;
        r.sharedMaterial = Mat("Mat_" + s.tex.name, s.tex);
        if (s.alignLocal) r.alignment = ParticleSystemRenderSpace.Local;
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

    static readonly Color Steel = new Color(0.93f, 0.96f, 1f);
    static readonly Color Dust = new Color(1f, 0.96f, 0.88f);

    // ------------------------------------------------------------------ Fx_GroundDust (reusable: origin, scale, sort order)
    static GameObject BuildGroundDust()
    {
        var root = new GameObject("Fx_GroundDust");
        var gd = root.AddComponent<GroundDust>();
        // 3-5 puffs, 10-26 px, brightened; first 10 of the 16 puff frames, rising and spreading
        gd.ps = PS(root.transform, "puffs", new PsSpec
        {
            tex = T("dust_puff_white"), cols = 4, rows = 4, frameOverLife = true, frameStart = 0.25f, frameEnd = 0.66f, burst = 4, duration = 0.1f,
            speed = new Vector2(0.4f, 0.9f), life = new Vector2(0.35f, 0.5f), size = new Vector2(0.26f, 0.40f), color = new Color(1f, 0.97f, 0.9f, 1f),
            cone = 55f, radius = 0.12f, grav = -0.05f, endSize = 1f, order = 100, seed = 55, shapeRot = new Vector3(-90f, 0f, 0f), fadeStart = 0.5f
        });
        string path = PrefDir + "Fx_GroundDust.prefab";
        var pf = PrefabUtility.SaveAsPrefabAsset(root, path);
        UnityEngine.Object.DestroyImmediate(root);
        return pf;
    }

    static void SetSeed(ParticleSystem ps, uint seed)
    {
        var so = new SerializedObject(ps); so.FindProperty("randomSeed").uintValue = seed; so.ApplyModifiedPropertiesWithoutUndo();
    }

    // ------------------------------------------------------------------ Fx_KnifeHit / Fx_KnifeMiss
    static GameObject BuildFx(bool miss, GameObject groundDust)
    {
        var root = new GameObject(miss ? "Fx_KnifeMiss" : "Fx_KnifeHit");
        var fx = root.AddComponent<KnifeEffect>(); var lay = root.AddComponent<AnnieLayers>();
        fx.layers = lay; fx.miss = miss; fx.flashTex = T("spark_bits_white"); fx.coreTex = T("slash_strip_white"); fx.lightPeak = 0.5f;
        var layerList = new List<AnnieLayers.Layer>();
        var bits = T("spark_bits_white"); var streak = T("wind_streak_white"); var puff = T("dust_puff_white");

        if (!miss)
        {
            var l1 = SR(root.transform, "L1_Flash", 100); fx.flash = l1;
            layerList.Add(new AnnieLayers.Layer { name = "Flash", target = l1.gameObject });
        }
        // L2_Core: the wedge + a thin faint tip trail
        var l2 = new GameObject("L2_Core"); l2.transform.SetParent(root.transform, false);
        fx.core = SR(l2.transform, "Wedge", 100);
        var head = new GameObject("TipTrail"); head.transform.SetParent(l2.transform, false); fx.trailHead = head.transform;
        var tr = head.AddComponent<TrailRenderer>();
        tr.sharedMaterial = Mat("Mat_wind_trail_white_trail", T("wind_trail_white"));
        tr.time = 0.2f; tr.minVertexDistance = 0.02f; tr.widthMultiplier = 0.048f; tr.textureMode = LineTextureMode.Stretch;
        tr.widthCurve = new AnimationCurve(new Keyframe(0, 1), new Keyframe(1, 0.15f));
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Steel, 0), new GradientColorKey(Steel, 1) },
                  new[] { new GradientAlphaKey(0.35f, 0f), new GradientAlphaKey(0.2f, 0.5f), new GradientAlphaKey(0f, 1f) });
        tr.colorGradient = g; tr.sortingOrder = 104; tr.emitting = false; tr.alignment = LineAlignment.View;
        fx.trail = tr;
        layerList.Add(new AnnieLayers.Layer { name = "Core", target = l2 });

        if (!miss)
        {
            var l3f = new GameObject("L3_ParticlesFront"); l3f.transform.SetParent(root.transform, false);
            fx.front = PS(l3f.transform, "bits", new PsSpec
            {
                tex = bits, cols = 2, rows = 2, burst = 8, cone = 65f, radius = 0.14f, speed = new Vector2(2.0f, 4.2f), life = new Vector2(0.2f, 0.25f),
                size = new Vector2(0.3f, 0.42f), color = Steel, grav = 0.5f, endSize = 0.5f, spin = true, order = 106, seed = 51, fadeStart = 0.4f
            });
            layerList.Add(new AnnieLayers.Layer { name = "ParticlesFront", target = l3f });
            var l3b = new GameObject("L3_ParticlesBack"); l3b.transform.SetParent(root.transform, false);
            fx.backBits = PS(l3b.transform, "bits", new PsSpec
            {
                tex = bits, cols = 2, rows = 2, burst = 10, cone = 18f, speed = new Vector2(3.2f, 5f), life = new Vector2(0.3f, 0.43f),
                size = new Vector2(0.3f, 0.42f), color = Steel, grav = 0.3f, endSize = 0.4f, spin = true, order = 95, seed = 52, fadeStart = 0.5f
            });
            fx.backStreaks = PS(l3b.transform, "streaks", new PsSpec
            {
                tex = streak, cols = 1, rows = 2, burst = 4, cone = 6f, speed = new Vector2(3.4f, 5.2f), life = new Vector2(0.3f, 0.43f),
                size3D = true, sizeX = new Vector2(0.22f, 0.34f), sizeY = new Vector2(0.06f, 0.09f), radius = 0.1f,
                color = new Color(Steel.r, Steel.g, Steel.b, 0.8f), endSize = 0.6f, alignLocal = true, order = 95, seed = 53, shapeRot = new Vector3(0f, 90f, 0f), fadeStart = 0.4f
            });
            layerList.Add(new AnnieLayers.Layer { name = "ParticlesBack", target = l3b });
        }

        // L5_Trail: the rear-boot dirt kick, the shared Fx_GroundDust as a nested prefab
        var l5t = new GameObject("L5_Trail"); l5t.transform.SetParent(root.transform, false);
        var dustInst = (GameObject)PrefabUtility.InstantiatePrefab(groundDust, l5t.transform);
        fx.dust = dustInst.GetComponent<GroundDust>();
        if (miss) SetSeed(dustInst.GetComponentInChildren<ParticleSystem>(true), 63);
        layerList.Add(new AnnieLayers.Layer { name = "Trail", target = l5t });

        // L5_Linger
        var l5l = new GameObject("L5_Linger"); l5l.transform.SetParent(root.transform, false);
        if (!miss)
        {
            fx.lingerPuffs = PS(l5l.transform, "dirt", new PsSpec
            {
                tex = puff, cols = 4, rows = 4, frameOverLife = true, frameEnd = 0.75f, burst = 2, duration = 0.4f,
                extraBursts = { new Vector2(0.15f, 1), new Vector2(0.3f, 1) },
                speed = new Vector2(0.08f, 0.22f), life = new Vector2(0.55f, 0.8f), size = new Vector2(0.34f, 0.46f), color = new Color(1f, 0.96f, 0.88f, 0.9f),
                cone = 40f, radius = 0.3f, grav = -0.03f, endSize = 1f, order = 104, seed = 54, shapeRot = new Vector3(-90f, 0f, 0f), fadeStart = 0.45f
            });
        }
        else
        {
            fx.lingerStreaks = PS(l5l.transform, "streaks", new PsSpec
            {
                tex = streak, cols = 1, rows = 2, burst = 3, cone = 2f, speed = new Vector2(0.4f, 0.7f), life = new Vector2(0.3f, 0.42f),
                size3D = true, sizeX = new Vector2(1.1f, 1.4f), sizeY = new Vector2(0.2f, 0.3f), radius = 0.25f,
                color = new Color(Steel.r, Steel.g, Steel.b, 0.7f), endSize = 1f, alignLocal = true, order = 104, seed = 61, shapeRot = new Vector3(0f, 90f, 0f), fadeStart = 0.3f
            });
            fx.lingerMotes = PS(l5l.transform, "motes", new PsSpec
            {
                tex = puff, cols = 4, rows = 4, frameOverLife = true, frameEnd = 0.45f, burst = 3, cone = 25f, speed = new Vector2(0.3f, 0.6f), life = new Vector2(0.3f, 0.45f),
                size = new Vector2(0.2f, 0.3f), color = new Color(1f, 1f, 1f, 0.35f), radius = 0.3f, grav = 0f, endSize = 1f, order = 104, seed = 62, fadeStart = 0.3f
            });
        }
        layerList.Add(new AnnieLayers.Layer { name = "Linger", target = l5l });

        if (!miss)
        {
            var l6 = new GameObject("L6_Light"); l6.transform.SetParent(root.transform, false);
            var lt = l6.AddComponent<Light2D>(); lt.lightType = Light2D.LightType.Point; lt.intensity = 0f; lt.color = new Color(1f, 0.97f, 0.9f);
            lt.pointLightOuterRadius = 3.4f; lt.pointLightInnerRadius = 0f; lt.falloffIntensity = 0.5f; lt.shadowsEnabled = false;
            fx.light2d = lt;
            layerList.Add(new AnnieLayers.Layer { name = "Light", target = l6 });
        }
        lay.layers = layerList;
        var pf = PrefabUtility.SaveAsPrefabAsset(root, PrefDir + root.name + ".prefab");
        UnityEngine.Object.DestroyImmediate(root);
        return pf;
    }

    // ------------------------------------------------------------------ scene
    static Sprite[] Sheet(string assetPath)
    {
        return AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<Sprite>().OrderBy(s => s.name).ToArray();
    }

    public static string Build()
    {
        foreach (var d in new[] { Job, TexDir, MatDir, PrefDir, Job + "Scenes/" }) if (!AssetDatabase.IsValidFolder(d.TrimEnd('/'))) Directory.CreateDirectory(d);
        AssetDatabase.Refresh();
        ImportAll();
        unlit = Mat("Mat_SpriteUnlit", null);
        var gd = BuildGroundDust();
        var hitPf = BuildFx(false, gd); var missPf = BuildFx(true, gd);

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var camGo = new GameObject("Main Camera"); camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>(); cam.orthographic = true; cam.orthographicSize = 5.4f; cam.transform.position = new Vector3(0, 0, -10);
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black; cam.nearClipPlane = 0.1f; cam.farClipPlane = 50f;
        var cd = camGo.AddComponent<UniversalAdditionalCameraData>(); cd.renderPostProcessing = false; cd.antialiasing = AntialiasingMode.None;

        var gl = new GameObject("GlobalLight2D"); var g2 = gl.AddComponent<Light2D>(); g2.lightType = Light2D.LightType.Global; g2.intensity = 1f;
        var bd = new GameObject("Backdrop"); var bsr = bd.AddComponent<SpriteRenderer>();
        bsr.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(TexDir + "backdrop.png"); bsr.sortingOrder = -100;
        var fg = new GameObject("Foreground"); var fsr = fg.AddComponent<SpriteRenderer>();
        fsr.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(TexDir + "foreground.png"); fsr.sortingOrder = 1000;

        var rootGo = new GameObject("KnifeScene");
        var dir = rootGo.AddComponent<KnifeDirector>();
        var capture = rootGo.AddComponent<AnnieCapture>();
        capture.cam = cam; capture.outDir = ""; capture.caption = "TEST"; capture.frameCount = 75; capture.fps = 30; capture.replayLayersOnStart = false; capture.autoStart = false;
        dir.asIdle = Sheet(TexDir + "assassin_idle_v4.png"); dir.asAttack = Sheet(TexDir + "assassin_attack_v3.png");
        dir.arIdleBody = Sheet(LoadDir + "idle_body_forest.png"); dir.arIdleBow = Sheet(LoadDir + "robes_idle_weapon_wood.png");
        dir.arHurtBody = Sheet(LoadDir + "hit_body_forest.png"); dir.arHurtBow = Sheet(LoadDir + "robes_hit_weapon_wood.png");
        dir.assassinFeetPx = AssassinX; dir.archerFeetPx = HitArcherX; dir.attackStart = 4;
        var hitInst = (GameObject)PrefabUtility.InstantiatePrefab(hitPf, rootGo.transform);
        var missInst = (GameObject)PrefabUtility.InstantiatePrefab(missPf, rootGo.transform);
        dir.fx = hitInst.GetComponent<KnifeEffect>();
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        return "built; sprites idle=" + dir.asIdle.Length + " attack=" + dir.asAttack.Length + " archerIdle=" + dir.arIdleBody.Length + "/" + dir.arIdleBow.Length + " hurt=" + dir.arHurtBody.Length + "/" + dir.arHurtBow.Length + " " + MeshCoverage(dir);
    }

    static string MeshCoverage(KnifeDirector d)
    {
        var all = new List<Sprite>(); foreach (var a in new[] { d.asIdle, d.asAttack, d.arIdleBody, d.arIdleBow, d.arHurtBody, d.arHurtBow }) all.AddRange(a);
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

    // ------------------------------------------------------------------ recording setup
    // args: name|hit or miss|offLayersCsv|outDir|frames|(optional archerFeetPx)
    public static string Record(string a)
    {
        var p = a.Split('|');
        string name = p[0]; bool miss = p[1] == "miss"; string off = p[2]; string outDir = p[3]; int frames = int.Parse(p[4]);
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var dir = UnityEngine.Object.FindAnyObjectByType<KnifeDirector>();
        var cap = dir.GetComponent<AnnieCapture>();
        foreach (var e in dir.GetComponentsInChildren<KnifeEffect>(true))
            if (e.miss != miss) UnityEngine.Object.DestroyImmediate(e.gameObject);
        var fx = dir.GetComponentInChildren<KnifeEffect>(true);
        dir.fx = fx; dir.hit = !miss;
        dir.assassinFeetPx = AssassinX; dir.archerFeetPx = miss ? MissArcherX : HitArcherX;
        if (p.Length > 5 && p[5].Length > 0) dir.archerFeetPx = float.Parse(p[5]);
        var offSet = new HashSet<string>(off.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
        foreach (var l in fx.layers.layers) l.on = !offSet.Contains(l.name);
        fx.layers.Apply();
        EditorUtility.SetDirty(fx.layers); PrefabUtility.RecordPrefabInstancePropertyModifications(fx.layers);
        EditorUtility.SetDirty(dir); EditorUtility.SetDirty(cap);
        if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
        Directory.CreateDirectory(outDir);
        cap.outDir = outDir; cap.caption = "TEST"; cap.autoStart = true; cap.replayLayersOnStart = false; cap.fps = 30; cap.frameCount = frames;
        EditorApplication.EnterPlaymode();
        return "armed " + name + " layersOff=[" + off + "] archerX=" + dir.archerFeetPx;
    }

    public static string OpenScene()
    {
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        return "opened";
    }
}
