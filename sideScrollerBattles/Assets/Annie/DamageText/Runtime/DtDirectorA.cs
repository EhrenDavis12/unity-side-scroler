using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using Annie.KnifeFx;

namespace Annie.DamageText
{
    /// <summary>
    /// Take A, the test bench: one beat per capture (hit, miss, blocked, heal, spread). The beat and every capture setting come from the config file, so the saved scene is never edited between takes.
    /// The tick is the number of frames the capture has already written, so capture frame n shows tick n. Pops start on the exact tick of the event.
    /// </summary>
    public class DtDirectorA : MonoBehaviour
    {
        public string configPath;
        public Sprite[] asIdle, asAttack, arIdleBody, arIdleBow, arHurtBody, arHurtBow, arDefBody, arDefBow;
        public KnifeEffect fxHit, fxMiss;
        public DtPops pops; public AnnieLayers popLayers; public TextAsset headTops;
        public int attackStart = 4, assassinOrder = 103, archerOrder = 100;

        [Serializable] class Cfg { public string beat, outDir, caption; public int frames, fps, seed; public float archerPx, assassinPx; public string[] layersOn; public string fontSet; }
        Cfg cfg; KnifeEffect fx; DtHeadTops ht;
        KnifeActor assassin, archer; int contactTick = -1; int C;
        public int tick = -1;
        readonly StringBuilder tl = new StringBuilder();
        Vector3 arFeet, asFeet;
        bool attacks;
        struct Ev { public int tick, kind; public string text; }
        readonly List<Ev> events = new List<Ev>();

        void Awake()
        {
            cfg = JsonUtility.FromJson<Cfg>(File.ReadAllText(configPath));
            var cap = GetComponent<AnnieCapture>();
            cap.outDir = cfg.outDir; cap.caption = cfg.caption; cap.frameCount = cfg.frames; cap.fps = cfg.fps; cap.autoStart = true; cap.replayLayersOnStart = false;
            var on = new HashSet<string>(cfg.layersOn);
            foreach (var l in popLayers.layers) l.on = on.Contains(l.name);
            popLayers.Apply();
            attacks = cfg.beat == "hit" || cfg.beat == "miss" || cfg.beat == "blocked";
            fx = cfg.beat == "hit" ? fxHit : cfg.beat == "miss" ? fxMiss : null;
            if (fxHit != null && fxHit != fx) DestroyImmediate(fxHit.gameObject);
            if (fxMiss != null && fxMiss != fx) DestroyImmediate(fxMiss.gameObject);
            pops.Configure(cfg.fps, cfg.seed, 0.01f, string.IsNullOrEmpty(cfg.fontSet) ? "chibi_half" : cfg.fontSet);
            pops.logPath = Path.Combine(cfg.outDir, "pops.json");
            ht = new DtHeadTops(headTops.text);
            C = attackStart + Mathf.CeilToInt(5 * 30f / 16f);
            switch (cfg.beat)
            {
                case "hit": events.Add(new Ev { tick = C, kind = 0, text = "27" }); break;
                case "miss": events.Add(new Ev { tick = C, kind = 2, text = "Miss" }); break;
                case "blocked": events.Add(new Ev { tick = C, kind = 3, text = "Blocked" }); break;
                case "heal": events.Add(new Ev { tick = C, kind = 1, text = "+27" }); break;
                case "twins":   // repeated digits: each repeat must take a different variant
                    events.Add(new Ev { tick = 12, kind = 0, text = "22" }); events.Add(new Ev { tick = 32, kind = 0, text = "111" }); events.Add(new Ev { tick = 52, kind = 1, text = "+33" }); break;
                case "spread":
                    string[] v = { "7", "108", "+15", "Miss", "27", "Blocked", "64", "+8" };
                    int[] k = { 0, 0, 1, 2, 0, 3, 0, 1 };
                    for (int i = 0; i < 8; i++) events.Add(new Ev { tick = 12 + 9 * i, kind = k[i], text = v[i] });
                    break;
            }
        }

