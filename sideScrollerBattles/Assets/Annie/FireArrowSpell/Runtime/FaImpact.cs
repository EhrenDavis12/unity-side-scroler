using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Annie.FireArrowSpell
{
    /// <summary>The burst at the target's chest. Flash on n=0 only; core n 0..8 (frames 39-47); front and back particles n 0..12; light n 0..7; embers n 0..46.</summary>
    public class FaImpact : MonoBehaviour
    {
        public AnnieLayers layers;
        public Texture2D glowTex, wispTex, wispWhiteTex, sparkTex, emberWhiteTex;
        public Texture2D burstTex, burstWhiteTex;
        public SpriteRenderer flash, coreGlow, coreCentreY, coreCentreW, burst;
        public ParticleSystem frontWispPs, frontSparkPs, backWispPs, backSparkPs, lingerPs;
        public Light2D light2d;
        public uint seedFront = 31, seedBack = 32, seedLinger = 33, seedCore = 34;
        public int I = -1000; uint off;
        public List<string> log = new List<string>();
        // the target's silhouette over its hit and idle frames (screen px) plus a margin: embers never spawn inside it
        public static Rect Keepout = new Rect(1330f, 730f, 330f, 320f);
        public const float BurstTilePx = 200f;
        static readonly Color BurstTint = new Color(1f, 0.97f, 0.85f, 1f);   // the drawn white centre stays under 245 so the only white frame is the flash
        static readonly float[] GlowAlpha = { 0.9f, 0.9f, 0.8f, 0.6f, 0.4f, 0.2f, 0f, 0f, 0f };
        static readonly float[] CoreSize = { 55f, 105f, 135f, 140f, 135f, 125f, 110f, 90f, 65f };
        static readonly float[] CoreAlpha = { 1f, 1f, 1f, 1f, 0.9f, 0.75f, 0.55f, 0.35f, 0.15f };
        FaPs fw, fs, bw, bs, lg; Sprite glow; Sprite[] burstTiles;

        void Awake()
        {
            glow = FaUtil.Spr(glowTex, "glow_soft"); burstTiles = FaUtil.Tiles(burstTex, 4, 4, "burst");
            foreach (var sr in new[] { flash, coreGlow, coreCentreY, coreCentreW }) sr.sprite = glow;
            burst.sprite = burstTiles[0];
            fw = new FaPs(frontWispPs); fs = new FaPs(frontSparkPs); bw = new FaPs(backWispPs); bs = new FaPs(backSparkPs); lg = new FaPs(lingerPs); Hide();
        }

        public void Hide() { flash.enabled = false; coreGlow.enabled = false; coreCentreY.enabled = false; coreCentreW.enabled = false; burst.enabled = false; if (light2d != null) light2d.enabled = false; }

        // a thrown particle: speed v px/frame at the start, easing to 0 at the end of its life
        FaPs.Rec Throw(float t0, float life, Vector2 p0, Vector2 v, float rise, float s0, float s1, uint sd, float aStart, float aPow)
        {
            return new FaPs.Rec { t0 = t0, life = life, s0 = s0, s1 = s1, seed = sd,
                pos = u => p0 + v * life * (u - 0.5f * u * u) + new Vector2(0f, -rise * u * u), col = u => FaUtil.Fire(u, aStart, aPow) };
        }

        public void Begin(int i, uint seedOffset)
        {
            I = i; off = seedOffset; var chest = FaGeo.Chest;
                        var rf = new System.Random((int)(seedFront + off)); fw.Clear(); fs.Clear();
            for (int k = 0; k < 7; k++)
                fw.recs.Add(Throw(i, FaUtil.Rnd(rf, 7f, 10f), chest + new Vector2(FaUtil.Rnd(rf, -6f, 4f), FaUtil.Rnd(rf, -8f, 8f)), FaUtil.Pol(180f + FaUtil.Rnd(rf, -38f, 38f)) * FaUtil.Rnd(rf, 12f, 28f), FaUtil.Rnd(rf, 4f, 14f), FaUtil.Rnd(rf, 26f, 32f), 10f, (uint)rf.Next(), 0.3f, 1.2f));
            for (int k = 0; k < 5; k++)
                fs.recs.Add(Throw(i, FaUtil.Rnd(rf, 6f, 9f), chest + new Vector2(FaUtil.Rnd(rf, -6f, 4f), FaUtil.Rnd(rf, -8f, 8f)), FaUtil.Pol(180f + FaUtil.Rnd(rf, -45f, 45f)) * FaUtil.Rnd(rf, 16f, 34f), 0f, FaUtil.Rnd(rf, 20f, 26f), 8f, (uint)rf.Next(), 0.3f, 1.2f));
            var rb = new System.Random((int)(seedBack + off)); bw.Clear(); bs.Clear();
            var edge = new Vector2(FaGeo.TargetEdgeX, chest.y);
            for (int k = 0; k < 9; k++)
                bw.recs.Add(Throw(i, FaUtil.Rnd(rb, 8f, 10f), edge + new Vector2(0f, FaUtil.Rnd(rb, -30f, 30f)), FaUtil.Pol(FaUtil.Rnd(rb, -24f, 24f)) * FaUtil.Rnd(rb, 40f, 80f), FaUtil.Rnd(rb, 0f, 10f), FaUtil.Rnd(rb, 32f, 40f), 12f, (uint)rb.Next(), 0.35f, 1.2f));
            for (int k = 0; k < 6; k++)
                bs.recs.Add(Throw(i, FaUtil.Rnd(rb, 7f, 9f), edge + new Vector2(0f, FaUtil.Rnd(rb, -30f, 30f)), FaUtil.Pol(FaUtil.Rnd(rb, -28f, 28f)) * FaUtil.Rnd(rb, 45f, 85f), 0f, FaUtil.Rnd(rb, 22f, 28f), 8f, (uint)rb.Next(), 0.35f, 1.2f));
            // embers: spawned outside the target's silhouette, drawn behind it, drift up 20-40 px and fade
            var re = new System.Random((int)(seedLinger + off)); lg.Clear();
            var ctr = new Vector2(1485f, 900f); int made = 0, guard = 0;
            while (made < 16 && guard++ < 400)
            {
                float a = FaUtil.Rnd(re, 0f, 360f), rx = FaUtil.Rnd(re, 120f, 260f), ry = FaUtil.Rnd(re, 150f, 240f);
                Vector2 p0 = ctr + new Vector2(Mathf.Cos(a * Mathf.Deg2Rad) * rx, Mathf.Sin(a * Mathf.Deg2Rad) * ry);
                float t0 = i + FaUtil.Rnd(re, 0f, 11f), life = FaUtil.Rnd(re, 28f, 35f), rise = FaUtil.Rnd(re, 20f, 40f), s0 = FaUtil.Rnd(re, 11f, 14f); float sway = FaUtil.Rnd(re, -8f, 8f); uint sd = (uint)re.Next();
                if (Keepout.Contains(p0) || Keepout.Contains(p0 + new Vector2(sway, -rise)) || p0.y > 1035f || p0.x > 1900f) continue;
                made++;
                lg.recs.Add(new FaPs.Rec { t0 = t0, life = life, s0 = s0, s1 = s0 * 0.55f, seed = sd, align = false,
                    pos = u => p0 + new Vector2(sway * u, -rise * u), col = u => { var e = FaUtil.EmberCol; e.a = Mathf.Clamp01((1f - u) / 0.45f); return e; } });
            }
        }

        public void Tick(int t)
        {
            int n = t - I; var chest = FaGeo.Chest;
            fw.Draw(t); fs.Draw(t); bw.Draw(t); bs.Draw(t); lg.Draw(t);
            if (n < 0 || n > 46) { Hide(); return; }
            if (n == 0) FaUtil.Show(flash, null, chest, 150f, 150f, 1f, Color.white, 0f); else flash.enabled = false;
            if (n <= 8)
            {
                // fire_burst flipbook: 16 cells spread over the 9 frames (0,2,4,6,8,9,11,13,15), never held; a soft glow under it that is gone by n=6
                int cell = Mathf.RoundToInt(n * 15f / 8f);
                FaUtil.Show(burst, burstTiles[cell], chest, BurstTilePx, BurstTilePx, 1f, BurstTint, 0f);
                float ga = GlowAlpha[n];
                if (ga > 0f) FaUtil.Show(coreGlow, null, chest, 120f, 120f, ga, FaUtil.Orange, 0f); else coreGlow.enabled = false;
                float ca = Mathf.Clamp01(1.3f - n / 3.5f);
                if (ca > 0f) { FaUtil.Show(coreCentreY, null, chest, 60f, 60f, ca, FaUtil.YellowOrange, 0f); FaUtil.Show(coreCentreW, null, chest, 32f, 32f, ca, FaUtil.WhiteYellow, 0f); }
                else { coreCentreY.enabled = false; coreCentreW.enabled = false; }
            }
            else { coreGlow.enabled = false; coreCentreY.enabled = false; coreCentreW.enabled = false; burst.enabled = false; }
            float li = n <= 7 ? 0.45f * (1f - n / 8f) : 0f;
            if (light2d != null) { light2d.color = new Color(1f, 0.78f, 0.5f); }
            FaUtil.Light(light2d, chest, li);
            log.Add(t + " impact n=" + n + (n == 0 ? " FLASH" : "") + " light=" + li.ToString("F2"));
        }
    }
}
