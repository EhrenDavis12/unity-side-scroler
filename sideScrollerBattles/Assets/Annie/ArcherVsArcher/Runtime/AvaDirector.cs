using System;
using System.Collections.Generic;
using UnityEngine;

namespace Annie.ArcherVsArcher
{
    /// <summary>
    /// The Archer-vs-Archer test scene: slots, shots, the IMGUI panel and the scripted recordings. Everything runs on a 30 fps tick
    /// (one tick per frame while Time.captureFramerate is set, real time otherwise).
    /// </summary>
    public class AvaDirector : MonoBehaviour
    {
        public AvaSheets sheets;
        public AvaEffect[] effectTypes;           // Spark, Fire, Ice, Dirt (scene instances; pool clones are made from them)
        public AnnieLayers sceneLayers;           // holds Bloom
        public int seed = 1234;
        public int poolSize = 3;
        [Header("Recording (set by the builder's Record method; empty = interactive)")]
        public string script = "";                // all | oneshot | crowd
        public int recType = 0;
        public int crowdSeed = 7;
        public int recPerSide = 1;

        public int tick;
        float accum;
        System.Random rng;
        int[] count = new int[2];
        AvaFighter[][] fighters = { new AvaFighter[15], new AvaFighter[15] };
        List<AvaEffect>[] pool;
        readonly List<Shot> shots = new List<Shot>();
        int lastShooterSide = 1;
        int selType = 0;
        bool trade;
        int nextTrade = 40;
        public List<string> log = new List<string>();
        public bool Recording { get { return !string.IsNullOrEmpty(script); } }

        class Shot
        {
            public int sSide, sSlot, tSide, tSlot, type, t0, arrowStart, A;
            public Vector3 grip, chest; public float sScale, tK; public int flight;
            public AvaEffect fx; public bool begun;
        }

        static readonly string[] TypeNames = { "Spark", "Fire", "Ice", "Dirt" };
        static readonly string[] RowNames = { "Flash", "Core", "Particles", "Shockwave", "Trail", "Linger", "Light", "Bloom" };

        // ---------------------------------------------------------------- setup
        void Awake()
        {
            rng = new System.Random(seed);
            if (Recording) poolSize = 1;
            pool = new List<AvaEffect>[effectTypes.Length];
            for (int i = 0; i < effectTypes.Length; i++)
            {
                if (effectTypes[i] == null) continue;
                pool[i] = new List<AvaEffect> { effectTypes[i] };
                for (int j = 1; j < poolSize; j++)
                {
                    var c = Instantiate(effectTypes[i], effectTypes[i].transform.parent);
                    c.name = effectTypes[i].name + "_pool" + j;
                    pool[i].Add(c);
                }
            }
        }

        void Start()
        {
            if (Recording) tick = -1;   // the capture renders its first frame after two steps; this makes capture frame n show tick n
            AddFighter(0); AddFighter(1);
            for (int i = 1; i < recPerSide; i++) { AddFighter(0); AddFighter(1); }
            if (Recording) BuildRecording();
        }

        void AddFighter(int side)
        {
            if (count[side] >= 15) return;
            int slot = count[side]++;
            var go = new GameObject((side == 0 ? "L" : "R") + "_archer_" + slot);
            go.transform.SetParent(transform, false);
            var f = go.AddComponent<AvaFighter>();
            f.Init(sheets, side, slot, rng.Next(0, 64));
            fighters[side][slot] = f;
        }

        void ResetFighters()
        {
            for (int s = 0; s < 2; s++)
            {
                for (int i = 0; i < 15; i++) if (fighters[s][i] != null) Destroy(fighters[s][i].gameObject);
                fighters[s] = new AvaFighter[15]; count[s] = 0;
            }
            shots.Clear();
            foreach (var l in pool) if (l != null) foreach (var e in l) e.HideAll();
            AddFighter(0); AddFighter(1);
            lastShooterSide = 1;
        }

