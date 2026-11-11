using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Annie.EnemyDeath
{
    [Serializable]
    public class EdFigure
    {
        public string name;
        public EnemyDeath fx;
        public SpriteRenderer body, bow;
        public Sprite[] bodyFrames, bowFrames;
        public bool hitAnim = true;       // false: a single still frame
        public int fireTick;              // the fatal hit
        public int seed = 1;
        public int replayAtTick = -1;
        public bool active = true;
        [NonSerialized] public int playTick = -1;
    }

    /// <summary>Test director: hit frames 0..6 at 16 fps off 30 fps ticks, hold 6, then EnemyDeath.Play at the first tick that shows frame 6.
    /// The tick is the number of frames AnnieCapture has written, so frame n shows tick n. Never moves a body.</summary>
    public class EdDirector : MonoBehaviour
    {
        public List<EdFigure> figures = new List<EdFigure>();
        public string framesDir = "";
        public string logDir = "";
        public int totalTicks = 255;
        public int holdFrame = 6;
        int tick = -1, last = -1; bool written;
        readonly StringBuilder tickLog = new StringBuilder();

        void Awake()
        {
            foreach (var f in figures) { f.playTick = -1; if (f.fx != null) f.fx.autoStep = false; Show(f, 0); }
        }

        void Update()
        {
            int n;
            if (!string.IsNullOrEmpty(framesDir)) n = Directory.Exists(framesDir) ? Directory.GetFiles(framesDir, "f*.png").Length : 0;
            else { tick++; n = Mathf.Max(0, tick); }
            if (n > totalTicks) n = totalTicks;
            for (int t = last + 1; t <= n; t++) DoTick(t);
            last = Mathf.Max(last, n);
        }

        void Show(EdFigure f, int fr)
        {
            fr = Mathf.Clamp(fr, 0, f.bodyFrames.Length - 1);
            f.body.sprite = f.bodyFrames[fr];
            if (f.bow != null && f.bowFrames != null && f.bowFrames.Length > 0) f.bow.sprite = f.bowFrames[Mathf.Min(fr, f.bowFrames.Length - 1)];
        }

        void DoTick(int t)
        {
            foreach (var f in figures)
            {
                if (!f.active || f.fx == null) continue;
                int fr = 0;
                if (f.hitAnim && t >= f.fireTick) fr = Mathf.Min(holdFrame, (int)((t - f.fireTick) * 16f / 30f));
                bool arrive = f.hitAnim ? (fr >= holdFrame) : (t >= f.fireTick);
                if (f.playTick < 0 || (f.replayAtTick >= 0 && t == f.replayAtTick))
                {
                    if (arrive && f.playTick < 0)
                    {
                        Show(f, fr); f.playTick = t; f.fx.LogTag = t;
                        f.fx.Play(f.bow != null ? new[] { f.body, f.bow } : new[] { f.body }, f.seed);
                        tickLog.Append(f.name).Append(" PLAY ").Append(t).Append(' ').Append(f.fx.Summary).Append('\n');
                        continue;
                    }
                    if (f.replayAtTick >= 0 && t == f.replayAtTick)
                    {
                        Show(f, holdFrame); f.playTick = t; f.fx.LogTag = t; f.fx.Replay();
                        tickLog.Append(f.name).Append(" REPLAY ").Append(t).Append(' ').Append(f.fx.Summary).Append('\n');
                        continue;
                    }
                }
                if (f.playTick < 0) { Show(f, fr); continue; }
                Show(f, f.hitAnim ? holdFrame : 0);
                f.fx.LogTag = t; f.fx.Step(1f / 30f);
                tickLog.Append(f.name).Append(' ').Append(t).Append(' ').Append(f.fx.Progress.ToString("F5")).Append(' ').Append(f.fx.CutCount).Append('\n');
            }
            if (t >= totalTicks) WriteLogs();
        }

        void OnDestroy() { WriteLogs(); }

        void WriteLogs()
        {
            if (written || string.IsNullOrEmpty(logDir)) return;
            written = true;
            Directory.CreateDirectory(logDir);
            File.WriteAllText(Path.Combine(logDir, "tick_log.txt"), tickLog.ToString());
            foreach (var f in figures)
            {
                if (f.fx == null || f.playTick < 0) continue;
                File.WriteAllText(Path.Combine(logDir, "emit_log_" + f.name + ".txt"), "# tag group x y T pEff pixelIndex life srcR srcG srcB tintR tintG tintB driftPx risePx quadPx\n" + f.fx.EmitLog.ToString());
                File.WriteAllText(Path.Combine(logDir, "map_" + f.name + ".txt"), f.fx.MapText());
            }
        }
    }
}
