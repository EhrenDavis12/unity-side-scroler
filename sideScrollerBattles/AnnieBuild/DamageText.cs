using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Annie;
using Annie.DamageText;
using Annie.KnifeFx;
using Annie.BattleScene1Full;
using Annie.BattleScene1Wind;

// Builder for the DamageText job (floating combat text).
//   unity command run_script --file AnnieBuild/DamageText.cs --entry DamageTextBuild.Build
//   unity command run_script --file AnnieBuild/DamageText.cs --entry DamageTextBuild.Record --args '["beat|outDir|frames|fps|seed|layersCsv|caption"]'   beat = hit miss blocked heal spread (take A) or B (take B) or C (take C: 15 then 30 at once); args add |fontSet|mode
public static class DamageTextBuild
{
    const string Job = "Assets/Annie/DamageText/";
    const string TexDir = Job + "Textures/";
    const string MatDir = Job + "Materials/";
    const string SceneA = Job + "Scenes/DamageText.unity";
    const string SceneB = Job + "Scenes/DamageTextBattle.unity";
    const string SrcTex = "../../asset-animation-demo/local_runs/unity/damage_text/textures/";
    const string RunDir = "/Users/ehrendavis/work/local-RED-dev/src/asset-animation-demo/local_runs/unity/damage_text/";
    const string KnifeTex = "Assets/Annie/KnifeFx/Textures/";
    const string LoadDir = "Assets/Characters/archer_loadouts/";
    const string BaseB = "Assets/Annie/BattleScene1Full/Scenes/BattleScene1Full.unity";
    const string PlanE = "Assets/Annie/BattleScene1Full/Data/full_plan_e.json";

    static readonly string[] Names = { "Text_Damage", "Text_Heal", "Text_Miss", "Text_Blocked" };

#pragma warning disable 618
    static float MipBias = 0f;
    static void ImportDir(string dir, bool mips)
    {
        foreach (var f in Directory.GetFiles(dir, "*.png"))
        {
            string path = f.Replace('\\', '/');
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            ti.textureType = TextureImporterType.Sprite; ti.spriteImportMode = SpriteImportMode.Single; ti.spritePixelsPerUnit = 200;   // baked at 2x: 1 display px = 0.01 unit
            var st = new TextureImporterSettings(); ti.ReadTextureSettings(st);
            st.spriteMeshType = SpriteMeshType.FullRect; st.spriteAlignment = (int)SpriteAlignment.Center; ti.SetTextureSettings(st);
            ti.alphaSource = TextureImporterAlphaSource.FromInput; ti.alphaIsTransparency = true; ti.sRGBTexture = true;
            ti.mipmapEnabled = mips; ti.mipMapBias = MipBias; ti.wrapMode = TextureWrapMode.Clamp; ti.filterMode = mips ? FilterMode.Trilinear : FilterMode.Bilinear;
            ti.textureCompression = TextureImporterCompression.Uncompressed; ti.npotScale = TextureImporterNPOTScale.None;
            ti.maxTextureSize = 1024; ti.isReadable = false;
            var ps = ti.GetDefaultPlatformTextureSettings(); ps.format = TextureImporterFormat.RGBA32; ps.textureCompression = TextureImporterCompression.Uncompressed; ps.maxTextureSize = 1024; ti.SetPlatformTextureSettings(ps);
            ti.SaveAndReimport();
        }
    }
#pragma warning restore 618

    static Sprite Sp(string n) { return AssetDatabase.LoadAssetAtPath<Sprite>(TexDir + n + ".png"); }
    static Sprite SpIn(string dir, string n) { return AssetDatabase.LoadAssetAtPath<Sprite>(dir + n + ".png"); }
    const string TexHalf = Job + "Textures_chibi_half/", TexGame = Job + "Textures_game/";
    const string SrcHalf = RunDir + "textures_chibi_half/", SrcGame = RunDir + "textures_game/";

