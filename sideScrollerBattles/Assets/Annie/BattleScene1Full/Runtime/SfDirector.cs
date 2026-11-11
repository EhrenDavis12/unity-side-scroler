using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using Annie.BattleScene1Wind;

namespace Annie.BattleScene1Full
{
    [Serializable] public class SfFig { public string side; public int row, col; public float x, y, s; }
    [Serializable] public class SfSceneData { public List<SfFig> figs; public float AX, AY; }
    [Serializable] public class SfShot { public int i_s, i_t, t_a, t_spawn, t_hit, A, phi; public bool fatal; public float[] G, P; }
    [Serializable] public class SfBar { public int x0, x1, y0, y1, cw, inner, W, Hh; public bool right; }
    [Serializable] public class SfCols { public int[] SEP, PAPER, RED, BLUE; }
    [Serializable] public class SfPlan
    {
        public int seed, nt, nf, nl; public int[] kind, frame, ox, hp, mp, nudge; public float[] laneOff;
        public List<SfShot> shots; public List<SfBar> bars; public SfCols colors;
    }

    /// <summary>Battle scene 1, everything together: plays the per-tick plan (plan.py): intro take B, then the fight. One tick = one Update (48 per second).
    /// Kinds: 0 run, 1 run stop, 2 idle, 3 head turn, 4 shift, 5 attack, 6 hit, 7 death. Draws the v2 hit effect pool, the arrows and the health bars.</summary>
    [DefaultExecutionOrder(-100)]
    public class SfDirector : MonoBehaviour
    {
        public string framesDir = "";                   // the capture's frames folder: the tick is the number of frames already written, so frame n always shows tick n (a run that renders its first frame one Update late no longer shifts)
        WwWind wind; FieldInfo windTick; int lastT = -1;
        public TextAsset sceneJson, planJson;
        public int startTick = -2;
        public int windShift = 0;                        // take D: the wind clock starts this many ticks late, so the fight sees take1c's wind phase
        public string logPath = "";
        public Renderer[] laneRenderers;
        public string[] laneNames;
        public Renderer[] bodyR, weaponR;
        public Material[] bodyMats, weaponMats;          // per kind 0..7
        public int[] cols = { 4, 8, 8, 12, 12, 5, 5, 8 };
        public int cell = 512;
        public SfEffect[] pool;
        public Renderer[] barR;
        public int[] figOrderBase;                       // per figure: sort order of the body (weapon +1)
        public int[] rowBase;                            // per figure: its row block base (effects use +50)
        public float[] kRow;                             // per figure: row factor (row 1 = 1)
        public Texture2D arrowTex;

        SfSceneData sc; SfPlan p; int tick; int nF;
        int[] lastKey, oy, nn; Texture2D[] barTex; int[] lastBar; int[] barX;
        MaterialPropertyBlock mpb;
        StringBuilder log = new StringBuilder();
        bool written;
        Vector2[] sheetWH = new Vector2[8];
        int[] fxOf; int[] nextStep; int[] slotOwner;
        static readonly int RectId = Shader.PropertyToID("_Rect"), OffId = Shader.PropertyToID("_Off");
        const float STEP = 2000f / 48f;

        void Awake()
        {
            sc = JsonUtility.FromJson<SfSceneData>(sceneJson.text); p = JsonUtility.FromJson<SfPlan>(planJson.text);
            nF = sc.figs.Count; mpb = new MaterialPropertyBlock();
            for (int k = 0; k < 8; k++) sheetWH[k] = new Vector2(bodyMats[k].mainTexture.width, bodyMats[k].mainTexture.height);
            tick = startTick; lastKey = new int[nF]; oy = new int[nF]; nn = new int[nF]; lastBar = new int[nF]; barX = new int[nF]; barTex = new Texture2D[nF];
            for (int i = 0; i < nF; i++)
            {
                lastKey[i] = -1; lastBar[i] = int.MinValue; var f = sc.figs[i];
                nn[i] = (int)Math.Round(512.0 * f.s, MidpointRounding.ToEven);
                oy[i] = (int)Math.Round(f.y - sc.AY * nn[i] / 512.0, MidpointRounding.ToEven);
                var b = p.bars[i];
                barTex[i] = new Texture2D(b.W, b.Hh, TextureFormat.RGBA32, false, false);   // sRGB texture, point filter: exact colours
                barTex[i].filterMode = FilterMode.Point; barTex[i].wrapMode = TextureWrapMode.Clamp;
                var m = new Material(Shader.Find("Annie/BsUnlit")); m.mainTexture = barTex[i]; m.SetVector("_Rect", new Vector4(0, 0, 1, 1));
                barR[i].sharedMaterial = m;
                barR[i].enabled = false;
            }
            wind = GetComponent<WwWind>(); if (wind != null) windTick = typeof(WwWind).GetField("tick", BindingFlags.NonPublic | BindingFlags.Instance);
            fxOf = new int[p.shots.Count]; nextStep = new int[p.shots.Count];
            for (int q = 0; q < fxOf.Length; q++) { fxOf[q] = -1; nextStep[q] = 0; }
            slotOwner = new int[Mathf.Max(1, pool.Length)]; for (int i = 0; i < slotOwner.Length; i++) slotOwner[i] = -1;
            log.AppendLine("take E opening, take1c fight, seed " + p.seed + " nt " + p.nt);
        }

