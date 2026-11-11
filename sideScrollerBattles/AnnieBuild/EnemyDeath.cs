using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Annie;
using Annie.EnemyDeath;

// Builder for the EnemyDeath job. Run with:
//   unity command run_script --file AnnieBuild/EnemyDeath.cs --entry EnemyDeathBuild.Build
//   unity command run_script --file AnnieBuild/EnemyDeath.cs --entry EnemyDeathBuild.Record --args "scene|outDir|frames|offLayersCsv|figuresCsv|replayAt"
public static class EnemyDeathBuild
{
    const string Job = "Assets/Annie/EnemyDeath/";
    const string TexDir = Job + "Textures/";
    const string MatDir = Job + "Materials/";
    const string PrefDir = Job + "Prefabs/";
    const string MainScene = Job + "Scenes/EnemyDeath.unity";
    const string AssassinScene = Job + "Scenes/EnemyDeath_Assassin.unity";

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
            bool isHit = n.StartsWith("hit_") || n.StartsWith("robes_hit");
            bool isAs = n.StartsWith("assassin_");
            bool isSpark = n == "spark_bits_white";
            ti.textureType = (isBg || isHit || isAs || isSpark) ? TextureImporterType.Sprite : TextureImporterType.Default;
            if (isBg)
            {
                ti.spriteImportMode = SpriteImportMode.Single; ti.spritePixelsPerUnit = 100;
                var st = new TextureImporterSettings(); ti.ReadTextureSettings(st);
                st.spriteMeshType = SpriteMeshType.FullRect; st.spriteAlignment = (int)SpriteAlignment.Center; ti.SetTextureSettings(st);
            }
            if (isHit || isAs)
            {
                int cols = isHit ? 5 : 8, rows = isHit ? 4 : 6;
                ti.spriteImportMode = SpriteImportMode.Multiple; ti.spritePixelsPerUnit = 512;
                var metas = new List<SpriteMetaData>();
                for (int i = 0; i < cols * rows; i++)
                {
                    int c = i % cols, r = i / cols;
                    metas.Add(new SpriteMetaData { name = "frame_" + i.ToString("D2"), rect = new Rect(c * 512, (rows - 1 - r) * 512, 512, 512), alignment = (int)SpriteAlignment.Custom, pivot = new Vector2(0.5f, 0f) });
                }
                ti.spritesheet = metas.ToArray();
                var st = new TextureImporterSettings(); ti.ReadTextureSettings(st);
                st.spriteMeshType = SpriteMeshType.FullRect; ti.SetTextureSettings(st);
            }
            if (isSpark)
            {
                // the three shard cells only: the four-point star (top right) is not sliced, so it can never be drawn
                ti.spriteImportMode = SpriteImportMode.Multiple; ti.spritePixelsPerUnit = 64;
                var metas = new List<SpriteMetaData>
                {
                    new SpriteMetaData { name = "shard_a", rect = new Rect(0, 64, 64, 64), alignment = (int)SpriteAlignment.Center, pivot = new Vector2(0.5f, 0.5f) },
                    new SpriteMetaData { name = "shard_b", rect = new Rect(0, 0, 64, 64), alignment = (int)SpriteAlignment.Center, pivot = new Vector2(0.5f, 0.5f) },
                    new SpriteMetaData { name = "shard_c", rect = new Rect(64, 0, 64, 64), alignment = (int)SpriteAlignment.Center, pivot = new Vector2(0.5f, 0.5f) },
                };
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

    static Material Mat(string name, Texture2D tex, string shader = "Sprites/Default")
    {
        string p = MatDir + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(p);
        if (m == null) { m = new Material(Shader.Find(shader)); AssetDatabase.CreateAsset(m, p); }
        m.shader = Shader.Find(shader); if (tex != null) m.mainTexture = tex; m.name = name;
        EditorUtility.SetDirty(m);
        return m;
    }

    // ------------------------------------------------------------------ particle systems (emitted by script from the order map; no emission module)
    static ParticleSystem PS(Transform parent, string name, Material m, int order, uint seed, Sprite[] sprites, int cols, int rows, bool frameOverLife)
    {
        var go = new GameObject(name); go.transform.SetParent(parent, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.playOnAwake = false; main.loop = false; main.duration = 10f; main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = 1f; main.startSpeed = 0f; main.startSize = 0.05f; main.maxParticles = 2400; main.gravityModifier = 0f;
        main.scalingMode = ParticleSystemScalingMode.Local; main.startColor = Color.white;
        var em = ps.emission; em.enabled = false;
        var sh = ps.shape; sh.enabled = false;
        var ts = ps.textureSheetAnimation; ts.enabled = true;
        if (sprites != null)
        {
            ts.mode = ParticleSystemAnimationMode.Sprites;
            foreach (var s in sprites) ts.AddSprite(s);
            ts.frameOverTime = new ParticleSystem.MinMaxCurve(0f); ts.startFrame = new ParticleSystem.MinMaxCurve(0f, 0.999f);
        }
        else
        {
            ts.mode = ParticleSystemAnimationMode.Grid; ts.numTilesX = cols; ts.numTilesY = rows; ts.animation = ParticleSystemAnimationType.WholeSheet;
            if (frameOverLife) { ts.frameOverTime = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0, 1, 1)); ts.startFrame = new ParticleSystem.MinMaxCurve(0f); }
            else { ts.frameOverTime = new ParticleSystem.MinMaxCurve(0f); ts.startFrame = new ParticleSystem.MinMaxCurve(0f, 0.999f); }
        }
        var col = ps.colorOverLifetime; col.enabled = true;
        var g = new Gradient();   // soft fade from 60% of life on a low last step, never a cut
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                  new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0.35f, 0.82f), new GradientAlphaKey(0.08f, 0.97f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
        var sz = ps.sizeOverLifetime; sz.enabled = true;
        sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 1f, 1, 0.55f));
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Billboard; r.sortingOrder = order; r.sharedMaterial = m;
        ps.useAutoRandomSeed = false; ps.randomSeed = seed;
        return ps;
    }

    static GameObject Child(Transform parent, string name) { var g = new GameObject(name); g.transform.SetParent(parent, false); return g; }

    static GameObject BuildPrefab()
    {
        var spark = AssetDatabase.LoadAllAssetsAtPath(TexDir + "spark_bits_white.png").OfType<Sprite>().OrderBy(s => s.name).ToArray();
        var mSpark = Mat("Mat_spark_bits_white", T("spark_bits_white"), "Annie/EnemyDeathFleck"); mSpark.SetFloat("_Lift", 1f);
        var mDust = Mat("Mat_dust_puff_white", T("dust_puff_white"));
        var mDissolve = Mat("Mat_Dissolve", null, "Annie/EnemyDeathDissolve");
        var root = new GameObject("Fx_EnemyDeath");
        var fx = root.AddComponent<EnemyDeath>(); var lay = root.AddComponent<AnnieLayers>();
        fx.layers = lay; fx.dissolveTemplate = mDissolve;
        fx.coreLayer = Child(root.transform, "L2_Core");
        fx.edgeLayer = Child(root.transform, "L2_EdgeGlow");
        fx.frontLayer = Child(root.transform, "L3_ParticlesFront"); fx.backLayer = Child(root.transform, "L3_ParticlesBack"); fx.lingerLayer = Child(root.transform, "L5_Linger");
        fx.frontPs = PS(fx.frontLayer.transform, "flecks", mSpark, 0, 11, spark, 1, 1, false);
        fx.backPs = PS(fx.backLayer.transform, "motes", mDust, 0, 12, null, 4, 4, true);
        fx.lingerPs = PS(fx.lingerLayer.transform, "ash", mDust, 0, 13, null, 4, 4, true);
        lay.layers = new List<AnnieLayers.Layer>
        {
            new AnnieLayers.Layer { name = "Core", target = fx.coreLayer },
            new AnnieLayers.Layer { name = "EdgeGlow", target = fx.edgeLayer },
            new AnnieLayers.Layer { name = "ParticlesFront", target = fx.frontLayer },
            new AnnieLayers.Layer { name = "ParticlesBack", target = fx.backLayer },
            new AnnieLayers.Layer { name = "Linger", target = fx.lingerLayer },
        };
        var pf = PrefabUtility.SaveAsPrefabAsset(root, PrefDir + "Fx_EnemyDeath.prefab");
        UnityEngine.Object.DestroyImmediate(root);
        return pf;
    }

    // ------------------------------------------------------------------ scenes
    static Sprite[] Sheet(string file) { return AssetDatabase.LoadAllAssetsAtPath(TexDir + file).OfType<Sprite>().OrderBy(s => s.name).ToArray(); }

    class Fig { public string name; public float feetX, feetY, s; public int side, fire, seed, order; public bool assassin; }

    static EdFigure MakeFigure(Fig f, GameObject prefab, Transform parent, Material unlit)
    {
        const float FeetCellX = 247, FeetCellY = 431;
        float sx = f.side == 0 ? 1f : -1f;
        var feet = new Vector3((f.feetX - 960f) / 100f, (540f - f.feetY) / 100f, 0);
        var go = new GameObject("Figure_" + f.name);
        go.transform.position = feet - new Vector3((FeetCellX - 256f) * 0.01f * f.s * sx, (512f - FeetCellY) * 0.01f * f.s, 0);
        go.transform.localScale = new Vector3(sx * 5.12f * f.s, 5.12f * f.s, 1f);
        var bg = new GameObject("body"); bg.transform.SetParent(go.transform, false);
        var body = bg.AddComponent<SpriteRenderer>(); body.sharedMaterial = unlit; body.sortingOrder = f.order;
        SpriteRenderer bow = null;
        if (!f.assassin)
        {
            var wg = new GameObject("bow"); wg.transform.SetParent(go.transform, false);
            bow = wg.AddComponent<SpriteRenderer>(); bow.sharedMaterial = unlit; bow.sortingOrder = f.order + 1;
        }
        var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        inst.name = "Fx_" + f.name;
        var ef = new EdFigure
        {
            name = f.name, fx = inst.GetComponent<EnemyDeath>(), body = body, bow = bow, fireTick = f.fire, seed = f.seed, hitAnim = !f.assassin,
            bodyFrames = f.assassin ? Sheet("assassin_idle_v4.png") : Sheet("hit_body_forest.png"),
            bowFrames = f.assassin ? null : Sheet("robes_hit_weapon_wood.png")
        };
        body.sprite = ef.bodyFrames[0]; if (bow != null) bow.sprite = ef.bowFrames[0];
        return ef;
    }

    static Scene_ NewScene(string path, Fig[] figs, GameObject prefab)
    {
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
        var rootGo = new GameObject("DeathScene");
        var dir = rootGo.AddComponent<EdDirector>();
        var capture = rootGo.AddComponent<AnnieCapture>();
        capture.cam = cam; capture.outDir = ""; capture.caption = "TEST"; capture.frameCount = 255; capture.fps = 30; capture.replayLayersOnStart = false; capture.autoStart = false;
        var unlit = Mat("Mat_SpriteUnlit", null);
        var fxParent = new GameObject("Effects"); fxParent.transform.SetParent(rootGo.transform, false);
        var figParent = new GameObject("Figures"); figParent.transform.SetParent(rootGo.transform, false);
        foreach (var f in figs) { var ef = MakeFigure(f, prefab, fxParent.transform, unlit); ef.body.transform.parent.SetParent(figParent.transform, true); dir.figures.Add(ef); }
        EditorSceneManager.SaveScene(scene, path);
        return new Scene_ { dir = dir, cap = capture };
    }
    class Scene_ { public EdDirector dir; public AnnieCapture cap; }

    public static string Build()
    {
        foreach (var d in new[] { Job, TexDir, MatDir, PrefDir, Job + "Scenes/" }) if (!AssetDatabase.IsValidFolder(d.TrimEnd('/'))) Directory.CreateDirectory(d);
        AssetDatabase.Refresh();
        ImportAll();
        Mat("Mat_SpriteUnlit", null);
        var pf = BuildPrefab();
        // front-row size and back-row size, image left, facing image right; dragon size, image right, mirrored
        var figs = new[]
        {
            new Fig { name = "Front", feetX = 430, feetY = 1040, s = 240f / 404f, side = 0, fire = 15, seed = 101, order = 100 },
            new Fig { name = "Back", feetX = 170, feetY = 532, s = 184f / 404f, side = 0, fire = 30, seed = 202, order = 60 },
            new Fig { name = "Dragon", feetX = 1250, feetY = 1040, s = 1.88f, side = 1, fire = 110, seed = 303, order = 120 },
        };
        NewScene(MainScene, figs, pf);
        // reviewer-only: the same prefab, unchanged, on an Assassin frame; only the position differs
        var asn = new[] { new Fig { name = "Assassin", feetX = 960, feetY = 1040, s = 240f / 404f, side = 0, fire = 5, seed = 404, order = 100, assassin = true } };
        NewScene(AssassinScene, asn, pf);
        AssetDatabase.SaveAssets();
        return "built; prefab " + PrefabUtility.GetPrefabAssetType(pf);
    }

    // ------------------------------------------------------------------ recording setup
    // args: scene(main|assassin)|outDir|frames|offLayersCsv|figuresCsv(empty = all)|replayAtTick(-1 none)
    public static string Record(string a)
    {
        var p = a.Split('|');
        bool assassin = p[0] == "assassin"; string outDir = p[1]; int frames = int.Parse(p[2]);
        var off = new HashSet<string>(p[3].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
        var only = new HashSet<string>(p[4].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
        int replayAt = int.Parse(p[5]);
        EditorSceneManager.OpenScene(assassin ? AssassinScene : MainScene, OpenSceneMode.Single);
        var dir = UnityEngine.Object.FindAnyObjectByType<EdDirector>();
        var cap = dir.GetComponent<AnnieCapture>();
        foreach (var f in dir.figures)
        {
            bool keep = only.Count == 0 || only.Contains(f.name);
            f.active = keep;
            if (!keep) { UnityEngine.Object.DestroyImmediate(f.body.transform.parent.gameObject); UnityEngine.Object.DestroyImmediate(f.fx.gameObject); f.fx = null; continue; }
            if (replayAt >= 0) f.replayAtTick = replayAt;
            foreach (var l in f.fx.layers.layers) l.on = !off.Contains(l.name);
            f.fx.layers.Apply();
            EditorUtility.SetDirty(f.fx.layers);
        }
        dir.figures.RemoveAll(f => f.fx == null);
        if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
        Directory.CreateDirectory(outDir);
        dir.framesDir = outDir + "/frames"; dir.logDir = outDir + "/logs"; dir.totalTicks = frames;
        EditorUtility.SetDirty(dir); EditorUtility.SetDirty(cap);
        cap.outDir = outDir; cap.caption = "TEST"; cap.autoStart = true; cap.replayLayersOnStart = false; cap.fps = 30; cap.frameCount = frames;
        EditorApplication.EnterPlaymode();
        return "armed " + p[0] + " off=[" + p[3] + "] figs=[" + p[4] + "] frames=" + frames;
    }

    // reviewer check: a mirrored enemy made with flipX alone (no facing argument, no negative scale) must drift away from its back
    public static string FlipCheck()
    {
        var pf = AssetDatabase.LoadAssetAtPath<GameObject>(PrefDir + "Fx_EnemyDeath.prefab");
        var sp = Sheet("hit_body_forest.png")[6];
        var res = new System.Text.StringBuilder();
        foreach (bool flip in new[] { false, true })
        {
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(pf);
            var go = new GameObject("flipcheck"); var sr = go.AddComponent<SpriteRenderer>(); sr.sprite = sp; sr.flipX = flip;
            go.transform.localScale = new Vector3(5.12f * 0.594f, 5.12f * 0.594f, 1f);
            var fx = inst.GetComponent<EnemyDeath>(); fx.Play(new[] { sr }, 7);
            // the cut leans toward the facing side; the opaque centre of mass sits on the flipped side
            res.Append("flipX=" + flip + " facing=" + fx.Facing + " N=" + fx.OpaqueCount + "; ");
            fx.Restore();
            UnityEngine.Object.DestroyImmediate(inst); UnityEngine.Object.DestroyImmediate(go);
        }
        return res.ToString();
    }

    public static string OpenMain() { EditorSceneManager.OpenScene(MainScene, OpenSceneMode.Single); return "opened"; }
}
