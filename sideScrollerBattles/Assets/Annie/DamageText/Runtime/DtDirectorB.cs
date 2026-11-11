using System;
using System.IO;
using System.Text;
using UnityEngine;
using Annie.BattleScene1Full;
using Annie.BattleScene1Wind;

namespace Annie.DamageText
{
    /// <summary>
    /// Take B: red numbers on all 33 hits of the battle-scene copy. It reads the plan SfDirector plays, never edits SfDirector, and takes its tick from the frames written (frame n = tick n).
    /// Value = round(100 x bar drop / 62), the bar drop read from the plan's hp array: before = the tick before the hit (-1 = full = the figure's inner height), after = 16 ticks later (-1 = gone = 0).
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class DtDirectorB : MonoBehaviour
    {
        public string configPath;
        public DtPops pops; public AnnieLayers popLayers; public TextAsset headTops;
        public TextAsset planJson, sceneJson;
        [Serializable] class Cfg { public string outDir, caption; public int frames, fps, seed; public string[] layersOn; public string fontSet, mode; }
        Cfg cfg; SfDirector sf; SfPlan p; SfSceneData sc; DtHeadTops ht; int lastN = -1;
        static readonly string[] SheetOf = { "run", "run_stop", "idle", "idle_headturn", "idle_shift", "attack", "hit", "death" };
        readonly StringBuilder tl = new StringBuilder();
        int popsMade; System.Random crng;
        const int CTickHalf = 240, CTickAll = 320;   // take C: one side idle, then all 30 in their fight poses

        void Awake()
        {
            cfg = JsonUtility.FromJson<Cfg>(File.ReadAllText(configPath));
            sf = GetComponent<SfDirector>();
            var cap = GetComponent<AnnieCapture>();
            cap.outDir = cfg.outDir; cap.caption = cfg.caption; cap.frameCount = cfg.frames; cap.fps = cfg.fps; cap.autoStart = true; cap.replayLayersOnStart = false;
            sf.framesDir = Path.Combine(cfg.outDir, "frames"); sf.logPath = Path.Combine(cfg.outDir, "unity_log.txt");
            var w = GetComponent<WwWind>(); if (w != null) w.logPath = Path.Combine(cfg.outDir, "wind.json");
            var on = new System.Collections.Generic.HashSet<string>(cfg.layersOn);
            foreach (var l in popLayers.layers) l.on = on.Contains(l.name);
            popLayers.Apply();
            pops.Configure(cfg.fps, cfg.seed, 1f, string.IsNullOrEmpty(cfg.fontSet) ? "chibi_half" : cfg.fontSet);
            if (cfg.mode == "C") crng = new System.Random(cfg.seed + 31);
            pops.logPath = Path.Combine(cfg.outDir, "pops.json");
            p = JsonUtility.FromJson<SfPlan>(planJson.text); sc = JsonUtility.FromJson<SfSceneData>(sceneJson.text);
            ht = new DtHeadTops(headTops.text);
        }

        void Update()
        {
            string fd = sf.framesDir;
            if (!Directory.Exists(fd)) return;
            int n = Directory.GetFiles(fd, "f*.png").Length;
            if (n == lastN) return;
            if (n >= p.nt) { pops.Tick(n); lastN = n; return; }
            for (int t = Mathf.Max(0, lastN + 1); t <= n; t++)
            {
                if (cfg.mode == "C") { if (t == CTickHalf) SpawnAll(t, true); else if (t == CTickAll) SpawnAll(t, false); continue; }
                for (int q = 0; q < p.shots.Count; q++)
                    if (p.shots[q].t_hit == t) Spawn(q, t);
            }
            pops.Tick(n);
            lastN = n;
        }

        /// <summary>Take C: a red number on every figure of the right-of-image side (enemy) at once, or on all 30. Values 23..52 seeded; positions 4, 10 (and 19) are forced to 31, 41 (51) so the font's weak 1 is on show.</summary>
        void SpawnAll(int t, bool oneSide)
        {
            int nF = p.nf, cnt = 0; int[] force = oneSide ? new[] { 4, 10 } : new[] { 4, 10, 19 }; int[] fv = { 31, 41, 51 };
            for (int i = 0; i < nF; i++)
            {
                var fig = sc.figs[i];
                if (oneSide && fig.side != "enemy") continue;
                int k = p.kind[t * nF + i], fr = p.frame[t * nF + i], ox = p.ox[t * nF + i];
                int nn = (int)Math.Round(512.0 * fig.s, MidpointRounding.ToEven);
                int oy = (int)Math.Round(fig.y - sc.AY * nn / 512.0, MidpointRounding.ToEven);
                var cell = ht.Get(SheetOf[k], fr);
                bool flip = fig.side == "enemy";
                float sx = ox + (flip ? 512f - cell.x : cell.x) * nn / 512f;
                float sy = oy + cell.y * nn / 512f;
                int val = crng.Next(23, 53);
                for (int f = 0; f < force.Length; f++) if (cnt == force[f]) val = fv[f];
                int id = pops.Spawn(0, val.ToString(), new Vector3(sx, -sy, 0f), "fig" + i, t);
                popsMade++;
                tl.Append("C tick ").Append(t).Append(" fig ").Append(i).Append(" side ").Append(fig.side).Append(" kind ").Append(k).Append(" frame ").Append(fr).Append(" head ").Append(sx.ToString("F1")).Append(',').Append(sy.ToString("F1")).Append(" value ").Append(val).Append(" pop ").Append(id).Append('\n');
                cnt++;
            }
        }

        int Hp(int t, int i) { return p.hp[t * p.nf + i]; }

        void Spawn(int q, int t)
        {
            var s = p.shots[q]; int i = s.i_t, nF = p.nf; var fig = sc.figs[i];
            int k = p.kind[t * nF + i], fr = p.frame[t * nF + i], ox = p.ox[t * nF + i];
            int nn = (int)Math.Round(512.0 * fig.s, MidpointRounding.ToEven);
            int oy = (int)Math.Round(fig.y - sc.AY * nn / 512.0, MidpointRounding.ToEven);
            var cell = ht.Get(SheetOf[k], fr);
            bool flip = fig.side == "enemy";
            float sx = ox + (flip ? 512f - cell.x : cell.x) * nn / 512f;
            float sy = oy + cell.y * nn / 512f;
            int before = Hp(t - 1, i); if (before < 0) before = p.bars[i].inner;
            int after = Hp(t + 16, i); if (after < 0) after = 0;
            int drop = before - after;
            int val = (int)Math.Round(100.0 * drop / 62.0, MidpointRounding.AwayFromZero);
            int id = pops.Spawn(0, val.ToString(), new Vector3(sx, -sy, 0f), "fig" + i, t);
            popsMade++;
            tl.Append("shot ").Append(q).Append(" tick ").Append(t).Append(" target ").Append(i).Append(" fatal ").Append(s.fatal).Append(" kind ").Append(k).Append(" frame ").Append(fr)
              .Append(" head ").Append(sx.ToString("F1")).Append(',').Append(sy.ToString("F1")).Append(" bar before ").Append(before).Append(" after ").Append(after).Append(" drop ").Append(drop).Append(" value ").Append(val).Append(" pop ").Append(id).Append('\n');
        }

        void OnDestroy() { if (cfg != null && tl.Length > 0) File.WriteAllText(Path.Combine(cfg.outDir, "dt_timeline.txt"), "pops " + popsMade + "\n" + tl); }
    }
}
