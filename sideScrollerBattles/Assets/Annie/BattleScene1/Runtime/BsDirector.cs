using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Annie.BattleScene1
{
    [Serializable] public class BsFig { public string side; public int row, col; public float x, y, s; public int n, ox, oy; }
    [Serializable] public class BsEv { public int k, t; }
    [Serializable] public class BsPlanFig { public int ph0; public List<BsEv> evs; }
    [Serializable] public class BsShot { public int i_s, i_t, t_spawn, t_hit; public double[] G; public double[] anchor; }
    [Serializable] public class BsPlan { public int ticks; public List<BsPlanFig> pf; public List<BsShot> shots; }
    [Serializable] public class BsLane { public string name; public float speed; public int y0, rows; }
    [Serializable] public class BsArrow { public double[] nock, tip; public double length, shaft_y; public int[] size; }
    [Serializable] public class BsData
    {
        public BsPlan plan; public List<BsFig> figs; public List<BsLane> laneList; public BsArrow arrow;
        public float START, T_STILL, AX, AY; public int LW; public double step; public int tpf;
    }

    /// <summary>Battle scene 1: plays the approved idle-mix schedule on 30 archers over the scrolling layers. One tick = one Update (48 per second).</summary>
    public class BsDirector : MonoBehaviour
    {
        public TextAsset sceneJson;
        public bool scroll = false;            // false: every lane frozen at T_STILL
        public int startTick = -1;
        public int ticks = 1728;
        public string logPath = "";
        public Renderer[] laneRenderers;       // order = laneList
        public Renderer[] bodyR, weaponR;      // per figure
        public Material[] bodyMats, weaponMats; // per kind
        public Renderer[] arrowR, sparkR;      // per shot
        public int[] cols = { 8, 12, 12, 5, 5 };
        public int cell = 512;

        BsData d; int tick; int nF;
        int[][] kind, frame;
        int[] lastKey; 
        MaterialPropertyBlock mpb;
        StringBuilder log = new StringBuilder();
        bool written;
        Vector2[] sheetWH = new Vector2[5]; Vector2 sparkWH, arrWH;
        Material sparkMat;

        static readonly int RectId = Shader.PropertyToID("_Rect"), OffId = Shader.PropertyToID("_Off");
        static readonly int[] DUR = { 0, 384, 384, 60, 60 };

        public void SetSpark(Material m) { sparkMat = m; }

        void Awake()
        {
            d = JsonUtility.FromJson<BsData>(sceneJson.text);
            nF = d.figs.Count; mpb = new MaterialPropertyBlock();
            kind = new int[nF][]; frame = new int[nF][];
            for (int i = 0; i < nF; i++) BuildTrack(i);
            for (int k = 0; k < 5; k++) sheetWH[k] = new Vector2(bodyMats[k].mainTexture.width, bodyMats[k].mainTexture.height);
            tick = startTick; lastKey = new int[nF]; for (int i = 0; i < nF; i++) lastKey[i] = -1;
            for (int i = 0; i < nF; i++) { var f = d.figs[i]; int[] o = Origin(f); log.AppendLine("O " + i + " " + o[0] + " " + o[1] + " " + f.n); PlaceArcher(i, o); }
            // arrow / spark pools are placed per tick
        }

        // idle_mix_v2_preview.py track(): frame advances every tpf ticks from tick 0; an event lasts DUR; after it the idle restarts at 0 on the tick it ends
        void BuildTrack(int i)
        {
            int NT = d.plan.ticks, tpf = d.tpf; var pf = d.plan.pf[i];
            int[] kd = new int[NT], fr = new int[NT];
            for (int T = 0; T < NT; T++) { kd[T] = 0; fr[T] = (pf.ph0 + T / tpf) % 64; }
            foreach (var e in pf.evs)
            {
                int en = e.t + DUR[e.k];
                for (int T = 0; T < NT; T++)
                {
                    if (T >= e.t && T < en) { kd[T] = e.k; fr[T] = (T - e.t) / tpf; }
                    else if (T >= en) { kd[T] = 0; fr[T] = (((T - en) / tpf) % 64); }
                }
            }
            foreach (var e in pf.evs)
            {
                int en = e.t + DUR[e.k];
                for (int T = e.t; T < Math.Min(en, NT); T++) { kd[T] = e.k; fr[T] = (T - e.t) / tpf; }
            }
            kind[i] = kd; frame[i] = fr;
        }

        static int NOf(BsFig f) { return (int)Math.Round(512.0 * f.s, MidpointRounding.ToEven); }
        int[] Origin(BsFig f)
        {
            int n = NOf(f); double ax = d.AX * n / 512.0, ay = d.AY * n / 512.0;
            if (f.side == "enemy") ax = n - ax;
            return new[] { (int)Math.Round(f.x - ax, MidpointRounding.ToEven), (int)Math.Round(f.y - ay, MidpointRounding.ToEven) };
        }
        // image px (x right, y down) -> world (x, -y)
        static void PlaceQuad(Transform t, double cx, double cy, double w, double h, double rotDeg)
        {
            t.position = new Vector3((float)cx, (float)-cy, 0); t.localScale = new Vector3((float)w, (float)h, 1); t.rotation = Quaternion.Euler(0, 0, (float)rotDeg);
        }
        void PlaceArcher(int i, int[] o)
        {
            int n = NOf(d.figs[i]);
            PlaceQuad(bodyR[i].transform, o[0] + n / 2.0, o[1] + n / 2.0, n, n, 0);
            PlaceQuad(weaponR[i].transform, o[0] + n / 2.0, o[1] + n / 2.0, n, n, 0);
        }
        Vector4 CellRect(int k, int idx, bool flip)
        {
            int c = idx % cols[k], r = idx / cols[k]; Vector2 wh = sheetWH[k];
            float u0 = c * cell / wh.x, u1 = (c + 1) * cell / wh.x, v1 = 1f - r * cell / wh.y, v0 = 1f - (r + 1) * cell / wh.y;
            return flip ? new Vector4(u1, v0, u0, v1) : new Vector4(u0, v0, u1, v1);
        }

        void Update()
        {
            tick++;
            if (tick < 0 || tick >= ticks) { if (tick >= ticks + 2 && !written) WriteLog(); return; }
            Apply(tick);
        }
        void OnDestroy() { if (!written && log.Length > 0) WriteLog(); }

        void Apply(int T)
        {
            // scroll
            double t = scroll ? T / 48.0 : d.T_STILL;
            if (T == 0 || scroll)
            {
                for (int i = 0; i < laneRenderers.Length; i++)
                {
                    var l = d.laneList[i]; if (l.name == "L12_ground_haze") continue;
                    double off = (d.START + l.speed * t) % d.LW;
                    laneRenderers[i].GetPropertyBlock(mpb); mpb.SetFloat(OffId, (float)off); laneRenderers[i].SetPropertyBlock(mpb);
                }
            }
            var sb = new StringBuilder("S " + T);
            for (int i = 0; i < nF; i++)
            {
                int k = kind[i][T], fr = frame[i][T]; sb.Append(' ').Append(k).Append(' ').Append(fr);
                int key = k * 1000 + fr;
                if (key != lastKey[i])
                {
                    lastKey[i] = key; bool flip = d.figs[i].side == "enemy"; var rect = CellRect(k, fr, flip);
                    bodyR[i].sharedMaterial = bodyMats[k]; weaponR[i].sharedMaterial = weaponMats[k];
                    mpb.Clear(); mpb.SetVector(RectId, rect); bodyR[i].SetPropertyBlock(mpb); weaponR[i].SetPropertyBlock(mpb);
                }
            }
            log.AppendLine(sb.ToString());
            for (int q = 0; q < d.plan.shots.Count; q++)
            {
                var s = d.plan.shots[q]; var fs = d.figs[s.i_s]; var ft = d.figs[s.i_t];
                bool aOn = T >= s.t_spawn && T < s.t_hit;
                if (arrowR[q].enabled != aOn) arrowR[q].enabled = aOn;
                if (aOn)
                {
                    double dx = s.anchor[0] - s.G[0], dy = s.anchor[1] - s.G[1], D = Math.Sqrt(dx * dx + dy * dy), ux = dx / D, uy = dy / D;
                    double mx = s.G[0] + ux * d.step * (T - s.t_spawn), my = s.G[1] + uy * d.step * (T - s.t_spawn);
                    double k = NOf(fs) / 512.0 / 2.0; bool flip = ux < 0;
                    double phi = Math.Atan2(uy, ux) - (flip ? Math.PI : 0.0);
                    double w = d.arrow.size[0] * k, h = d.arrow.size[1] * k;
                    PlaceQuad(arrowR[q].transform, mx, my, w, h, -phi * 180.0 / Math.PI);
                    mpb.Clear(); mpb.SetVector(RectId, flip ? new Vector4(1, 0, 0, 1) : new Vector4(0, 0, 1, 1)); arrowR[q].SetPropertyBlock(mpb);
                    log.AppendLine("A " + T + " " + q + " " + mx.ToString("R") + " " + my.ToString("R"));
                }
                bool sOn = T >= s.t_hit && T < s.t_hit + 18;
                if (sparkR[q].enabled != sOn) sparkR[q].enabled = sOn;
                if (sOn)
                {
                    int idx = (T - s.t_hit) / 3; int n = NOf(ft);
                    int px = (int)Math.Round(s.anchor[0] - n / 2.0, MidpointRounding.ToEven), py = (int)Math.Round(s.anchor[1] - n / 2.0, MidpointRounding.ToEven);
                    PlaceQuad(sparkR[q].transform, px + n / 2.0, py + n / 2.0, n, n, 0);
                    mpb.Clear(); mpb.SetVector(RectId, new Vector4(idx / 6f, 0, (idx + 1) / 6f, 1)); sparkR[q].SetPropertyBlock(mpb);
                    log.AppendLine("P " + T + " " + q + " " + idx + " " + px + " " + py + " " + n);
                }
            }
        }

        void WriteLog()
        {
            written = true;
            if (!string.IsNullOrEmpty(logPath)) { Directory.CreateDirectory(Path.GetDirectoryName(logPath)); File.WriteAllText(logPath, log.ToString()); Debug.Log("BsDirector: log " + logPath); }
        }
    }
}