        // ---------------------------------------------------------------- shots
        Shot MakeShot(int sSide, int sSlot, int tSide, int tSlot, int type, int t0)
        {
            var s = new Shot { sSide = sSide, sSlot = sSlot, tSide = tSide, tSlot = tSlot, type = type, t0 = t0 };
            AvaGeo.ShotPoints(sSide, sSlot, tSide, tSlot, out s.grip, out s.chest, out s.sScale, out s.tK, out s.flight);
            s.arrowStart = t0 + 4;
            s.A = s.arrowStart + s.flight;
            return s;
        }

        AvaEffect FreeFx(int type)
        {
            foreach (var e in pool[type]) if (!e.busy) return e;
            return pool[type][0];
        }

        void Launch(Shot s)
        {
            s.fx = FreeFx(s.type);
            s.fx.HideAll();
            int tr, tc; AvaGeo.SlotRC(s.tSlot, out tr, out tc);
            s.fx.Begin(s.grip, s.chest, AvaGeo.FeetWorld(s.sSide, s.sSlot), AvaGeo.FeetWorld(s.tSide, s.tSlot), s.tK, s.sScale, s.sSide == 0 ? 1 : -1, s.arrowStart, s.A, 100 - tr * 10);
            s.begun = true;
            log.Add("shot " + TypeNames[s.type] + " " + (s.sSide == 0 ? "L" : "R") + s.sSlot + "->" + (s.tSide == 0 ? "L" : "R") + s.tSlot +
                    " t0=" + s.t0 + " arrow=" + s.arrowStart + " A=" + s.A);
            shots.Add(s);
        }

        /// <summary>Interactive: fire one shot now, from the side that did not shoot last.</summary>
        public void FireNow(int type)
        {
            int sSide = 1 - lastShooterSide; lastShooterSide = sSide; int tSide = 1 - sSide;
            if (count[sSide] == 0 || count[tSide] == 0) return;
            int sSlot = rng.Next(0, count[sSide]); int tSlot = rng.Next(0, count[tSide]);
            var s = MakeShot(sSide, sSlot, tSide, tSlot, type, tick);
            pending.Add(s);
        }
        readonly List<Shot> pending = new List<Shot>();

        // ---------------------------------------------------------------- tick
        void Update()
        {
            if (Recording && Time.captureFramerate == 0) return;   // wait for the capture to start so tick n is capture frame n
            if (Time.captureFramerate > 0) { Step(); return; }
            accum += Time.deltaTime;
            int guard = 0;
            while (accum >= 1f / 30f && guard++ < 6) { Step(); accum -= 1f / 30f; }
        }

        void Step()
        {
            if (trade && !Recording && tick >= nextTrade) { FireNow(selType); nextTrade = tick + 80; }
            // scripted / pending launches
            for (int i = pending.Count - 1; i >= 0; i--)
                if (tick >= pending[i].t0) { var s = pending[i]; pending.RemoveAt(i); Launch(s); }
            foreach (var s in shots)
            {
                if (tick == s.t0) F(s.sSide, s.sSlot).StartAttack(tick);
                if (tick == s.A) { F(s.tSide, s.tSlot).StartHurt(tick); }
            }
            foreach (var side in fighters) foreach (var f in side) if (f != null) f.Tick(tick);
            foreach (var l in pool) if (l != null) foreach (var e in l) e.Tick(tick);
            shots.RemoveAll(s => tick > s.A + 80);
            if (Recording) RecStepLog();
            tick++;
        }

        AvaFighter F(int side, int slot) { return fighters[side][slot]; }

        // ---------------------------------------------------------------- recordings
        public class PlanShot { public int sSide, sSlot, tSide, tSlot, t0; }
        public List<PlanShot> plan = new List<PlanShot>();
        public int planFrames;

        // virtual idle state used to align the target's idle to frame 63/0/1 on the hit frame
        class Virt { public int idleStart, idleOff; public bool acted; }

