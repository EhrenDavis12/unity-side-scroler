using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Annie.FireArrowSpell
{
    /// <summary>The fire arrow in flight, launch L (frame 27) .. impact L+12 (frame 39), 77.9 px a frame along the orb -> chest line.</summary>
    public class FaArrow : MonoBehaviour
    {
        public AnnieLayers layers;
        public Texture2D coreTex, coreWhiteTex, glowTex, wispWhiteTex, emberWhiteTex;
        public SpriteRenderer halo, head, headGlow;
        public Transform trailHead; public TrailRenderer trail;
        public ParticleSystem strandAPs, strandBPs, lingerPs;
        public Light2D light2d;
        public uint seedStrandA = 21, seedStrandB = 22, seedLinger = 23, seedCore = 24;
        public int L = -1000; uint off;
        public const float HeadPx = 120f; public const float TrailTime = 0.19f;
        public List<string> log = new List<string>(); public StringBuilder pathJson = new StringBuilder();
        FaPs sa, sb, lg; Sprite coreSpr, coreWhiteSpr, glowSpr;

        void Awake()
        {
            coreSpr = FaUtil.Spr(coreTex, "fire_arrow_core"); coreWhiteSpr = FaUtil.Spr(coreWhiteTex, "fire_arrow_core_white"); glowSpr = FaUtil.Spr(glowTex, "glow_soft");
            head.sprite = coreSpr; halo.sprite = coreWhiteSpr; headGlow.sprite = glowSpr;
            sa = new FaPs(strandAPs); sb = new FaPs(strandBPs); lg = new FaPs(lingerPs); Hide();
        }

        public void Hide() { head.enabled = false; halo.enabled = false; headGlow.enabled = false; if (trail != null) trail.emitting = false; if (light2d != null) light2d.enabled = false; }

        public void Begin(int launch, uint seedOffset)
        {
            L = launch; off = seedOffset;
            if (trail != null) { trail.Clear(); trail.time = TrailTime; }
            var perp = FaGeo.Perp; var dir = FaGeo.Dir;
            // two swirling strands: 16 px x sin(2 pi t / 4 frames), the second half a turn behind; particles stay where they are emitted
            var ra = new System.Random((int)(seedStrandA + off)); var rb = new System.Random((int)(seedStrandB + off));
            sa.Clear(); sb.Clear();
            for (int t = 0; t <= FaGeo.FlightFrames; t++) for (int j = 0; j < 3; j++)
            {
                float tau = t - (2 - j) / 3f; if (tau < 0f) continue;
                foreach (int s in new[] { 0, 1 })
                {
                    var rr = s == 0 ? ra : rb; var set = s == 0 ? sa : sb;
                    float ph = s * Mathf.PI, life = FaUtil.Rnd(rr, 6f, 7f), s0 = FaUtil.Rnd(rr, 19f, 23f); uint sd = (uint)rr.Next();
                    Vector2 p0 = FaGeo.HeadAt(tau) + perp * (16f * Mathf.Sin(2f * Mathf.PI * tau / 4f + ph));
                    life = Mathf.Min(life, launch + FaGeo.FlightFrames + 2f - (launch + tau)); if (life < 0.5f) continue;
                    set.recs.Add(new FaPs.Rec { t0 = launch + tau, life = life, s0 = s0, s1 = s0 * 0.4f, seed = sd, pos = u => p0,
                        col = u => FaUtil.Fire(u, 0.0f, 1.5f), align = true, rot0 = 90f + (float)(rr.NextDouble() * 30 - 15) });
                }
            }
            // linger: small fire bits shed along the path, life 10-15 frames, drift up 10-20 px, fade. Not near the target's body: behind it by order, and shed stops at n=10
            var rl = new System.Random((int)(seedLinger + off)); lg.Clear();
            for (int n = 2; n <= 10; n++) for (int j = 0; j < 2; j++)
            {
                float tau = n - j * 0.5f, life = FaUtil.Rnd(rl, 10f, 15f), rise = FaUtil.Rnd(rl, 10f, 20f), s0 = FaUtil.Rnd(rl, 11f, 14f); uint sd = (uint)rl.Next();
                Vector2 p0 = FaGeo.HeadAt(tau) + perp * FaUtil.Rnd(rl, -9f, 9f) - dir * FaUtil.Rnd(rl, 20f, 55f);
                lg.recs.Add(new FaPs.Rec { t0 = launch + tau, life = life, s0 = s0, s1 = s0 * 0.5f, seed = sd, align = false,
                    pos = u => p0 + new Vector2(0f, -rise * u), col = u => FaUtil.Fire(u * 0.8f + 0.1f, 0.35f, 1.3f) });
            }
        }

        public void Tick(int t)
        {
            int n = t - L; var orb = FaGeo.Orb;
            sa.Draw(t); sb.Draw(t); lg.Draw(t);
            if (n < 0 || n > FaGeo.FlightFrames) { Hide(); return; }
            Vector2 pos = FaGeo.HeadAt(n); var d = FaGeo.Dir; float ang = -Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            float flick = 1f + 0.04f * Mathf.Sin(t * 2.1f);
            FaUtil.Show(halo, null, pos, HeadPx * 1.25f, 52f, 0.5f, FaUtil.Orange, ang);
            FaUtil.Show(head, null, pos, HeadPx * flick, 40f * flick, 1f, Color.white, ang);
            FaUtil.Show(headGlow, null, pos + d * 22f, 38f, 38f, 0.95f, FaUtil.WhiteYellow, 0f);
            var pw = FaGeo.PxToWorld(pos); trailHead.position = pw;
            if (n == 0 && trail.gameObject.activeInHierarchy)
            {
                trail.Clear(); trail.time = TrailTime; trail.emitting = true;
                var wu = new Vector3(d.x, -d.y, 0f);
                trail.AddPosition(pw - wu * 0.8f); trail.AddPosition(pw);
            }
            if (n == FaGeo.FlightFrames) { trail.emitting = false; trail.time = 0.05f; }   // the ribbon dies with the head
            FaUtil.Light(light2d, pos, 0.4f);
            log.Add(t + " arrow n=" + n + " pos=(" + pos.x.ToString("F1") + "," + pos.y.ToString("F1") + ")");
            pathJson.Append("{\"t\":").Append(t).Append(",\"n\":").Append(n).Append(",\"c\":[").Append(pos.x.ToString("F2")).Append(',').Append(pos.y.ToString("F2")).Append("]},\n");
        }
    }
}
