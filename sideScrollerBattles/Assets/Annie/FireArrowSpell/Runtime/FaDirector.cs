using System.IO;
using System.Text;
using UnityEngine;

namespace Annie.FireArrowSpell
{
    /// <summary>Fire arrow test scene. The tick is the number of frames the capture has written, so capture frame n shows tick n. take: full = both spells, one = the first, bg = the first with the target hidden (silhouette reference).</summary>
    public class FaDirector : MonoBehaviour
    {
        public FaSheets sheets;
        public FaCast cast; public FaArrow arrow; public FaImpact impact;
        public string take = "full";
        public int tick = -1;
        FaFighter target;
        readonly StringBuilder tl = new StringBuilder();

        void Start()
        {
            var tg = new GameObject("R_archer_0"); tg.transform.SetParent(transform, false);
            target = tg.AddComponent<FaFighter>(); target.Init(sheets, FaGeo.IdleOff);
            if (take == "bg") tg.SetActive(false);
            var o = FaGeo.Orb; var c = FaGeo.Chest;
            tl.Append("plan C1=" + FaGeo.C1 + " L1=" + (FaGeo.C1 + FaGeo.ChargeFrames) + " I1=" + FaGeo.I1 + " C2=" + FaGeo.C2 + " I2=" + FaGeo.I2 + " idleOff=" + FaGeo.IdleOff + "\n");
            tl.Append("orb=(" + o.x.ToString("F2") + "," + o.y.ToString("F2") + ") chest=(" + c.x.ToString("F2") + "," + c.y.ToString("F2") + ") dist=" + Vector2.Distance(o, c).ToString("F1") + " step=" + FaGeo.StepPx.ToString("F2") + "\n");
        }

        void Update()
        {
            if (Time.captureFramerate == 0) return;
            var cap = GetComponent<AnnieCapture>();
            string fd = Path.Combine(cap.outDir, "frames");
            if (!Directory.Exists(fd)) return;
            tick = Directory.GetFiles(fd, "f*.png").Length;
            Step(tick);
        }

        void Step(int t)
        {
            if (t == FaGeo.C1) { cast.Begin(t, 0); arrow.Begin(t + FaGeo.ChargeFrames, 0); impact.Begin(FaGeo.I1, 0); }
            if (take == "full" && t == FaGeo.C2) { cast.Begin(t, 101); arrow.Begin(t + FaGeo.ChargeFrames, 101); impact.Begin(FaGeo.I2, 101); }
            if (t == FaGeo.I1 || (take == "full" && t == FaGeo.I2)) target.StartHurt(t);
            target.Tick(t);
            cast.Tick(t); arrow.Tick(t); impact.Tick(t);
            tl.Append(t).Append(": target ").Append(target.FrameLabel()).Append('\n');
        }

        void OnDestroy()
        {
            var cap = GetComponent<AnnieCapture>();
            if (cap != null && !string.IsNullOrEmpty(cap.outDir) && tick >= 0)
            {
                File.WriteAllText(Path.Combine(cap.outDir, "timeline.txt"), string.Join("\n", cast.log) + "\n" + string.Join("\n", arrow.log) + "\n" + string.Join("\n", impact.log) + "\n\n" + tl);
                File.WriteAllText(Path.Combine(cap.outDir, "path.json"), "{\"orb\":[" + FaGeo.Orb.x + "," + FaGeo.Orb.y + "],\"chest\":[" + FaGeo.Chest.x + "," + FaGeo.Chest.y + "],\"C1\":" + FaGeo.C1 + ",\"L1\":" + (FaGeo.C1 + FaGeo.ChargeFrames) + ",\"I1\":" + FaGeo.I1 + ",\"C2\":" + FaGeo.C2 + ",\"I2\":" + FaGeo.I2 + ",\"frames\":[\n" + arrow.pathJson.ToString().TrimEnd('\n', ',') + "\n]}\n");
            }
        }
    }
}
