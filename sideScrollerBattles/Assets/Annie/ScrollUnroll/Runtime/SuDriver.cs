using System;
using System.IO;
using UnityEngine;

namespace Annie.ScrollUnroll
{
    /// <summary>
    /// ScrollUnroll: every piece's position, width and shader parameter is a function of the frame number only.
    /// While AnnieCapture records, the frame number is the count of frames already written (never a hand-kept offset).
    /// Takes: A modal rod rolls; B modal, rod stays, panel slides out, bundle enters from the left; C page-size panel, method of A.
    /// All positions are whole screen pixels (1920x1080 camera, 100 px per unit).
    /// </summary>
    public class SuDriver : MonoBehaviour
    {
        [Serializable] public class Piece { public Renderer r; [NonSerialized] public Material m; }
        [Serializable] public class TexSet { public Texture2D page, panel, gap, knobTop, knobBot, rod, hold; }

        public char take = 'A';
        public bool recording;
        public AnnieCapture capture;
        public Piece page, dim, panel, gap, contact, knobTop, knobBot, rod, hold;
        public TexSet modalSet, pageSet;
        public float scaleClosed = 1.4f;
        public float dimMax = 0.6f;
        public float peak = 0.34f;
        public float rampPx = 31f;
        public int lastFrame = -1;
        public string log = "";

        class G
        {
            public int restC, pivot, rodW, rodTop, rodH;
            public int knobTopDx, knobTopY, knobTopW, knobTopH, knobBotDx, knobBotY, knobBotW, knobBotH;
            public int gapDx, gapY, gapW, gapH;
            public int panelX, panelY, panelW, panelH;
            public int plantC, entry;
            public float efold;
            public int D { get { return entry + plantC - restC; } }
            public int unroll { get { return plantC - restC; } }
        }
        static readonly G Modal = new G {
            restC = 492, pivot = 34, rodW = 66, rodTop = 265, rodH = 551,
            knobTopDx = 461 - 492, knobTopY = 220, knobTopW = 77, knobTopH = 55,
            knobBotDx = 458 - 492, knobBotY = 816, knobBotW = 80, knobBotH = 58,
            gapDx = 524 - 492, gapY = 275, gapW = 14, gapH = 543,
            panelX = 524, panelY = 275, panelW = 943, panelH = 543,
            plantC = 1355, entry = 620, efold = 10.24f };
        static readonly G Pg = new G {
            restC = 186, pivot = 38, rodW = 74, rodTop = 340, rodH = 481,
            knobTopDx = 151 - 186, knobTopY = 295, knobTopW = 85, knobTopH = 54,
            knobBotDx = 148 - 186, knobBotY = 821, knobBotW = 88, knobBotH = 58,
            gapDx = 222 - 186, gapY = 349, gapW = 14, gapH = 476,
            panelX = 222, panelY = 349, panelW = 1554, panelH = 476,
            plantC = 1650, entry = 340, efold = 11.52f };

        G g;
        int openLen, holdLen = 60;
        public int TotalFrames { get { return take == 'C' ? 227 : 203; } }

        void Start()
        {
            g = take == 'C' ? Pg : Modal;
            openLen = take == 'C' ? 60 : 48;
            var set = take == 'C' ? pageSet : modalSet;
            Bind(page, set.page); Bind(dim, null); Bind(panel, set.panel); Bind(gap, set.gap); Bind(contact, set.panel);
            Bind(knobTop, set.knobTop); Bind(knobBot, set.knobBot); Bind(rod, set.rod); Bind(hold, set.hold);
        }

        static void Bind(Piece p, Texture2D t)
        {
            if (p == null || p.r == null) return;
            p.m = p.r.material;      // runtime instance
            if (t != null) p.m.SetTexture("_MainTex", t);
        }

        int FrameNow()
        {
            if (recording && capture != null && !string.IsNullOrEmpty(capture.outDir))
            {
                string d = Path.Combine(capture.outDir, "frames");
                return Directory.Exists(d) ? Directory.GetFiles(d, "f*.png").Length : 0;
            }
            return (int)(Time.unscaledTime * 60.0f) % TotalFrames;
        }

        void Update()
        {
            if (g == null) return;
            int f = FrameNow();
            if (f >= TotalFrames) f = TotalFrames - 1;
            Apply(f);
        }