        static void PlaceQuad(Transform t, double cx, double cy, double w, double h)
        {
            t.position = new Vector3((float)cx, (float)-cy, 0); t.localScale = new Vector3((float)w, (float)h, 1);
        }
        Vector4 CellRect(int k, int idx, bool flip)
        {
            int c = idx % cols[k], r = idx / cols[k]; Vector2 wh = sheetWH[k];
            float u0 = c * cell / wh.x, u1 = (c + 1) * cell / wh.x, v1 = 1f - r * cell / wh.y, v0 = 1f - (r + 1) * cell / wh.y;
            return flip ? new Vector4(u1, v0, u0, v1) : new Vector4(u0, v0, u1, v1);
        }

        void Update()
        {
            int n = 0;
            if (!string.IsNullOrEmpty(framesDir) && Directory.Exists(framesDir)) n = Directory.GetFiles(framesDir, "f*.png").Length;
            else { tick++; n = Mathf.Max(0, tick + 1); }
            tick = n;
            if (n >= p.nt) { if (!written) WriteLog(); return; }
            if (windTick != null) windTick.SetValue(wind, Mathf.Max(0, n - windShift) - 1);   // WwWind.Update adds one: the wind time follows the same tick (held at 0 through the blank: ticks 0-60 in take E)
            if (n == lastT) return;
            lastT = n;
            Apply(n, n);
        }
        void OnDestroy() { if (!written && log.Length > 0) WriteLog(); }

        void Apply(int T, int raw)
        {
            for (int i = 0; i < laneRenderers.Length; i++)
            {
                if (laneNames[i] == "L12_ground_haze") continue;
                laneRenderers[i].GetPropertyBlock(mpb); mpb.SetFloat(OffId, p.laneOff[T * p.nl + i]); laneRenderers[i].SetPropertyBlock(mpb);
            }
            var sb = new StringBuilder("S " + raw + " " + T);
            for (int i = 0; i < nF; i++)
            {
                int k = p.kind[T * nF + i], fr = p.frame[T * nF + i], ox = p.ox[T * nF + i]; int n = nn[i];
                sb.Append(' ').Append(k).Append(' ').Append(fr).Append(' ').Append(ox);
                PlaceQuad(bodyR[i].transform, ox + n / 2.0, oy[i] + n / 2.0, n, n); PlaceQuad(weaponR[i].transform, ox + n / 2.0, oy[i] + n / 2.0, n, n);
                int key = k * 1000 + fr;
                if (key != lastKey[i])
                {
                    lastKey[i] = key; bool flip = sc.figs[i].side == "enemy"; var rect = CellRect(k, fr, flip);
                    bodyR[i].sharedMaterial = bodyMats[k]; weaponR[i].sharedMaterial = weaponMats[k];
                    mpb.Clear(); mpb.SetVector(RectId, rect); bodyR[i].SetPropertyBlock(mpb); weaponR[i].SetPropertyBlock(mpb);
                }
            }
            log.AppendLine(sb.ToString());
            if (T == 0 || T == 12 || T == 204 || T == 299 || T == 300 || T == p.nt - 1 || raw == -1)
            {
                var lb = new StringBuilder("L " + raw + " " + T); for (int i = 0; i < p.nl; i++) lb.Append(' ').Append(p.laneOff[T * p.nl + i].ToString("R")); log.AppendLine(lb.ToString());
            }
            Bars(T, raw);
            Shots(T, raw);
        }

        void Bars(int T, int raw)
        {
            for (int i = 0; i < nF; i++)
            {
                int hp = p.hp[T * nF + i], mp = p.mp[T * nF + i], nd = p.nudge[T * nF + i];
                var b = p.bars[i];
                if (hp < 0) { if (barR[i].enabled) barR[i].enabled = false; lastBar[i] = int.MinValue; continue; }
                int key = hp * 1000 + mp;
                if (key != lastBar[i]) { lastBar[i] = key; DrawBar(i, hp, mp); }
                int x0 = b.right ? b.x0 + nd : b.x0 - nd;
                PlaceQuad(barR[i].transform, x0 + b.W / 2.0, b.y0 + b.Hh / 2.0, b.W, b.Hh);
                if (!barR[i].enabled) barR[i].enabled = true;
                log.AppendLine("B " + raw + " " + T + " " + i + " " + hp + " " + mp + " " + x0);
            }
        }

