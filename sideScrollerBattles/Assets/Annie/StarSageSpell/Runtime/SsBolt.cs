using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Annie.StarSageSpell
{
    /// <summary>The bolt in flight from the orb centre to the target's chest, C+12 .. I (12 frames).</summary>
    public class SsBolt : MonoBehaviour
    {
        public AnnieLayers layers;
        public Texture2D starTex;
        public SpriteRenderer core;
        public Transform trailHead; public TrailRenderer trail;
        public ParticleSystem sparkles;
        public Light2D light2d;
        public uint seedSparkles = 21;
        public int C = -1000; uint off; System.Random rng;
        public List<string> log = new List<string>();
        public StringBuilder pathJson = new StringBuilder();
        public const float StarPx = 28f, TurnPerFrame = 24f;

        void Awake() { core.sprite = SsUtil.Spr(starTex, "star_core"); Hide(); }

        public void Hide() { core.enabled = false; if (trail != null) { trail.emitting = false; } if (light2d != null) light2d.enabled = false; }

        public void Begin(int c, uint seedOffset) { C = c; off = seedOffset; rng = new System.Random((int)(seedSparkles + seedOffset)); }

        public void Tick(int t)
        {
            int n = t - (C + SsGeo.CastFrames); var orb = SsGeo.Orb; var chest = SsGeo.Chest;
            if (n < 0 || n > SsGeo.FlightFrames) { Hide(); return; }
            Vector2 pos = Vector2.Lerp(orb, chest, n / (float)SsGeo.FlightFrames);
            Vector2 d = (chest - orb).normalized;
            SsUtil.Show(core, pos, StarPx * 128f / 124f, 128f, 1f, Color.white, -n * TurnPerFrame);
            var pw = SsGeo.PxToWorld(pos);
            trailHead.position = pw;
            if (n == 0 && trail.gameObject.activeInHierarchy)
            {
                trail.Clear(); trail.emitting = true;
                var wu = new Vector3(d.x, -d.y, 0f);
                trail.AddPosition(pw - wu * 0.5f); trail.AddPosition(pw);
            }
            if (n == SsGeo.FlightFrames) trail.emitting = false;   // the trail dies with the bolt
            SsUtil.Light(light2d, pos, 0.9f);
            if (n > 0 && n < SsGeo.FlightFrames && n % 2 == 0 && sparkles != null && sparkles.gameObject.activeInHierarchy)
            {
                var ep = new ParticleSystem.EmitParams();
                float ang = (float)(rng.NextDouble() * Mathf.PI * 2f), sp = 0.15f + (float)rng.NextDouble() * 0.25f;
                ep.position = pw + new Vector3((float)(rng.NextDouble() - 0.5) * 0.1f, (float)(rng.NextDouble() - 0.5) * 0.1f, 0f);
                ep.velocity = new Vector3(Mathf.Cos(ang) * sp, Mathf.Sin(ang) * sp, 0f);
                ep.startLifetime = 0.33f + (float)rng.NextDouble() * 0.1f; ep.startSize = 0.2f + (float)rng.NextDouble() * 0.08f;
                ep.rotation3D = new Vector3(0, 0, (float)rng.NextDouble() * 360f);
                ep.startColor = (Color32)SsUtil.Gold;
                sparkles.Emit(ep, 1);
            }
            log.Add(t + " bolt n=" + n + " pos=(" + pos.x.ToString("F1") + "," + pos.y.ToString("F1") + ")");
            pathJson.Append("{\"t\":").Append(t).Append(",\"n\":").Append(n).Append(",\"c\":[").Append(pos.x.ToString("F2")).Append(',').Append(pos.y.ToString("F2")).Append("]},\n");
        }
    }
}