        /// <summary>Plan the scripted shots; also fixes each first-time target's idle phase so it is on frame 0 at A (shooter shifts for the rest).</summary>
        public void BuildRecording()
        {
            plan.Clear();
            int type = recType;
            var virt = new Dictionary<int, Virt>();
            Func<int, int, Virt> V = (sd, sl) => { int key = sd * 100 + sl; Virt v; if (!virt.TryGetValue(key, out v)) { v = new Virt { idleStart = 0, idleOff = F(sd, sl).idleOff }; virt[key] = v; } return v; };
            var seq = new List<int[]>(); // sSide, sSlot, tSide, tSlot
            if (script == "all") { seq.Add(new[] { 0, 0, 1, 0 }); seq.Add(new[] { 1, 0, 0, 0 }); }
            else if (script == "oneshot") seq.Add(new[] { 0, 0, 1, 0 });
            else if (script == "crowd")
            {
                // four shots; seed searched upward until every target is a different archer and (with two rows on a side) at least one shot is diagonal
                for (int sd = crowdSeed; sd < crowdSeed + 500; sd++)
                {
                    var r = new System.Random(sd); seq.Clear(); int side = 0; var tgts = new HashSet<int>(); bool diag = false;
                    for (int i = 0; i < 4; i++)
                    {
                        int sSlot = r.Next(0, recPerSide), tSlot = r.Next(0, recPerSide);
                        seq.Add(new[] { side, sSlot, 1 - side, tSlot }); tgts.Add((1 - side) * 100 + tSlot);
                        if (sSlot / 3 != tSlot / 3) diag = true;
                        side = 1 - side;
                    }
                    if (tgts.Count == 4 && (diag || recPerSide <= 3)) break;
                }
            }
            int tmin = 15;
            foreach (var q in seq)
            {
                var sh = MakeShot(q[0], q[1], q[2], q[3], type, 0);
                int off = sh.A; // A - t0
                var tv = V(q[2], q[3]);
                int t0 = tmin;
                if (!tv.acted)
                {
                    int A = t0 + off; // align this fresh target's idle to frame 0 at A
                    tv.idleOff = (64 - (int)((A - tv.idleStart) * 16f / 30f) % 64) % 64;
                    F(q[2], q[3]).idleOff = tv.idleOff;
                }
                else
                {
                    for (int g = 0; g < 400; g++, t0++)
                    {
                        int fr = (((int)((t0 + off - tv.idleStart) * 16f / 30f) + tv.idleOff) % 64 + 64) % 64;
                        if (fr == 63 || fr == 0 || fr == 1) break;
                    }
                }
                int A2 = t0 + off;
                plan.Add(new PlanShot { sSide = q[0], sSlot = q[1], tSide = q[2], tSlot = q[3], t0 = t0 });
                var sv = V(q[0], q[1]); sv.acted = true; sv.idleStart = t0 + 38 /*attack 20 frames at 16 fps = 37.5 ticks*/; sv.idleOff = 0;
                // keep virtual idle consistent with AvaFighter: returns to idle at tick t0 + ceil(20*30/16) = t0 + 38 (first tick with frame >= 20)
                tv.acted = true; tv.idleStart = A2 + 38; tv.idleOff = 0;
                tmin = A2 + 38 + 15;
            }
            planFrames = tmin + 0;
            if (script == "oneshot") planFrames = plan[0].t0 + (MakeShot(0, 0, 1, 0, 0, 0).A) + 45;
            var capC = GetComponent<AnnieCapture>(); if (capC != null) capC.frameCount = planFrames;
            foreach (var p in plan)
            {
                var s = MakeShot(p.sSide, p.sSlot, p.tSide, p.tSlot, type, p.t0);
                pending.Add(s);
            }
        }

        System.Text.StringBuilder tl = new System.Text.StringBuilder();
        void RecStepLog() { tl.Append(tick).Append(": ").Append(FighterStates()).Append('\n'); }
        void OnDestroy()
        {
            var cap = GetComponent<AnnieCapture>();
            if (Recording && cap != null && !string.IsNullOrEmpty(cap.outDir))
            {
                System.IO.File.WriteAllText(System.IO.Path.Combine(cap.outDir, "timeline.txt"), string.Join("\n", log) + "\n\n" + tl.ToString());
            }
        }

