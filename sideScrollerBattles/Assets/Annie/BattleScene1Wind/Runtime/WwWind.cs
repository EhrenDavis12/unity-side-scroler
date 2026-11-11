using System;
using System.IO;
using UnityEngine;

namespace Annie.BattleScene1Wind
{
    /// <summary>Drives the wind shader: global time (one tick = 1/48 s, same count as BsDirector) and each lane's peak push.
    /// A lane's peak is its take peak while its "Wind_*" layer toggle is on, else 0 (exactly the approved look).</summary>
    public class WwWind : MonoBehaviour
    {
        [Serializable] public class Lane { public string layerName; public Renderer renderer; public float peakA, peakB; }
        public AnnieLayers layers;
        public Lane[] lanes;
        public int take = 1;            // 1 = A (gentle), 2 = B (stronger)
        public int startTick = -2;      // same start as BsDirector so the frames line up
        public float speedPxPerSec = 240f;
        public string logPath = "";
        int tick; MaterialPropertyBlock mpb; float[] applied;
        static readonly int PeakId = Shader.PropertyToID("_Peak"), TId = Shader.PropertyToID("_WwT"), SId = Shader.PropertyToID("_WwSpeed");

        void Awake() { tick = startTick; mpb = new MaterialPropertyBlock(); applied = new float[lanes.Length]; for (int i = 0; i < applied.Length; i++) applied[i] = -1f; }

        float PeakOf(int i)
        {
            var l = lanes[i]; bool on = false;
            foreach (var x in layers.layers) if (x.name == l.layerName) on = x.on;
            return on ? (take == 2 ? l.peakB : l.peakA) : 0f;
        }

        void Start() { Push(); WriteLog(); }
        void Update() { tick++; Push(); }

        void Push()
        {
            Shader.SetGlobalFloat(TId, Mathf.Max(0, tick) / 48f);
            Shader.SetGlobalFloat(SId, speedPxPerSec);
            for (int i = 0; i < lanes.Length; i++)
            {
                float p = PeakOf(i);
                if (p == applied[i]) continue;
                applied[i] = p;
                var r = lanes[i].renderer; r.GetPropertyBlock(mpb); mpb.SetFloat(PeakId, p); r.SetPropertyBlock(mpb);
            }
        }

        void WriteLog()
        {
            if (string.IsNullOrEmpty(logPath)) return;
            var s = "{\"take\":\"" + (take == 2 ? "B" : "A") + "\",\"speed_px_s\":" + speedPxPerSec + ",\"startTick\":" + startTick + ",\"lanes\":[";
            for (int i = 0; i < lanes.Length; i++) s += (i > 0 ? "," : "") + "{\"layer\":\"" + lanes[i].layerName + "\",\"peak_px\":" + PeakOf(i) + "}";
            File.WriteAllText(logPath, s + "]}");
        }
    }
}