    static DtSet[] BuildSets()
    {
        var chibi = new DtSet { name = "chibi", plusRaise = 6f };
        chibi.red = Enumerable.Range(0, 10).Select(i => Sp("red_" + i)).ToArray(); chibi.green = Enumerable.Range(0, 10).Select(i => Sp("green_" + i)).ToArray();
        chibi.plus = new[] { Sp("green_plus") }; chibi.miss = Sp("word_miss"); chibi.blocked = Sp("word_blocked");
        var half = new DtSet { name = "chibi_half", plusRaise = 3f };
        half.red = Enumerable.Range(0, 10).Select(i => SpIn(TexHalf, "red_" + i)).ToArray(); half.green = Enumerable.Range(0, 10).Select(i => SpIn(TexHalf, "green_" + i)).ToArray();
        half.plus = new[] { SpIn(TexHalf, "green_plus") }; half.miss = SpIn(TexHalf, "word_miss"); half.blocked = SpIn(TexHalf, "word_blocked");
        var game = new DtSet { name = "game", plusRaise = 0f, baseline = 8f };
        var idx = Enumerable.Range(0, 30).ToArray();
        game.red = idx.Select(i => SpIn(TexGame, "red_" + (i / 3) + "_" + (i % 3))).ToArray(); game.green = idx.Select(i => SpIn(TexGame, "green_" + (i / 3) + "_" + (i % 3))).ToArray();
        game.plus = Enumerable.Range(0, 3).Select(v => SpIn(TexGame, "green_plus_" + v)).ToArray(); game.miss = SpIn(TexGame, "word_miss"); game.blocked = SpIn(TexGame, "word_blocked");
        game.metrics = AssetDatabase.LoadAssetAtPath<TextAsset>(Job + "Data/game_metrics.json");
        return new[] { chibi, half, game };
    }

    /// <summary>Re-imports the two new sets with mipmaps on or off (the v1 set is never touched). args: "on", "off", or "bias-1" (mips on, negative LOD bias).</summary>
    public static string SetMips(string a)
    {
        bool on = a != "off"; MipBias = a.StartsWith("bias") ? float.Parse(a.Substring(4)) : 0f; ImportDir(TexHalf, on); ImportDir(TexGame, on); AssetDatabase.SaveAssets(); return "mips " + a;
    }
    static Sprite[] Sheet(string assetPath) { return AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<Sprite>().OrderBy(s => s.name).ToArray(); }

    static Material SpriteMat()
    {
        string p = MatDir + "Mat_DamageText.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(p);
        if (m == null) { m = new Material(Shader.Find("Sprites/Default")); AssetDatabase.CreateAsset(m, p); }
        m.shader = Shader.Find("Sprites/Default"); EditorUtility.SetDirty(m);
        return m;
    }

    /// <summary>The AnnieLayers root: Text_Damage/Heal/Miss/Blocked, each a pool of 32 slots of 5 glyph sprites; DtPops on the same root.</summary>
    static DtPops BuildTextLayers(Material mat, TextAsset ignored)
    {
        var root = new GameObject("TextLayers");
        var lay = root.AddComponent<AnnieLayers>(); var pops = root.AddComponent<DtPops>();
        var list = new List<AnnieLayers.Layer>(); var roots = new List<Transform>();
        foreach (var n in Names)
        {
            var l = new GameObject(n); l.transform.SetParent(root.transform, false);
            for (int s = 0; s < 32; s++)
            {
                var sg = new GameObject("pop_" + s); sg.transform.SetParent(l.transform, false);
                var slot = sg.AddComponent<DtPopSlot>(); slot.glyphs = new SpriteRenderer[5];
                for (int g = 0; g < 5; g++)
                {
                    var gg = new GameObject("g" + g); gg.transform.SetParent(sg.transform, false);
                    var sr = gg.AddComponent<SpriteRenderer>(); sr.sharedMaterial = mat; sr.enabled = false; slot.glyphs[g] = sr;
                }
            }
            list.Add(new AnnieLayers.Layer { name = n, target = l, on = true }); roots.Add(l.transform);
        }
        lay.layers = list;
        pops.layerRoots = roots.ToArray();
        pops.sets = BuildSets();
        return pops;
    }