        static Color32 C(int[] a) { return new Color32((byte)a[0], (byte)a[1], (byte)a[2], 255); }
        void DrawBar(int i, int hp, int mp)
        {
            var b = p.bars[i]; int W = b.W, H = b.Hh; var px = new Color32[W * H];
            Color32 sep = C(p.colors.SEP), paper = C(p.colors.PAPER), red = C(p.colors.RED), blue = C(p.colors.BLUE);
            // image rows: y 0 = top; the texture row 0 is the bottom
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    Color32 c = sep;
                    int cx0 = 1, cx1 = 2 + b.cw;
                    bool inner = y >= 1 && y <= H - 2;
                    if (inner && x >= cx0 && x < cx0 + b.cw) c = (y >= H - 1 - hp) ? red : paper;
                    else if (inner && x >= cx1 && x < cx1 + b.cw) c = (y >= H - 1 - mp) ? blue : paper;
                    px[(H - 1 - y) * W + x] = c;
                }
            barTex[i].SetPixels32(px); barTex[i].Apply(false);
        }

        void Shots(int T, int raw)
        {
            for (int q = 0; q < p.shots.Count; q++)
            {
                var s = p.shots[q];
                if (T < s.t_spawn || T > s.t_hit + 150) continue;
                var fs = sc.figs[s.i_s]; var ft = sc.figs[s.i_t];
                double dx = s.P[0] - s.G[0], dy = s.P[1] - s.G[1], D = Math.Sqrt(dx * dx + dy * dy), ux = dx / D, uy = dy / D;
                double ka = nn[s.i_s] / 512.0 / 2.0;
                if (T == s.t_spawn && fxOf[q] < 0)
                {
                    int slot = q % pool.Length; if (pool[slot].busy) log.AppendLine("WARN pool slot busy " + q);
                    fxOf[q] = slot; slotOwner[slot] = q; var fx = pool[slot];
                    fx.HideAll();
                    fx.ArrowLenPx = (float)(410.0 * ka);
                    float aScale = (float)(100.0 * 490.0 / arrowTex.width * ka);
                    int rowS = rowBase[s.i_s], rowT = rowBase[s.i_t];
                    int arrowOrder = Mathf.Max(rowS, rowT) + 60;
                    float kEff = 100f * kRow[s.i_t];
                    var grip = new Vector3(s.G[0], -s.G[1], 0); var chest = new Vector3(s.P[0], -s.P[1], 0); var feet = new Vector3(ft.x, -ft.y, 0);
                    fx.Begin(grip, chest, feet, kEff, aScale, fs.side == "player" ? 1 : -1, 0, s.A, rowT + 50, arrowOrder);
                    log.AppendLine("E " + raw + " " + T + " " + q + " begin fx " + slot + " A " + s.A + " phi " + s.phi + " k " + kEff.ToString("R"));
                }
                if (fxOf[q] < 0) continue;
                if (slotOwner[fxOf[q]] != q) continue;   // a newer shot claimed this slot: the stale shot stops ticking it
                var f = pool[fxOf[q]];
                // 30 fps steps of this effect: step n lands on tick t_spawn + floor((8n + phi) / 5), so step A is exactly t_hit
                while ((8 * nextStep[q] + s.phi) / 5 + s.t_spawn <= T)
                {
                    int before = f.ImpactCount;
                    f.Tick(nextStep[q]);
                    if (nextStep[q] == s.A && f.ImpactCount > before) log.AppendLine("E " + raw + " " + T + " " + q + " impact (hit tick " + s.t_hit + ")");
                    nextStep[q]++;
                }
                if (T >= s.t_spawn && T < s.t_hit && f.InFlight)
                {
                    double mx = s.G[0] + ux * STEP * (T - s.t_spawn), my = s.G[1] + uy * STEP * (T - s.t_spawn);
                    f.PlaceFlight(new Vector3((float)mx, (float)-my, 0));
                    log.AppendLine("A " + raw + " " + T + " " + q + " " + mx.ToString("R") + " " + my.ToString("R"));
                }
            }
        }

        void WriteLog()
        {
            written = true;
            if (!string.IsNullOrEmpty(logPath)) { Directory.CreateDirectory(Path.GetDirectoryName(logPath)); File.WriteAllText(logPath, log.ToString()); Debug.Log("SfDirector: log " + logPath); }
        }
    }
}
