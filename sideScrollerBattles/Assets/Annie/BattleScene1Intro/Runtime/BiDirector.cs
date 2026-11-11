using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Annie.BattleScene1Intro
{
    [Serializable] public class BiFig { public string side; public int row, col; public float x, y, s; }
    [Serializable] public class BiLane { public string name; public float speed; public int y0, rows; }
    [Serializable] public class BiScene { public List<BiFig> figs; public List<BiLane> laneList; public float AX, AY; }
    [Serializable] public class BiPlan { public string take, caption; public int nt, nf, nl; public int[] kind, frame, ox; public float[] laneOff; }

    /// <summary>Battle scene 1 intro v4: plays a precomputed per-tick plan (intro_v4_plan.py): which installed cell each of the 30 archers shows and where its sprite sits, and the lane offsets. One tick = one Update (48 per second).</summary>
    public class BiDirector : MonoBehaviour
    {
        public TextAsset sceneJson, planJson;
        public int startTick = -2;
        public string logPath = "";
        public Renderer[] laneRenderers;       // order = laneList
        public Renderer[] bodyR, weaponR;      // per figure (scene.json order)
        public Material[] bodyMats, weaponMats; // per kind: 0 run, 1 stop, 2 idle
        public int[] cols = { 4, 8, 8 };
        public int cell = 512;

        BiScene sc; BiPlan p; int tick; int nF;
        int[] lastKey, oy, nn; float[] ax;
        MaterialPropertyBlock mpb;
        StringBuilder log = new StringBuilder();
        bool written;
        Vector2[] sheetWH = new Vector2[3];
        static readonly int RectId = Shader.PropertyToID("_Rect"), OffId = Shader.PropertyToID("_Off");

        void Awake()
        {
            sc = JsonUtility.FromJson<BiScene>(sceneJson.text); p = JsonUtility.FromJson<BiPlan>(planJson.text);
            nF = sc.figs.Count; mpb = new MaterialPropertyBlock();
            for (int k = 0; k < 3; k++) sheetWH[k] = new Vector2(bodyMats[k].mainTexture.width, bodyMats[k].mainTexture.height);
            tick = startTick; lastKey = new int[nF]; oy = new int[nF]; nn = new int[nF];
            for (int i = 0; i < nF; i++)
            {
                lastKey[i] = -1; var f = sc.figs[i];
                nn[i] = (int)Math.Round(512.0 * f.s, MidpointRounding.ToEven);
                oy[i] = (int)Math.Round(f.y - sc.AY * nn[i] / 512.0, MidpointRounding.ToEven);
            }
            log.AppendLine("take " + p.take + " nt " + p.nt);
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
            tick++;
            if (tick >= p.nt + 2) { if (!written) WriteLog(); return; }
            Apply(Mathf.Clamp(tick, 0, p.nt - 1), tick);
        }
        void OnDestroy() { if (!written && log.Length > 0) WriteLog(); }

        void Apply(int T, int raw)
        {
            for (int i = 0; i < laneRenderers.Length; i++)
            {
                if (sc.laneList[i].name == "L12_ground_haze") continue;
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
        }

        void WriteLog()
        {
            written = true;
            if (!string.IsNullOrEmpty(logPath)) { Directory.CreateDirectory(Path.GetDirectoryName(logPath)); File.WriteAllText(logPath, log.ToString()); Debug.Log("BiDirector: log " + logPath); }
        }
    }
}