        // ---------------------------------------------------------------- panel
        Vector2 dummy;
        public string FighterStates()
        {
            var sb = new System.Text.StringBuilder();
            for (int s = 0; s < 2; s++) for (int i = 0; i < 15; i++) if (fighters[s][i] != null) sb.Append((s == 0 ? "L" : "R") + i + ":" + fighters[s][i].FrameLabel() + " ");
            return sb.ToString();
        }

        public AvaFighter GetFighter(int side, int slot) { return fighters[side][slot]; }

        bool RowState(int idx)
        {
            if (idx == 7) return sceneLayers != null && sceneLayers.GetStates().ContainsKey("Bloom") && sceneLayers.GetStates()["Bloom"];
            var st = pool[selType][0].layers.GetStates();
            if (idx == 2) return st["ParticlesFront"] && st["ParticlesBack"];
            return st[RowNames[idx]];
        }

        void SetRow(int idx, bool on)
        {
            if (idx == 7) { if (sceneLayers != null) sceneLayers.SetLayer("Bloom", on); return; }
            foreach (var l in pool) if (l != null) foreach (var e in l)
                    {
                        if (idx == 2) { e.layers.SetLayer("ParticlesFront", on); e.layers.SetLayer("ParticlesBack", on); }
                        else e.layers.SetLayer(RowNames[idx], on);
                    }
        }

        GUIStyle bs, ls;
        void OnGUI()
        {
            if (Recording) return;
            if (bs == null) { bs = new GUIStyle(GUI.skin.button) { fontSize = 22 }; ls = new GUIStyle(GUI.skin.label) { fontSize = 22, normal = { textColor = Color.white } }; }
            float sc = Screen.height / 1080f;
            GUI.matrix = Matrix4x4.Scale(new Vector3(sc, sc, 1));
            GUI.color = new Color(0, 0, 0, 0.75f); GUI.DrawTexture(new Rect(10, 10, 1260, 210), Texture2D.whiteTexture); GUI.color = Color.white;
            float x = 20, y = 18, h = 44;
            if (GUI.Button(new Rect(x, y, 130, h), "+ Left", bs)) AddFighter(0);
            if (GUI.Button(new Rect(x + 140, y, 130, h), "+ Right", bs)) AddFighter(1);
            if (GUI.Button(new Rect(x + 280, y, 130, h), "+ Both", bs)) { AddFighter(0); AddFighter(1); }
            if (GUI.Button(new Rect(x + 420, y, 170, h), "Reset 1v1", bs)) ResetFighters();
            GUI.backgroundColor = trade ? new Color(0.35f, 0.9f, 0.4f) : Color.white; if (GUI.Button(new Rect(x + 610, y, 170, h), trade ? "Trade: ON" : "Trade: off", bs)) trade = !trade; GUI.backgroundColor = Color.white;
            GUI.Label(new Rect(x + 790, y, 440, h), "L " + count[0] + "   R " + count[1] + "   type: " + TypeNames[selType], ls);
            y += 56;
            for (int i = 0; i < 4; i++)
                if (GUI.Button(new Rect(x + i * 140, y, 130, h), TypeNames[i], bs)) { selType = i; FireNow(i); }
            GUI.enabled = false;
            GUI.Button(new Rect(x + 4 * 140, y, 170, h), "Beam (later)", bs);
            GUI.Button(new Rect(x + 4 * 140 + 180, y, 170, h), "Slash (later)", bs);
            GUI.enabled = true;
            y += 60;
            GUI.Label(new Rect(x, y, 200, h), "Layers:", ls);
            for (int i = 0; i < RowNames.Length; i++)
            {
                bool cur = RowState(i);
                var st = new GUIStyle(GUI.skin.button) { fontSize = 22 };
                GUI.backgroundColor = cur ? new Color(0.35f, 0.9f, 0.4f) : new Color(0.6f, 0.3f, 0.3f);
                if (GUI.Button(new Rect(x + 100 + i * 142, y, 136, h), (cur ? "[x] " : "[ ] ") + RowNames[i], st)) SetRow(i, !cur);
                GUI.backgroundColor = Color.white;
            }
            y += 50;
            GUI.Label(new Rect(x, y, 1200, h), "Shots alternate sides; shooter and target are seeded random archers. Camera shake: off.", ls);
        }
    }
}
