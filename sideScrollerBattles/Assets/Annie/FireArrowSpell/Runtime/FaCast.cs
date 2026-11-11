using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Annie.FireArrowSpell
{
    /// <summary>The charge: wisps spiral in from a 95 px ring to the orb and feed a fireball that grows 12 -> 80 px across by C+17 (frame 22), holds with a flicker until C+21 (frame 26).</summary>
    public class FaCast : MonoBehaviour
    {
        public AnnieLayers layers;
        public Texture2D glowTex, wispTex, wispWhiteTex, emberWhiteTex;
        public SpriteRenderer glowOrange, centreY, centreW; public SpriteRenderer[] flames;
        public ParticleSystem wispsPs, embersPs;
        public Light2D light2d;
        public uint seedFront = 11, seedCore = 12, seedLinger = 13;
        public int C = -1000; uint off;
        public const float RingPx = 95f, PeakPx = 80f, StartPx = 12f;
        public List<string> log = new List<string>();
        FaPs wisps, embers; Sprite glow; Sprite[] wispTiles; float[] flick = new float[40]; float[] fAng = new float[4], fPh = new float[4];

        void Awake()
        {
            glow = FaUtil.Spr(glowTex, "glow_soft"); wispTiles = FaUtil.Tiles(wispTex, 4, 4, "wisp");
            foreach (var sr in new[] { glowOrange, centreY, centreW }) sr.sprite = glow;
            foreach (var sr in flames) sr.sprite = wispTiles[0];
            wisps = new FaPs(wispsPs); embers = new FaPs(embersPs); HideCore();
        }

        void HideCore() { glowOrange.enabled = false; centreY.enabled = false; centreW.enabled = false; foreach (var f in flames) f.enabled = false; if (light2d != null) light2d.enabled = false; }

        public void Begin(int c, uint seedOffset)
        {
            C = c; off = seedOffset;
            var rc = new System.Random((int)(seedCore + off));
            for (int i = 0; i < flick.Length; i++) flick[i] = (float)(rc.NextDouble() * 2 - 1);
            for (int i = 0; i < fAng.Length; i++) { fAng[i] = FaUtil.Rnd(rc, 0f, 360f); fPh[i] = FaUtil.Rnd(rc, 0f, 16f); }
            // wisps: a spiral from the ring to the orb, arriving by C+14 (frame 19)
            wisps.Clear(); var rw = new System.Random((int)(seedFront + off)); var orb = FaGeo.Orb;
            for (int n = 0; n <= 7; n++) for (int j = 0; j < 2; j++)
            {
                float a0 = FaUtil.Rnd(rw, 0f, 360f), life = FaUtil.Rnd(rw, 5.5f, 6.5f), turn = FaUtil.Rnd(rw, 0.7f, 1.0f), s0 = FaUtil.Rnd(rw, 24f, 30f);
                uint sd = (uint)rw.Next();
                wisps.recs.Add(new FaPs.Rec
                {
                    t0 = c + n + j * 0.5f, life = life, s0 = s0, s1 = s0 * 0.45f, seed = sd,
                    pos = u => orb + FaUtil.Pol(a0 + turn * 360f * u) * (RingPx * (1f - u)),
                    col = u => FaUtil.Fire(u, 0.7f, 1f)
                });
            }
            // embers dropping from the orb, drawn behind the caster
            embers.Clear(); var re = new System.Random((int)(seedLinger + off));
            for (int k = 0; k < 5; k++)
            {
                float t0 = c + 6 + k * 3.2f, life = FaUtil.Rnd(re, 14f, 20f), s0 = FaUtil.Rnd(re, 11f, 14f);
                Vector2 p0 = orb + new Vector2(FaUtil.Rnd(re, 22f, 40f), FaUtil.Rnd(re, -8f, 8f)), v = new Vector2(FaUtil.Rnd(re, 1.0f, 2.2f), FaUtil.Rnd(re, -0.3f, 0.4f)); uint sd = (uint)re.Next();
                embers.recs.Add(new FaPs.Rec
                {
                    t0 = t0, life = life, s0 = s0, s1 = s0 * 0.6f, seed = sd, align = false,
                    pos = u => { float a = u * life; return p0 + v * a + new Vector2(0f, 0.5f * 0.13f * a * a); },
                    col = u => { var e = FaUtil.EmberCol; e.a = Mathf.Clamp01((1f - u) / 0.4f); return e; }
                });
            }
        }

        public float Diameter(int n)
        {
            float d = n <= 17 ? Mathf.Lerp(StartPx, PeakPx, FaUtil.Smooth(n / 17f)) : PeakPx;
            if (n > 17) d *= 1f + 0.08f * flick[n % flick.Length];
            return d;
        }

        public void Tick(int t)
        {
            int n = t - C; var orb = FaGeo.Orb;
            wisps.Draw(t); embers.Draw(t);
            if (n < 0 || n > 21) { HideCore(); if (n >= 22 && n <= 26) FaUtil.Light(light2d, orb, 0.5f * (1f - (n - 21) / 5f)); else if (light2d != null) light2d.enabled = false; return; }
            float D = Diameter(n);
                        FaUtil.Show(glowOrange, null, orb, D * 1.45f, D * 1.45f, 1f, FaUtil.Orange, 0f);
            for (int i = 0; i < flames.Length; i++)
            {
                // flames wrap around the ball: they orbit it, tips trailing along the orbit
                float th = fAng[i] + (i * 360f / flames.Length) + n * 14f; float rr = 0.34f * D;
                Vector2 pp = orb + new Vector2(Mathf.Cos(th * Mathf.Deg2Rad), Mathf.Sin(th * Mathf.Deg2Rad)) * rr;
                int fr = ((int)(t * 24f / 30f) + (int)fPh[i]) % 16; float fs = D * 0.62f;
                FaUtil.Show(flames[i], wispTiles[fr], pp, fs, fs, 1f, Color.white, -th);
            }
            FaUtil.Show(centreY, null, orb, D * 0.8f, D * 0.8f, 1f, FaUtil.YellowOrange, 0f);
            FaUtil.Show(centreW, null, orb, D * 0.38f, D * 0.38f, 1f, FaUtil.WhiteYellow, 0f);
            float li = 0.5f * FaUtil.Smooth(n / 17f);
            FaUtil.Light(light2d, orb, li);
            log.Add(t + " cast n=" + n + " diameter=" + D.ToString("F1") + " light=" + li.ToString("F2"));
        }
    }
}