        /// <summary>Open/hold/close time, 0..1, a palindrome around the hold.</summary>
        public float TimeAt(int f)
        {
            int os = 23, L = openLen;
            if (f <= os) return 0f;
            if (f <= os + L) return (f - os) / (float)L;
            if (f <= os + L + holdLen) return 1f;
            if (f <= os + 2 * L + holdLen) return (os + 2 * L + holdLen - f) / (float)L;
            return 0f;
        }

        static int Rnd(float v) { return Mathf.FloorToInt(v + 0.5f); }

        static void Place(Piece p, int left, int top, int w, int h, int order)
        {
            if (p == null || p.r == null) return;
            var t = p.r.transform;
            t.position = new Vector3((left + w * 0.5f - 960f) / 100f, (540f - (top + h * 0.5f)) / 100f, 5f - order * 0.1f);
            t.localScale = new Vector3(w / 100f, h / 100f, 1f);
        }

        public void Apply(int f)
        {
            lastFrame = f;
            float t = TimeAt(f);
            float e = 1f - Mathf.Pow(1f - t, 3f);          // ease-out cubic, no overshoot
            float d = g.D * e;                              // px of travel so far
            float P = g.entry;
            float c, panelLeft;
            float laid = Mathf.Clamp01((d - P) / g.unroll);
            if (take == 'B')
            {
                c = g.restC - Mathf.Max(0f, P - d);
                float curl = c + (g.panelX + g.panelW - g.plantC) + Mathf.Max(0f, d - P);
                panelLeft = curl - g.panelW;
            }
            else
            {
                c = g.restC + (g.D - d);
                panelLeft = g.panelX + Mathf.Max(0f, P - d);
            }
            float s = scaleClosed - (scaleClosed - 1f) * laid;
            int ci = Rnd(c), pl = Rnd(panelLeft);
            int rodWs = Rnd(g.rodW * s), rodLeft = ci - Rnd(g.pivot * s), rodRight = rodLeft + rodWs;
            float ramp = Mathf.Clamp01((g.D - d) / rampPx);

            // 0 page, 1 dim, 2 gap shadow, 3 panel, 4 contact shadow, 5 knob top, 6 knob bottom, 7 rod, 8 hold
            Place(page, 0, 0, 1920, 1080, 0);
            Place(dim, 0, 0, 1920, 1080, 1);
            Place(gap, ci + g.gapDx, g.gapY, g.gapW, g.gapH, 2);
            Place(panel, pl, g.panelY, g.panelW, g.panelH, 3);
            Place(contact, pl, g.panelY, g.panelW, g.panelH, 4);
            Place(knobTop, ci + g.knobTopDx, g.knobTopY, g.knobTopW, g.knobTopH, 5);
            Place(knobBot, ci + g.knobBotDx, g.knobBotY, g.knobBotW, g.knobBotH, 6);
            Place(rod, rodLeft, g.rodTop, rodWs, g.rodH, 7);
            Place(hold, 0, 0, 1920, 1080, 8);

            if (panel.m != null) { panel.m.SetFloat("_Mode", 1); panel.m.SetFloat("_ClipX", ci); }
            if (contact.m != null)
            {
                contact.m.SetFloat("_Mode", 2); contact.m.SetFloat("_ClipX", ci);
                contact.m.SetFloat("_ShadowR", rodRight); contact.m.SetFloat("_ShadowAmp", peak * ramp); contact.m.SetFloat("_ShadowEFold", g.efold);
            }
            if (dim.m != null)
            {
                float k = take == 'C' ? 0f : Mathf.Clamp01(t * openLen / 24f);
                dim.m.SetFloat("_Mode", 3); dim.m.SetFloat("_Alpha", dimMax * k);
                dim.m.SetVector("_Tint", new Vector4(74f / 255f, 46f / 255f, 26f / 255f, 1f));
            }
            if (page.m != null) page.m.SetFloat("_Mode", 0);
            if (gap.m != null) gap.m.SetFloat("_Mode", 0);
            if (knobTop.m != null) knobTop.m.SetFloat("_Mode", 0);
            if (knobBot.m != null) knobBot.m.SetFloat("_Mode", 0);
            if (rod.m != null) rod.m.SetFloat("_Mode", 0);
            if (hold.m != null) hold.m.SetFloat("_Mode", 0);
            if (hold.r != null) hold.r.enabled = t >= 1f;
            log = "f" + f + " t=" + t.ToString("0.0000") + " d=" + d.ToString("0.00") + " c=" + ci + " s=" + s.ToString("0.0000") + " rod=" + rodLeft + ".." + rodRight + " panelX=" + pl + " ramp=" + ramp.ToString("0.00");
        }
    }
}