    public static string Build()
    {
        foreach (var d in new[] { TexDir, MatDir, Job + "Scenes/", Job + "Data/" }) Directory.CreateDirectory(d);
        foreach (var d in new[] { TexHalf, TexGame }) Directory.CreateDirectory(d);
        // the v1 textures are already imported and stay untouched; only the two new sets are copied and imported (mipmaps + trilinear)
        foreach (var f in Directory.GetFiles(SrcHalf, "*.png")) File.Copy(f, TexHalf + Path.GetFileName(f), true);
        foreach (var f in Directory.GetFiles(SrcGame, "*.png")) File.Copy(f, TexGame + Path.GetFileName(f), true);
        File.Copy(RunDir + "game_metrics_unity.json", Job + "Data/game_metrics.json", true);
        File.Copy(RunDir + "headtops.json", Job + "Data/headtops.json", true);
        AssetDatabase.Refresh();
        ImportDir(TexHalf, false); ImportDir(TexGame, false);   // mipmaps tried and left off: settled text went softer (see v2_scratch/mips_vs_none.png)
        var mat = SpriteMat();
        var head = AssetDatabase.LoadAssetAtPath<TextAsset>(Job + "Data/headtops.json");

        // ---- take A: the test bench
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var camGo = new GameObject("Main Camera"); camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>(); cam.orthographic = true; cam.orthographicSize = 5.4f; cam.transform.position = new Vector3(0, 0, -10);
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black; cam.nearClipPlane = 0.1f; cam.farClipPlane = 50f;
        var cd = camGo.AddComponent<UniversalAdditionalCameraData>(); cd.renderPostProcessing = false; cd.antialiasing = AntialiasingMode.None;
        var gl = new GameObject("GlobalLight2D"); var g2 = gl.AddComponent<Light2D>(); g2.lightType = Light2D.LightType.Global; g2.intensity = 1f;
        var bd = new GameObject("Backdrop"); bd.AddComponent<SpriteRenderer>().sprite = AssetDatabase.LoadAssetAtPath<Sprite>(KnifeTex + "backdrop.png"); bd.GetComponent<SpriteRenderer>().sortingOrder = -100;
        var fg = new GameObject("Foreground"); fg.AddComponent<SpriteRenderer>().sprite = AssetDatabase.LoadAssetAtPath<Sprite>(KnifeTex + "foreground.png"); fg.GetComponent<SpriteRenderer>().sortingOrder = 1000;

        var rootGo = new GameObject("DamageTextBench");
        var dir = rootGo.AddComponent<DtDirectorA>(); var capture = rootGo.AddComponent<AnnieCapture>();
        capture.cam = cam; capture.outDir = ""; capture.caption = "TEST"; capture.frameCount = 90; capture.fps = 30; capture.replayLayersOnStart = false; capture.autoStart = false;
        dir.configPath = RunDir + "run_config.json";
        dir.asIdle = Sheet(KnifeTex + "assassin_idle_v4.png"); dir.asAttack = Sheet(KnifeTex + "assassin_attack_v3.png");
        dir.arIdleBody = Sheet(LoadDir + "idle_body_forest.png"); dir.arIdleBow = Sheet(LoadDir + "robes_idle_weapon_wood.png");
        dir.arHurtBody = Sheet(LoadDir + "hit_body_forest.png"); dir.arHurtBow = Sheet(LoadDir + "robes_hit_weapon_wood.png");
        dir.arDefBody = Sheet(LoadDir + "defend_body_forest.png"); dir.arDefBow = Sheet(LoadDir + "robes_defend_weapon_wood.png");
        var hitPf = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Annie/KnifeFx/Prefabs/Fx_KnifeHit.prefab");
        var missPf = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Annie/KnifeFx/Prefabs/Fx_KnifeMiss.prefab");
        dir.fxHit = ((GameObject)PrefabUtility.InstantiatePrefab(hitPf, rootGo.transform)).GetComponent<KnifeEffect>();
        dir.fxMiss = ((GameObject)PrefabUtility.InstantiatePrefab(missPf, rootGo.transform)).GetComponent<KnifeEffect>();
        var popsA = BuildTextLayers(mat, head);
        dir.pops = popsA; dir.popLayers = popsA.GetComponent<AnnieLayers>(); dir.headTops = head;
        EditorSceneManager.SaveScene(scene, SceneA);

        // ---- take B: a copy of the battle scene
        if (File.Exists(SceneB)) AssetDatabase.DeleteAsset(SceneB);
        if (!AssetDatabase.CopyAsset(BaseB, SceneB)) return "copy failed";
        var sb = EditorSceneManager.OpenScene(SceneB, OpenSceneMode.Single);
        var sf = UnityEngine.Object.FindAnyObjectByType<SfDirector>();
        var db = sf.gameObject.AddComponent<DtDirectorB>();
        var popsB = BuildTextLayers(mat, head);
        db.configPath = RunDir + "run_config.json"; db.pops = popsB; db.popLayers = popsB.GetComponent<AnnieLayers>(); db.headTops = head;
        db.planJson = AssetDatabase.LoadAssetAtPath<TextAsset>(PlanE); db.sceneJson = sf.sceneJson;
        // the copy still names take2e's folders: point them at this job
        var capB = sf.GetComponent<AnnieCapture>(); capB.outDir = RunDir + "v2_scratch/battle"; capB.fps = 48; capB.frameCount = 1404;
        sf.framesDir = RunDir + "v2_scratch/battle/frames"; sf.logPath = RunDir + "v2_scratch/battle/unity_log.txt";
        var ww = sf.GetComponent<WwWind>(); ww.logPath = RunDir + "v2_scratch/battle/wind.json";
        EditorSceneManager.MarkSceneDirty(sb); EditorSceneManager.SaveScene(sb, SceneB);
        AssetDatabase.SaveAssets();
        var cam2 = UnityEngine.Object.FindAnyObjectByType<Camera>();
        return "built A + B; sf plan=" + sf.planJson.name + " windShift=" + sf.windShift + " cam ortho=" + cam2.orthographic + " size=" + cam2.orthographicSize + " pos=" + cam2.transform.position + " sets=" + popsA.sets.Length + " game red0=" + popsA.sets[2].red[0].rect.size + " ppu=" + popsA.sets[2].red[0].pixelsPerUnit;
    }