        void Start()
        {
            asFeet = KnifeGeo.PxToWorld(new Vector2(cfg.assassinPx, KnifeGeo.FeetPxY));
            arFeet = KnifeGeo.PxToWorld(new Vector2(cfg.archerPx, KnifeGeo.FeetPxY));
            bool block = cfg.beat == "blocked";
            var ag = new GameObject("Assassin"); ag.transform.SetParent(transform, false);
            assassin = ag.AddComponent<KnifeActor>();
            assassin.idleBody = asIdle; assassin.attackBody = asAttack; assassin.feetCellX = KnifeGeo.AsFeetCellX; assassin.order = assassinOrder;
            assassin.mirror = false; assassin.returnCell = 1;
            assassin.idleStartCell = ((48 - (int)(attackStart * 16f / 30f)) % 48 + 48) % 48;
            assassin.Init(asFeet);
            var rg = new GameObject("Archer"); rg.transform.SetParent(transform, false);
            archer = rg.AddComponent<KnifeActor>();
            archer.idleBody = arIdleBody; archer.idleWeapon = arIdleBow;
            archer.hurtBody = block ? arDefBody : arHurtBody; archer.hurtWeapon = block ? arDefBow : arHurtBow;
            archer.mirror = true; archer.feetCellX = KnifeGeo.ArFeetCellX; archer.order = archerOrder; archer.returnCell = 0;
            archer.idleStartCell = ((64 - (int)(C * 16f / 30f) % 64) % 64);
            archer.Init(arFeet);
            if (fx != null)
            {
                float farX = KnifeGeo.ArcherCell(new Vector2(170f, 219f), arFeet).x;
                fx.Setup(asFeet, arFeet, farX, archerOrder, assassinOrder);
            }
        }

        void Update()
        {
            if (Time.captureFramerate == 0) return;
            var cap = GetComponent<AnnieCapture>();
            string fd = Path.Combine(cap.outDir, "frames");
            if (!Directory.Exists(fd)) return;
            int n = Directory.GetFiles(fd, "f*.png").Length;
            if (n == tick) return;
            tick = n;
            Step(tick);
        }

        string ArcherSheet() { return archer.state == KnifeActor.St.Idle ? "idle" : (cfg.beat == "blocked" ? "defend" : "hit"); }

        void Step(int t)
        {
            if (attacks && t == attackStart) assassin.StartAttack(t);
            assassin.Tick(t);
            if (attacks && contactTick < 0 && assassin.AttackFrame == 5)
            {
                contactTick = t;
                if (cfg.beat == "hit") archer.StartHurt(t);
            }
            if (cfg.beat == "blocked" && t == C - 10) archer.StartHurt(t);   // the guard's raise takes 9.4 ticks, so it is up on the word's first frame
            archer.Tick(t);
            if (fx != null) fx.Tick(t, assassin.AttackFrame);
            foreach (var e in events)
                if (e.tick == t)
                {
                    var cellPt = ht.Get(ArcherSheet(), archer.Frame);
                    var head = KnifeGeo.ArcherCell(cellPt, arFeet);
                    int id = pops.Spawn(e.kind, e.text, head, "archer", t);
                    tl.Append("pop ").Append(id).Append(" tick ").Append(t).Append(" '").Append(e.text).Append("' archer ").Append(ArcherSheet()).Append(' ').Append(archer.Frame).Append(" cell ").Append(cellPt).Append('\n');
                }
            pops.Tick(t);
            tl.Append(t).Append(": assassin ").Append(assassin.Label()).Append(" | archer ").Append(archer.Label()).Append('\n');
        }

        void OnDestroy()
        {
            if (cfg != null && tick >= 0) File.WriteAllText(Path.Combine(cfg.outDir, "timeline.txt"), tl.ToString());
        }
    }
}
