using System.IO;
using System.Text;
using UnityEngine;

namespace Annie.ArrowMiss
{
    /// <summary>
    /// The arrow-miss test scene: a shooter on the left, a target on the right who stays in idle. The tick is the number of frames the capture has already written,
    /// so capture frame n shows tick n. Idle phases are those of archer_vs_archer/v2 (shooter 25, target 51 on tick 0); the attack starts on tick 15.
    /// </summary>
    public class AmDirector : MonoBehaviour
    {
        public AmSheets sheets;
        public AmEffect fx;
        public string take = "long";
        public int attackStart = 15;
        public int shooterIdleOff = 25, targetIdleOff = 51;

        public int tick = -1;
        AmFighter shooter, target;
        readonly StringBuilder tl = new StringBuilder();

        void Start()
        {
            var sg = new GameObject("L_archer_0"); sg.transform.SetParent(transform, false);
            shooter = sg.AddComponent<AmFighter>(); shooter.Init(sheets, 0, shooterIdleOff);
            var tg = new GameObject("R_archer_0"); tg.transform.SetParent(transform, false);
            target = tg.AddComponent<AmFighter>(); target.Init(sheets, 1, targetIdleOff);
            fx.Setup(take);
        }

        void Update()
        {
            if (Time.captureFramerate == 0) return;
            var cap = GetComponent<AnnieCapture>();
            string fd = Path.Combine(cap.outDir, "frames");
            if (!Directory.Exists(fd)) return;
            tick = Directory.GetFiles(fd, "f*.png").Length;
            Step();
        }

        void Step()
        {
            int t = tick;
            if (t == attackStart) shooter.StartAttack(t);
            shooter.Tick(t); target.Tick(t);
            fx.Tick(t);
            tl.Append(t).Append(": shooter ").Append(shooter.FrameLabel()).Append(" | target ").Append(target.FrameLabel()).Append('\n');
        }

        void OnDestroy()
        {
            var cap = GetComponent<AnnieCapture>();
            if (cap != null && !string.IsNullOrEmpty(cap.outDir) && tick >= 0)
            {
                File.WriteAllText(Path.Combine(cap.outDir, "timeline.txt"), string.Join("\n", fx.log) + "\n\n" + tl);
                string pj = fx.pathJson.ToString().TrimEnd('\n', ',');
                File.WriteAllText(Path.Combine(cap.outDir, "path.json"), pj + "\n]}\n");
            }
        }
    }
}
