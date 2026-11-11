using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Annie.StarSageSpell
{
    /// <summary>The burst at the target's chest, frames I .. I+40.</summary>
    public class SsImpact : MonoBehaviour
    {
        public AnnieLayers layers;
        public Texture2D glowTex, starTex, ringTex;
        public SpriteRenderer flash, core, ring;
        public ParticleSystem front, back, linger;
        public Light2D light2d;
        public uint seedFront = 31, seedBack = 32, seedLinger = 33;
        public int I = -1000; uint off;
        public List<string> log = new List<string>();
        static readonly float[] CoreSize = { 36f, 60f, 78f, 88f, 90f, 90f, 88f, 86f, 84f };
        static readonly float[] CoreAlpha = { 1f, 1f, 1f, 1f, 1f, 0.8f, 0.6f, 0.4f, 0.2f };

        void Awake()
        {
            flash.sprite = SsUtil.Spr(glowTex, "glow_soft"); core.sprite = SsUtil.Spr(starTex, "star_core"); ring.sprite = SsUtil.Spr(ringTex, "shockwave_ring");
            Hide();
        }

        public void Hide() { flash.enabled = false; core.enabled = false; ring.enabled = false; if (light2d != null) light2d.enabled = false; }

        public void Begin(int i, uint seedOffset) { I = i; off = seedOffset; }

        public void Tick(int t)
        {
            int n = t - I; var chest = SsGeo.Chest;
            if (n < 0 || n > 40) { Hide(); return; }
            var cw = SsGeo.PxToWorld(chest);
            if (n == 0)
            {
                SsUtil.Fire(front, cw, new Vector3(-1f, 0f, 0f), seedFront + off);
                SsUtil.Fire(back, SsGeo.PxToWorld(SsGeo.TargetEdgeX, chest.y), new Vector3(1f, 0f, 0f), seedBack + off);
                SsUtil.Fire(linger, cw, Vector3.forward, seedLinger + off);
            }
            if (n == 0) SsUtil.Show(flash, chest, 150f, 128f, 1f, Color.white, 0f); else flash.enabled = false;
            if (n <= 8) SsUtil.Show(core, chest, CoreSize[n] * 128f / 124f, 128f, CoreAlpha[n], Color.white, n * 14f); else core.enabled = false;
            if (n <= 9)
            {
                float u = n / 9f, e = 1f - (1f - u) * (1f - u);
                float r = 18f + 112f * e;                        // up to 130 px radius
                SsUtil.Show(ring, chest, 2f * r * 256f / 222f, 256f, 0.9f * (1f - u * u), SsUtil.Cyan, 0f);
            }
            else ring.enabled = false;
            SsUtil.Light(light2d, chest, n <= 6 ? 1.5f * (1f - n / 6f) : 0f);
            log.Add(t + " impact n=" + n + (n == 0 ? " FLASH" : ""));
        }
    }
}
