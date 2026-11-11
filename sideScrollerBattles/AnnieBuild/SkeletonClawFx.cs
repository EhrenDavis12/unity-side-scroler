using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Annie;
using Annie.SkeletonClawFx;

// Builder for the SkeletonClawFx job. Run with:
//   unity command run_script --file AnnieBuild/SkeletonClawFx.cs --entry SkeletonClawFxBuild.Build
//   unity command run_script --file AnnieBuild/SkeletonClawFx.cs --entry SkeletonClawFxBuild.Record --args "name|hit|offLayersCsv|outDir|frames"
public static class SkeletonClawFxBuild
{
    const string Job = "Assets/Annie/SkeletonClawFx/";
    const string TexDir = Job + "Textures/";
    const string MatDir = Job + "Materials/";
    const string PrefDir = Job + "Prefabs/";
    const string ScenePath = Job + "Scenes/SkeletonClawFx.unity";
    const string LoadDir = "Assets/Characters/archer_loadouts/";
    const string RunDir = "/Users/ehrendavis/work/local-RED-dev/src/asset-animation-demo/local_runs/unity/skeleton_claw_fx/";
    const float SkeletonX = 740f, HitArcherX = 1042f, MissArcherX = 1175f;

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
            bool isAtk = n.StartsWith("skel_atk_"), isIdle = n.StartsWith("skel_idle_");
            ti.textureType = (isBg || isAtk || isIdle) ? TextureImporterType.Sprite : TextureImporterType.Default;
            if (isBg || isAtk || isIdle)
            {
                ti.spriteImportMode = SpriteImportMode.Single;
                ti.spritePixelsPerUnit = isBg ? 100 : 512;
                var st = new TextureImporterSettings(); ti.ReadTextureSettings(st);
                st.spriteMeshType = SpriteMeshType.FullRect;
                if (isBg) st.spriteAlignment = (int)SpriteAlignment.Center;
                else
                {
                    // the sole of the left foot: (172.5, 81.5) px from the cell's bottom-left
                    st.spriteAlignment = (int)SpriteAlignment.Custom;
                    st.spritePivot = isAtk ? new Vector2(172.5f / 768f, 81.5f / 591f) : new Vector2(172.5f / 512f, 81.5f / 512f);
                }
                ti.SetTextureSettings(st);
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

    // ------------------------------------------------------------------ particle helper: no shape or burst, the effect script emits with its own seeded stream
    class PsSpec
    {
        public Texture2D tex; public int cols = 1, rows = 1; public bool frameOverLife; public float frameStart = 0f, frameEnd = 1f;
        public float grav; public float endSize = 1f; public bool size3D;
        public int order = 100; public uint seed = 1; public float fadeStart = 0.5f;
    }

    static ParticleSystem PS(Transform parent, string name, PsSpec s)
    {
        var go = new GameObject(name); go.transform.SetParent(parent, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.playOnAwake = false; main.loop = true; main.duration = 10f; main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = 1f; main.startSpeed = 0f; main.startSize = 0.3f;
        if (s.size3D) { main.startSize3D = true; main.startSizeX = 0.3f; main.startSizeY = 0.1f; main.startSizeZ = 1f; }
        main.startColor = Color.white; main.gravityModifier = s.grav; main.maxParticles = 100;
        main.scalingMode = ParticleSystemScalingMode.Local;
        var em = ps.emission; em.enabled = true; em.rateOverTime = 0; em.rateOverDistance = 0; em.SetBursts(new ParticleSystem.Burst[0]);
        var sh = ps.shape; sh.enabled = false;
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
        ps.useAutoRandomSeed = false; ps.randomSeed = s.seed;
        return ps;
    }

    static GameObject Child(Transform parent, string name) { var go = new GameObject(name); go.transform.SetParent(parent, false); return go; }

    static MeshRenderer Ribbon(Transform parent, string name, Material m, int order)
    {
        var go = Child(parent, name);
        go.AddComponent<MeshFilter>();
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = m; mr.sortingOrder = order; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
        return mr;
    }

    // ------------------------------------------------------------------ Fx_SkelHit / Fx_SkelMiss
    // sort orders (each renderer its own): Archer 100/101, skeleton 103. back bits 95, back streaks 94, wisps 98, dust 99, smoke 102,
    // edge 104, core 105, trail 106, front 107, miss streaks 108, flash 109, miss motes 110, miss soil 111
    static GameObject BuildFx(bool miss)
    {
        var root = new GameObject(miss ? "Fx_SkelMiss" : "Fx_SkelHit");
        var fx = root.AddComponent<ClawEffect>(); var lay = root.AddComponent<AnnieLayers>();
        fx.layers = lay; fx.miss = miss; fx.flashTex = T("spark_bits_white"); fx.lightPeak = 0.5f;
        var layerList = new List<AnnieLayers.Layer>();
        var bits = T("spark_bits_white"); var streak = T("wind_streak_white"); var puff = T("dust_puff_white");

        if (!miss)
        {
            var l1 = new GameObject("L1_Flash"); l1.transform.SetParent(root.transform, false);
            var sr = l1.AddComponent<SpriteRenderer>(); sr.sharedMaterial = unlit; sr.sortingOrder = 109; fx.flash = sr;
            layerList.Add(new AnnieLayers.Layer { name = "Flash", target = l1 });
        }
        // L2_Core: one crescent ribbon (a ghost-green edge under a bone-cream body, both from slash_strip_white) + a thin faint trail on the cleaver head
        var l2 = Child(root.transform, "L2_Core");
        var stripMat = Mat("Mat_slash_strip_white", T("slash_strip_white"));
        fx.edgeMr = Ribbon(l2.transform, "Edge", stripMat, 104);
        fx.coreMr = Ribbon(l2.transform, "Crescent", stripMat, 105);
        var head = Child(l2.transform, "HeadTrail"); fx.trailHead = head.transform;
        var tr = head.AddComponent<TrailRenderer>();
        tr.sharedMaterial = Mat("Mat_wind_trail_white_trail", T("wind_trail_white"));
        tr.time = 0.2f; tr.minVertexDistance = 0.02f; tr.widthMultiplier = 0.048f; tr.textureMode = LineTextureMode.Stretch;
        tr.widthCurve = new AnimationCurve(new Keyframe(0, 1), new Keyframe(1, 0.15f));
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(ClawEffect.Bone, 0), new GradientColorKey(ClawEffect.Bone, 1) },
                  new[] { new GradientAlphaKey(0.35f, 0f), new GradientAlphaKey(0.2f, 0.5f), new GradientAlphaKey(0f, 1f) });
        tr.colorGradient = g; tr.sortingOrder = 106; tr.emitting = false; tr.alignment = LineAlignment.View;
        fx.trail = tr;
        layerList.Add(new AnnieLayers.Layer { name = "Core", target = l2 });

        if (!miss)
        {
            var l3f = Child(root.transform, "L3_ParticlesFront");
            fx.front = PS(l3f.transform, "sparks", new PsSpec { tex = bits, cols = 2, rows = 2, grav = 0.5f, endSize = 0.5f, order = 107, seed = 71, fadeStart = 0.4f });
            layerList.Add(new AnnieLayers.Layer { name = "ParticlesFront", target = l3f });
            var l3b = Child(root.transform, "L3_ParticlesBack");
            fx.backBits = PS(l3b.transform, "bits", new PsSpec { tex = bits, cols = 2, rows = 2, grav = 0.3f, endSize = 0.4f, order = 95, seed = 72, fadeStart = 0.5f });
            fx.backStreaks = PS(l3b.transform, "streaks", new PsSpec { tex = streak, cols = 1, rows = 2, size3D = true, endSize = 0.6f, order = 94, seed = 73, fadeStart = 0.4f });
            layerList.Add(new AnnieLayers.Layer { name = "ParticlesBack", target = l3b });
        }

        // L5_Trail: grave smoke shed off the cleaver head
        var l5t = Child(root.transform, "L5_Trail");
        fx.smoke = PS(l5t.transform, "smoke", new PsSpec { tex = T("smoke_puff_white"), cols = 2, rows = 2, grav = -0.02f, endSize = 1.25f, order = 102, seed = 75, fadeStart = 0.3f });
        layerList.Add(new AnnieLayers.Layer { name = "Trail", target = l5t });

        // L5_Linger
        var l5l = Child(root.transform, "L5_Linger");
        if (!miss)
        {
            fx.wisps = PS(l5l.transform, "wisps", new PsSpec { tex = T("fire_wisp_white"), cols = 4, rows = 4, frameOverLife = true, frameStart = 0f, frameEnd = 0.94f, endSize = 0.8f, order = 98, seed = 76, fadeStart = 0.45f });
            fx.dust = PS(l5l.transform, "dust", new PsSpec { tex = puff, cols = 4, rows = 4, frameOverLife = true, frameStart = 0f, frameEnd = 0.75f, grav = -0.03f, endSize = 1f, order = 99, seed = 77, fadeStart = 0.45f });
        }
        else
        {
            fx.missStreaks = PS(l5l.transform, "streaks", new PsSpec { tex = streak, cols = 1, rows = 2, size3D = true, endSize = 1f, order = 108, seed = 81, fadeStart = 0.3f });
            fx.missMotes = PS(l5l.transform, "motes", new PsSpec { tex = T("fire_wisp_white"), cols = 4, rows = 4, frameOverLife = true, frameStart = 0f, frameEnd = 0.5f, endSize = 0.8f, order = 110, seed = 82, fadeStart = 0.3f });
            fx.missSoil = PS(l5l.transform, "soil", new PsSpec { tex = puff, cols = 4, rows = 4, frameOverLife = true, frameStart = 0.25f, frameEnd = 0.66f, grav = -0.05f, endSize = 1f, order = 111, seed = 83, fadeStart = 0.5f });
        }
        layerList.Add(new AnnieLayers.Layer { name = "Linger", target = l5l });

        if (!miss)
        {
            var l6 = Child(root.transform, "L6_Light");
            var lt = l6.AddComponent<Light2D>(); lt.lightType = Light2D.LightType.Point; lt.intensity = 0f; lt.color = ClawEffect.Ghost;
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

    static Sprite[] Cells(string prefix, int n)
    {
        var a = new Sprite[n];
        for (int i = 0; i < n; i++) a[i] = AssetDatabase.LoadAssetAtPath<Sprite>(TexDir + prefix + i.ToString("D3") + ".png");
        return a;
    }

    public static string Build()
    {
        foreach (var d in new[] { Job, TexDir, MatDir, PrefDir, Job + "Scenes/" }) if (!AssetDatabase.IsValidFolder(d.TrimEnd('/'))) Directory.CreateDirectory(d);
        AssetDatabase.Refresh();
        ImportAll();
        unlit = Mat("Mat_SpriteUnlit", null);
        var hitPf = BuildFx(false); var missPf = BuildFx(true);

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

        // Bloom: a global Volume, off by default, toggled like a layer by its own AnnieLayers
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        var bloom = profile.Add<Bloom>(true); bloom.intensity.Override(0.6f); bloom.threshold.Override(0.97f); bloom.scatter.Override(0.5f);
        string profPath = Job + "Bloom_Profile.asset";
        AssetDatabase.DeleteAsset(profPath);
        AssetDatabase.CreateAsset(profile, profPath);
        var bgo = new GameObject("Bloom"); var vol = bgo.AddComponent<Volume>(); vol.isGlobal = true; vol.sharedProfile = profile; vol.priority = 10;
        var bt = new GameObject("BloomToggle"); var bl = bt.AddComponent<AnnieLayers>();
        bgo.transform.SetParent(bt.transform, false);
        bl.layers = new List<AnnieLayers.Layer> { new AnnieLayers.Layer { name = "Bloom", target = bgo, on = false } };
        bgo.SetActive(false);

        var rootGo = new GameObject("ClawScene");
        var dir = rootGo.AddComponent<ClawDirector>();
        var capture = rootGo.AddComponent<AnnieCapture>();
        capture.cam = cam; capture.outDir = ""; capture.caption = "TEST"; capture.frameCount = 75; capture.fps = 30; capture.replayLayersOnStart = false; capture.autoStart = false;
        dir.skIdle = Cells("skel_idle_", 48); dir.skAttack = Cells("skel_atk_", 21);
        dir.arIdleBody = Sheet(LoadDir + "idle_body_forest.png"); dir.arIdleBow = Sheet(LoadDir + "robes_idle_weapon_wood.png");
        dir.arHurtBody = Sheet(LoadDir + "hit_body_forest.png"); dir.arHurtBow = Sheet(LoadDir + "robes_hit_weapon_wood.png");
        dir.skeletonFeetPx = SkeletonX; dir.archerFeetPx = HitArcherX; dir.attackStart = 4;
        var hitInst = (GameObject)PrefabUtility.InstantiatePrefab(hitPf, rootGo.transform);
        var missInst = (GameObject)PrefabUtility.InstantiatePrefab(missPf, rootGo.transform);
        dir.fx = hitInst.GetComponent<ClawEffect>();
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        return "built; sprites skIdle=" + dir.skIdle.Length + " skAttack=" + dir.skAttack.Length + " archerIdle=" + dir.arIdleBody.Length + "/" + dir.arIdleBow.Length + " hurt=" + dir.arHurtBody.Length + "/" + dir.arHurtBow.Length + " " + MeshCoverage(dir);
    }

    static string MeshCoverage(ClawDirector d)
    {
        var groups = new List<KeyValuePair<string, Sprite[]>> {
            new KeyValuePair<string, Sprite[]>("skel_atk", d.skAttack), new KeyValuePair<string, Sprite[]>("skel_idle", d.skIdle),
            new KeyValuePair<string, Sprite[]>("archer_idle_body", d.arIdleBody), new KeyValuePair<string, Sprite[]>("archer_idle_bow", d.arIdleBow),
            new KeyValuePair<string, Sprite[]>("archer_hurt_body", d.arHurtBody), new KeyValuePair<string, Sprite[]>("archer_hurt_bow", d.arHurtBow) };
        var sb = new StringBuilder(); float min = 9; int bad = 0, count = 0;
        foreach (var gp in groups)
            foreach (var s in gp.Value)
            {
                var v = s.vertices; var tri = s.triangles; float area = 0;
                for (int i = 0; i < tri.Length; i += 3)
                {
                    Vector2 a = v[tri[i]], b = v[tri[i + 1]], c = v[tri[i + 2]];
                    area += Mathf.Abs((b.x - a.x) * (c.y - a.y) - (c.x - a.x) * (b.y - a.y)) * 0.5f;
                }
                float full = s.rect.width * s.rect.height / s.pixelsPerUnit / s.pixelsPerUnit;
                float cov = area / full; min = Mathf.Min(min, cov); if (cov < 0.999f) bad++; count++;
                sb.Append(gp.Key).Append(' ').Append(s.name).Append(" ppu=").Append(s.pixelsPerUnit).Append(" pivot=").Append(s.pivot.x.ToString("F4")).Append(',').Append(s.pivot.y.ToString("F4"))
                  .Append(" mesh_coverage=").Append(cov.ToString("F4")).Append('\n');
            }
        string r = "mesh coverage: sprites=" + count + " min=" + min.ToString("F4") + " below0.999=" + bad;
        Directory.CreateDirectory(RunDir);
        File.WriteAllText(RunDir + "mesh_coverage.txt", sb + r + "\n");
        return r;
    }

    // ------------------------------------------------------------------ recording setup
    // args: name|hit or miss|offLayersCsv|outDir|frames|(optional archerFeetPx)|(optional 'bloom')
    public static string Record(string a)
    {
        var p = a.Split('|');
        string name = p[0]; bool miss = p[1] == "miss"; string off = p[2]; string outDir = p[3]; int frames = int.Parse(p[4]);
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var dir = UnityEngine.Object.FindAnyObjectByType<ClawDirector>();
        var cap = dir.GetComponent<AnnieCapture>();
        foreach (var e in dir.GetComponentsInChildren<ClawEffect>(true))
            if (e.miss != miss) UnityEngine.Object.DestroyImmediate(e.gameObject);
        var fx = dir.GetComponentInChildren<ClawEffect>(true);
        dir.fx = fx; dir.hit = !miss;
        dir.skeletonFeetPx = SkeletonX; dir.archerFeetPx = miss ? MissArcherX : HitArcherX;
        if (p.Length > 5 && p[5].Length > 0) dir.archerFeetPx = float.Parse(p[5]);
        var offSet = new HashSet<string>(off.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
        foreach (var l in fx.layers.layers) l.on = !offSet.Contains(l.name);
        fx.layers.Apply();
        bool bloomOn = p.Length > 6 && p[6] == "bloom";
        var bt = GameObject.Find("BloomToggle");
        if (bt != null) { var bl = bt.GetComponent<AnnieLayers>(); foreach (var l in bl.layers) l.on = bloomOn; bl.Apply(); EditorUtility.SetDirty(bl); }
        { var bp = AssetDatabase.LoadAssetAtPath<VolumeProfile>(Job + "Bloom_Profile.asset"); Bloom bm;
          if (bp != null) {
            if (!bp.TryGet(out bm)) { bp.components.Clear(); bm = bp.Add<Bloom>(true); AssetDatabase.AddObjectToAsset(bm, bp); }
            bm.active = true; bm.threshold.Override(0.97f); bm.intensity.Override(0.9f); bm.scatter.Override(0.5f);
            EditorUtility.SetDirty(bm); EditorUtility.SetDirty(bp); AssetDatabase.SaveAssets(); } }
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