    // args: beat|outDir|frames|fps|seed|layersCsv|caption
    public static string Record(string a)
    {
        var p = a.Split('|');
        string beat = p[0], outDir = p[1]; int frames = int.Parse(p[2]), fps = int.Parse(p[3]), seed = int.Parse(p[4]);
        var layers = p[5].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
        string caption = p.Length > 6 ? p[6] : "TEST";
        string fontSet = p.Length > 7 ? p[7] : "chibi_half", mode = p.Length > 8 ? p[8] : "";
        AssetDatabase.Refresh();
        if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
        Directory.CreateDirectory(outDir);
        string cfg = "{\"beat\":\"" + beat + "\",\"outDir\":\"" + outDir + "\",\"caption\":\"" + caption + "\",\"fontSet\":\"" + fontSet + "\",\"mode\":\"" + mode + "\",\"frames\":" + frames + ",\"fps\":" + fps + ",\"seed\":" + seed +
                     ",\"archerPx\":" + (beat == "miss" ? "1170" : "932") + ",\"assassinPx\":780,\"layersOn\":[" + string.Join(",", layers.Select(l => "\"" + l + "\"")) + "]}";
        File.WriteAllText(RunDir + "run_config.json", cfg);
        EditorSceneManager.OpenScene(beat == "B" || beat == "C" ? SceneB : SceneA, OpenSceneMode.Single);
        EditorApplication.EnterPlaymode();
        return "armed " + beat + " " + cfg;
    }

    public static string OpenA() { EditorSceneManager.OpenScene(SceneA, OpenSceneMode.Single); return "opened"; }
}
