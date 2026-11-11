using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Annie.FireArrowSpell
{
    [Serializable] public class FaSheets { public Sprite[] idleBody, idleBow, hurtBody, hurtBow; }

    /// <summary>Screen-pixel maths copied from the StarSageSpell job. Screen px: x right, y down, 1920x1080; world: 100 px per unit, origin screen centre.</summary>
    public static class FaGeo
    {
        public const float S = 240f / 404f;
        public const float FeetCellX = 247f, FeetCellY = 431f;
        public const float CasterScale = 218f / 804f;
        public const float StillFeetX = 424f, StillFeetY = 862f;
        public const float OrbStillX = 781.6f, OrbStillY = 169.6f;
        public static readonly Vector2 CasterFeet = new Vector2(430f, 1040f);
        public static readonly Vector2 TargetFeet = new Vector2(1490f, 1040f);
        public const float TargetEdgeX = 1570f;
        public const float H = 270f;                                // the Archer's height on screen, from the brief

        public static Vector3 PxToWorld(double x, double y) { return new Vector3((float)((x - 960.0) / 100.0), (float)((540.0 - y) / 100.0), 0f); }
        public static Vector3 PxToWorld(Vector2 p) { return PxToWorld(p.x, p.y); }
        public static Vector2 Orb { get { return new Vector2(CasterFeet.x + (OrbStillX - StillFeetX) * CasterScale, CasterFeet.y - (StillFeetY - OrbStillY) * CasterScale); } }
        public static Vector2 CasterCentre { get { return new Vector2(CasterFeet.x + (512f - StillFeetX) * CasterScale, CasterFeet.y - (StillFeetY - 512f) * CasterScale); } }
        public static Vector2 Chest { get { return new Vector2(TargetFeet.x - (300f - FeetCellX) * S, TargetFeet.y - (FeetCellY - 238f) * S); } }
        public static Vector3 TargetRoot()
        {
            var feet = PxToWorld(TargetFeet);
            return feet - new Vector3((FeetCellX - 256f) * 0.01f * S * -1f, (512f - FeetCellY) * 0.01f * S, 0f);
        }

        // plan (0-based frames): charge 5..26, launch 27, impact 39; spell 2 is +157
        public const int C1 = 5, ChargeFrames = 22, FlightFrames = 12, HitFrames = 20, I1 = 39, C2 = 162, I2 = 196, TotalFrames = 250;
        public const float StepPx = 934.4f / 12f;
        public static Vector2 Dir { get { return (Chest - Orb).normalized; } }
        public static Vector2 Perp { get { var d = Dir; return new Vector2(-d.y, d.x); } }
        public static Vector2 HeadAt(float n) { return Orb + (Chest - Orb) * (n / FlightFrames); }
        public static int Hurt(int tick, int start) { return (int)((tick - start) * 16f / 30f); }
        public static int IdleOff { get { return (64 - (int)(I1 * 16f / 30f) % 64) % 64; } }
    }

    public static class FaUtil
    {
        public static float RotSign = 1f;
        public static Color C(int r, int g, int b, float a = 1f) { return new Color(r / 255f, g / 255f, b / 255f, a); }
        public static readonly Color WhiteYellow = new Color(255 / 255f, 245 / 255f, 200 / 255f, 1f);
        public static readonly Color YellowOrange = new Color(255 / 255f, 190 / 255f, 60 / 255f, 1f);
        public static readonly Color Orange = new Color(255 / 255f, 120 / 255f, 20 / 255f, 1f);
        public static readonly Color RedOrange = new Color(220 / 255f, 60 / 255f, 10 / 255f, 1f);
        public static readonly Color EmberCol = new Color(255 / 255f, 110 / 255f, 30 / 255f, 1f);

        /// <summary>yellow -> orange -> red -> transparent over life u; alpha holds to aStart then falls with a power.</summary>
        public static Color Fire(float u, float aStart, float aPow)
        {
            u = Mathf.Clamp01(u);
            Color a = C(255, 215, 90), b = Orange, c = RedOrange;
            Color rgb = u < 0.45f ? Color.Lerp(a, b, u / 0.45f) : Color.Lerp(b, c, Mathf.Clamp01((u - 0.45f) / 0.4f));
            float k = Mathf.Clamp01((u - aStart) / Mathf.Max(0.001f, 1f - aStart));
            rgb.a = Mathf.Pow(1f - k, aPow) * Mathf.Clamp01(u / 0.06f + 0.35f);
            return rgb;
        }

        public static Sprite Spr(Texture2D t, string name)
        {
            var s = Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect); s.name = name; return s;
        }
        /// <summary>One tile of a grid sheet, tile 0 top-left.</summary>
        public static Sprite[] Tiles(Texture2D t, int cols, int rows, string name)
        {
            var r = new Sprite[cols * rows]; float w = t.width / (float)cols, h = t.height / (float)rows;
            for (int i = 0; i < r.Length; i++)
            {
                int cx = i % cols, cy = i / cols;
                r[i] = Sprite.Create(t, new Rect(cx * w, (rows - 1 - cy) * h, w, h), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect); r[i].name = name + i;
            }
            return r;
        }

        public static void Show(SpriteRenderer sr, Sprite sp, Vector2 px, float wPx, float hPx, float alpha, Color c, float rotDeg)
        {
            if (sp != null) sr.sprite = sp;
            sr.enabled = true;
            sr.transform.position = PxToW(px);
            sr.transform.localScale = new Vector3(wPx / (sr.sprite.rect.width), hPx / (sr.sprite.rect.height), 1f);
            sr.transform.rotation = Quaternion.Euler(0, 0, rotDeg);
            sr.color = new Color(c.r, c.g, c.b, alpha);
        }
        public static Vector3 PxToW(Vector2 p) { return FaGeo.PxToWorld(p); }

        public static void Light(Light2D l, Vector2 px, float intensity)
        {
            if (l == null) return;
            l.enabled = intensity > 0.001f; l.intensity = intensity; l.transform.position = FaGeo.PxToWorld(px);
        }
        public static float Smooth(float u) { u = Mathf.Clamp01(u); return u * u * (3f - 2f * u); }
        public static Vector2 Pol(float deg) { float r = deg * Mathf.Deg2Rad; return new Vector2(Mathf.Cos(r), Mathf.Sin(r)); }
        public static float Rnd(System.Random r, float a, float b) { return a + (float)r.NextDouble() * (b - a); }
    }

    /// <summary>A Particle System used as a pure renderer: every particle's place, size and colour is computed here each frame, so the spiral, the braid and the exact timing do not depend on the simulation.</summary>
    public class FaPs
    {
        public class Rec
        {
            public float t0, life, s0, s1; public Func<float, Vector2> pos; public Func<float, Color> col;
            public bool align = true; public float rot0; public uint seed;
        }
        public ParticleSystem ps; public List<Rec> recs = new List<Rec>(); ParticleSystem.Particle[] buf = new ParticleSystem.Particle[256];
        public FaPs(ParticleSystem p) { ps = p; }
        public void Clear() { recs.Clear(); }

        public void Draw(float t)
        {
            if (ps == null || !ps.gameObject.activeInHierarchy) return;
            if (!ps.isPlaying) ps.Play(true);
            int n = 0;
            foreach (var r in recs)
            {
                float age = t - r.t0; if (age < 0f || age >= r.life || n >= buf.Length) continue;
                float u = age / r.life; var p = new ParticleSystem.Particle();
                Vector2 sp = r.pos(u);
                p.position = FaGeo.PxToWorld(sp);
                p.startSize = Mathf.Lerp(r.s0, r.s1, u) / 100f;
                p.startColor = (Color32)r.col(u);
                p.startLifetime = r.life / 30f; p.remainingLifetime = r.life / 30f * (1f - u);
                p.randomSeed = r.seed;
                float rot = r.rot0;
                if (r.align)
                {
                    Vector2 a = r.pos(Mathf.Max(0f, u - 0.03f)), b = r.pos(Mathf.Min(1f, u + 0.03f)); Vector2 v = b - a;
                    if (v.sqrMagnitude > 1e-6f) { Vector2 w = -v.normalized; rot += Mathf.Atan2(-w.y, w.x) * Mathf.Rad2Deg - 90f; }
                }
                p.rotation = rot * FaUtil.RotSign;
                buf[n++] = p;
            }
            if (n == 0) ps.Clear(); else ps.SetParticles(buf, n);
        }
    }
}
