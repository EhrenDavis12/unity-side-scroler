using System.IO;
using System.Text;
using UnityEngine;

namespace Annie.StarSageSpell
{
    /// <summary>
    /// The Star Sage spell test scene: a still caster on the left, the Archer target on the right who idles and plays his hit sheet on each impact frame.
    /// The tick is the number of frames the capture has already written, so capture frame n shows tick n.
    /// take: "full" = both spells; "one" = the first spell only.
    /// </summary>
    public class SsDirector : MonoBehaviour
    {
        public SsSheets sheets;
        public SsCast cast; public SsBolt bolt; public SsImpact impact;
        public string take = "full";
        public int tick = -1;
        SsFighter target;
        SsGeo.Plan plan;
        readonly StringBuilder tl = new StringBuilder();

        void Start()
        {
            plan = SsGeo.MakePlan();
            var tg = new GameObject("R_archer_0"); tg.transform.SetParent(transform, false);
            target = tg.AddComponent<SsFighter>(); target.Init(sheets, plan.idleOff);
            tl.Append("plan C1=" + plan.C1 + " I1=" + plan.I1 + " hurtEnd1=" + plan.hurtEnd1 + " C2=" + plan.C2 + " I2=" + plan.I2 + " frames=" + plan.frames + " idleOff=" + plan.idleOff + "\n");
            var o = SsGeo.Orb; var c = SsGeo.Chest;
            tl.Append("orb=(" + o.x.ToString("F2") + "," + o.y.ToString("F2") + ") chest=(" + c.x.ToString("F2") + "," + c.y.ToString("F2") + ") dist=" + Vector2.Distance(o, c).ToString("F1") + "\n");
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
            if (t == plan.C1) { cast.Begin(t, 0); bolt.Begin(t, 0); impact.Begin(plan.I1, 0); }
            if (take == "full" && t == plan.C2) { cast.Begin(t, 101); bolt.Begin(t, 101); impact.Begin(plan.I2, 101); }
            if (t == plan.I1 || (take == "full" && t == plan.I2)) target.StartHurt(t);
            target.Tick(t);
            cast.Tick(t); bolt.Tick(t); impact.Tick(t);
            tl.Append(t).Append(": target ").Append(target.FrameLabel()).Append('\n');
        }

        void OnDestroy()
        {
            var cap = GetComponent<AnnieCapture>();
            if (cap != null && !string.IsNullOrEmpty(cap.outDir) && tick >= 0)
            {
                File.WriteAllText(Path.Combine(cap.outDir, "timeline.txt"), string.Join("\n", cast.log) + "\n" + string.Join("\n", bolt.log) + "\n" + string.Join("\n", impact.log) + "\n\n" + tl);
                File.WriteAllText(Path.Combine(cap.outDir, "path.json"), "{\"orb\":[" + SsGeo.Orb.x + "," + SsGeo.Orb.y + "],\"chest\":[" + SsGeo.Chest.x + "," + SsGeo.Chest.y + "],\"C1\":" + plan.C1 + ",\"I1\":" + plan.I1 + ",\"C2\":" + plan.C2 + ",\"I2\":" + plan.I2 + ",\"frames\":[\n" + bolt.pathJson.ToString().TrimEnd('\n', ',') + "\n]}\n");
            }
        }
    }
}
