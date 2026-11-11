using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Annie.StarSageSpell
{
    /// <summary>Glow at the staff: builds C..C+12, flash and release at C+12, fades by C+16.</summary>
    public class SsCast : MonoBehaviour
    {
        public AnnieLayers layers;
        public Texture2D glowTex;
        public SpriteRenderer glowOuter, glowInner, flash;
        public ParticleSystem front, back, linger;
        public Light2D light2d;
        public uint seedFront = 11, seedBack = 12, seedLinger = 13;
        public int C = -1000; uint off;
        public List<string> log = new List<string>();

        void Awake()
        {
            var g = SsUtil.Spr(glowTex, "glow_soft");
            foreach (var sr in new[] { glowOuter, glowInner, flash }) sr.sprite = g;
            Hide();
        }

        public void Hide()
        {
            glowOuter.enabled = false; glowInner.enabled = false; flash.enabled = false; if (light2d != null) light2d.enabled = false;
        }

        public void Begin(int c, uint seedOffset) { C = c; off = seedOffset; }

        public void Tick(int t)
        {
            int n = t - C; var orb = SsGeo.Orb;
            if (n < 0 || n > 18) { Hide(); return; }
            if (n == 0)
            {
                SsUtil.Fire(front, SsGeo.PxToWorld(orb) + new Vector3(0, 0, 0), Vector3.forward, seedFront + off);
                SsUtil.Fire(back, SsGeo.PxToWorld(orb), Vector3.forward, seedBack + off);
            }
            if (n == 12) SsUtil.Fire(linger, SsGeo.PxToWorld(orb), Vector3.up, seedLinger + off);
            // core: scale 0.4 -> 1.0 over C..C+12 (peak radius 36 px), holds, fades by C+16
            float k = n <= 12 ? 0.4f + 0.6f * n / 12f : 1f;
            float a = n <= 12 ? 0.45f + 0.55f * n / 12f : Mathf.Clamp01(1f - (n - 12) / 4f);
            float outer = 72f * 128f / 120f * k;
            if (a > 0f)
            {
                SsUtil.Show(glowOuter, orb, outer, 128f, a, SsUtil.Cyan, 0f);
                SsUtil.Show(glowInner, orb, outer * 0.5f, 128f, a, Color.white, 0f);
            }
            else { glowOuter.enabled = false; glowInner.enabled = false; }
            if (n == 12) SsUtil.Show(flash, orb, 120f, 128f, 1f, Color.white, 0f); else flash.enabled = false;
            float li = n <= 12 ? 1.3f * n / 12f : Mathf.Max(0f, 1.3f * (1f - (n - 12) / 6f));
            SsUtil.Light(light2d, orb, li);
            log.Add(t + " cast n=" + n + " glowScale=" + k.ToString("F2") + " alpha=" + a.ToString("F2") + " light=" + li.ToString("F2") + (n == 12 ? " FLASH" : ""));
        }
    }
}
