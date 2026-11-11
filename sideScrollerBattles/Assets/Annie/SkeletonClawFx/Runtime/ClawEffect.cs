using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Annie.SkeletonClawFx
{
    /// <summary>
    /// Skeleton cleaver hit / miss overlay. Layers are child objects wired into AnnieLayers. Everything is keyed to the skeleton's attack sheet frame
    /// (frame 3 = trail and smoke start, frame 4 = contact C), never to time. Every emitter takes its own seeded random stream. The effect never moves a body.
    /// </summary>
    public class ClawEffect : MonoBehaviour
    {
        public bool miss;
        public AnnieLayers layers;
        public Texture2D flashTex;
        public SpriteRenderer flash;
        public MeshRenderer coreMr, edgeMr; MeshRenderer hotMr; Mesh hotMesh;
        public Transform trailHead; public TrailRenderer trail;
        public ParticleSystem front, backBits, backStreaks, smoke, wisps, dust, missStreaks, missSoil, missMotes;
        public Light2D light2d;
        public float lightPeak = 0.5f;
        public float coreWidth = 0.42f, edgeWidth = 0.52f;     // ribbon widths in world units; the texture's visible thickness is 68/128 of it

        static readonly float[] HitAlpha = { 1f, 0.9f, 0.65f, 0.42f, 0.22f, 0.08f };
        static readonly float[] MissAlpha = { 0.6f, 0.55f, 0.38f, 0.22f, 0.09f };
        public static readonly Color Bone = new Color(0.97f, 0.93f, 0.80f);
        public static readonly Color Ghost = new Color(222f / 255f, 246f / 255f, 181f / 255f);   // the skeleton's eye dot, sampled from keys/K0ie.png
        static readonly Color Rust = new Color(1f, 0.66f, 0.32f);
        static readonly Color Grave = new Color(0.62f, 0.72f, 0.62f, 0.35f);
        static readonly Color GraveDust = new Color(0.86f, 0.85f, 0.78f, 0.9f);
        static readonly Color Soil = new Color(0.93f, 0.73f, 0.59f, 0.9f);   // the ground's colour (189,149,120) x 1.25

        Vector3 skFeet, arFeet; float archerFeetPx;
        Vector3 P3, P4, P5;
        int lastF = -1; public int contactTick = -1; int trailOnTick = -1, trailOffTick = -1; float sPrev;
        Sprite flashSp; Mesh coreMesh, edgeMesh;
        public readonly List<string> log = new List<string>();
        readonly Dictionary<ParticleSystem, System.Random> rng = new Dictionary<ParticleSystem, System.Random>();

        static bool Live(Component c) { return c != null && c.gameObject.activeInHierarchy; }
        Vector3 Arc(float s) { return ClawGeo.Spline(P3, P4, P5, s); }
        // the drawn crescent is a straight upper-left to lower-right chop, 45 deg from vertical with a 0.04 u arc, its leading end anchored on the blade head of the moment
        Vector3 cutTip;
        Vector3 CutArc(float s)
        {
            Vector3 d = new Vector3(0.7071f, -0.7071f, 0f), perp = new Vector3(0.7071f, 0.7071f, 0f);
            const float L = 1.0f;
            float t = s * 0.5f;
            return cutTip - d * (L * (1f - t)) - perp * (0.04f * Mathf.Sin(Mathf.PI * t));
        }
        static float SOf(int f) { return f <= 3 ? 0f : (f == 4 ? 1f : 2f); }

        public void Setup(Vector3 skFeet_, Vector3 arFeet_, float archerFeetPx_, int targetOrder, int skeletonOrder)
        {
            skFeet = skFeet_; arFeet = arFeet_; archerFeetPx = archerFeetPx_;
            P3 = ClawGeo.Head(3, skFeet); P4 = ClawGeo.Head(4, skFeet); P5 = ClawGeo.Head(5, skFeet);
            if (flash != null)
            {
                flashSp = Sprite.Create(flashTex, new Rect(64, 64, 64, 64), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);   // 2x2 grid, cell 1 = the star (top right)
                flash.sprite = flashSp; flash.enabled = false;
            }
            // HDR tints so the flash and the white-hot centre cross the bloom threshold; the green core and edge stay under 1
            if (flash != null) { flash.material = new Material(flash.sharedMaterial); flash.material.SetColor("_Color", new Color(4f, 4f, 4f, 1f)); }
            if (smoke != null) smoke.GetComponent<ParticleSystemRenderer>().sortingOrder = 96;     // behind the Archer (100/101)
            coreWidth = 0.30f;
            var hotGo = Instantiate(coreMr.gameObject, coreMr.transform.parent); hotGo.name = "HotCentre";
            hotMr = hotGo.GetComponent<MeshRenderer>(); hotMesh = new Mesh { name = "HotRibbon" };
            hotGo.GetComponent<MeshFilter>().sharedMesh = hotMesh; hotMr.sortingOrder = 112;
            hotMr.material = new Material(coreMr.sharedMaterial); hotMr.material.SetColor("_Color", new Color(3f, 3f, 3f, 1f)); hotMr.enabled = false;
            coreMesh = new Mesh { name = "CoreRibbon" }; edgeMesh = new Mesh { name = "EdgeRibbon" };
            coreMr.GetComponent<MeshFilter>().sharedMesh = coreMesh; edgeMr.GetComponent<MeshFilter>().sharedMesh = edgeMesh;
            coreMr.enabled = false; edgeMr.enabled = false;
            if (trail != null) { trail.emitting = false; trailHead.position = Arc(0f); trail.Clear(); }
            if (light2d != null) light2d.enabled = false;
            foreach (var ps in GetComponentsInChildren<ParticleSystem>(true))
            {
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                if (Live(ps)) ps.Play(true);
                rng[ps] = new System.Random((int)ps.randomSeed);
            }
        }

        public void Tick(int t, int f)
        {
            if (f != lastF)
            {
                if (f >= 0) OnFrame(t, f);
                lastF = f;
            }
            if (trailOnTick >= 0 && trailOffTick < 0) MoveTrail(f);
            if (trailOnTick >= 0 && t >= trailOnTick && t < trailOnTick + 5) ShedSmoke(t, f);
            FadeTrail(t);
            if (contactTick >= 0) Post(t - contactTick);
        }

        void OnFrame(int t, int f)
        {
            if (f == 3 && trailOnTick < 0)
            {
                trailOnTick = t; sPrev = 0f;
                if (Live(trail))
                {
                    trail.Clear(); trailHead.position = Arc(0f); trail.emitting = true; trail.AddPosition(Arc(0f));
                    log.Add(t + ": trail on");
                }
            }
            if (f == 4 && contactTick < 0) { contactTick = t; log.Add(t + ": contact (C) head=" + P4.x.ToString("F3") + "," + P4.y.ToString("F3")); }
        }

        void MoveTrail(int f)
        {
            if (!Live(trail)) return;
            float s = SOf(f);
            for (int k = 1; k <= 4; k++) trail.AddPosition(Arc(Mathf.Lerp(sPrev, s, k / 4f)));
            trailHead.position = Arc(s); sPrev = s;
        }

        // the trail stops at tick C+3 (it was on for ticks C-2..C+2) and fades out over three ticks, so no stub outlives the head
        void FadeTrail(int t)
        {
            if (trail == null) return;
            if (contactTick >= 0 && trailOffTick < 0 && t >= contactTick + 3) { trail.emitting = false; trailOffTick = t; log.Add(t + ": trail off"); }
            if (trailOffTick < 0 || !Live(trail)) return;
            float k = 1f - (t - trailOffTick + 1) / 3f;
            if (k <= 0f) { trail.Clear(); return; }
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Bone, 0), new GradientColorKey(Bone, 1) },
                      new[] { new GradientAlphaKey(0.35f * k, 0f), new GradientAlphaKey(0.2f * k, 0.5f), new GradientAlphaKey(0f, 1f) });
            trail.colorGradient = g;
        }

        float R(ParticleSystem ps, float a, float b) { return a + (b - a) * (float)rng[ps].NextDouble(); }

        void Emit(ParticleSystem ps, Vector3 pos, Vector3 vel, float size, float life, Color col, float rotDeg)
        {
            var ep = new ParticleSystem.EmitParams { position = pos, velocity = vel, startLifetime = life, startColor = col, rotation = rotDeg, startSize = size };
            ps.Emit(ep, 1);
        }

        void Emit3(ParticleSystem ps, Vector3 pos, Vector3 vel, Vector2 size, float life, Color col, float rotDeg)
        {
            var ep = new ParticleSystem.EmitParams { position = pos, velocity = vel, startLifetime = life, startColor = col, rotation = rotDeg, startSize3D = new Vector3(size.x, size.y, 1f) };
            ps.Emit(ep, 1);
        }

        // grave smoke shed off the cleaver head on the five ticks the trail is on
        void ShedSmoke(int t, int f)
        {
            if (!Live(smoke)) return;
            Vector3 h = Arc(SOf(f));
            for (int i = 0; i < 2; i++)
            {
                Vector3 p = h + new Vector3(R(smoke, -0.06f, 0.06f), R(smoke, -0.06f, 0.06f), 0f);
                Vector3 v = new Vector3(R(smoke, -0.15f, 0.15f), R(smoke, 0.1f, 0.3f), 0f);
                Emit(smoke, p, v, R(smoke, 0.2f, 0.3f), R(smoke, 0.3f, 0.38f), Grave, R(smoke, 0f, 360f));
            }
        }

        void BuildRibbon(Mesh m, float sEnd, float width)
        {
            const int N = 28;
            var v = new Vector3[2 * (N + 1)]; var uv = new Vector2[v.Length]; var tri = new int[N * 6];
            for (int i = 0; i <= N; i++)
            {
                float s = sEnd * i / N;
                Vector3 p = CutArc(s);
                Vector3 tg = (CutArc(Mathf.Min(s + 0.03f, 2f)) - CutArc(Mathf.Max(s - 0.03f, 0f))).normalized;
                Vector3 nrm = new Vector3(-tg.y, tg.x, 0f) * (width * 0.5f);
                v[2 * i] = p + nrm; v[2 * i + 1] = p - nrm;
                float u = Mathf.Lerp(0.967f, 0.003f, i / (float)N);       // thick end of the strip at the blade end of the path
                uv[2 * i] = new Vector2(u, 1f); uv[2 * i + 1] = new Vector2(u, 0f);
            }
            for (int i = 0; i < N; i++)
            {
                int a = 2 * i, b = a + 1, c = a + 2, d = a + 3, o = i * 6;
                tri[o] = a; tri[o + 1] = c; tri[o + 2] = b; tri[o + 3] = b; tri[o + 4] = c; tri[o + 5] = d;
            }
            m.Clear(); m.vertices = v; m.uv = uv; m.triangles = tri;
            var col = new Color[v.Length]; for (int i = 0; i < col.Length; i++) col[i] = Color.white; m.colors = col;
        }

        void SetRibbonColor(Mesh m, Color c)
        {
            var col = new Color[m.vertexCount]; for (int i = 0; i < col.Length; i++) col[i] = c; m.colors = col;
        }

        void Post(int a)
        {
            if (flash != null)
            {
                flash.enabled = !miss && a == 0 && Live(flash);
                if (flash.enabled) { flash.transform.position = P4; flash.transform.localScale = Vector3.one * 1.8f; flash.color = Color.white; }
            }
            var al = miss ? MissAlpha : HitAlpha;
            bool on = a < al.Length && Live(coreMr);
            coreMr.enabled = on; edgeMr.enabled = on; hotMr.enabled = on;
            if (on)
            {
                cutTip = a < 2 ? P4 : P5;          // the leading end rides the blade head: the frame 4 head on ticks C, C+1, the frame 5 head after
                BuildRibbon(edgeMesh, 2f, edgeWidth); BuildRibbon(coreMesh, 2f, coreWidth); BuildRibbon(hotMesh, 2f, 0.09f);
                SetRibbonColor(edgeMesh, new Color(Ghost.r, Ghost.g, Ghost.b, al[a] * 0.9f));
                SetRibbonColor(coreMesh, new Color(Ghost.r, Ghost.g, Ghost.b, al[a]));
                SetRibbonColor(hotMesh, new Color(1f, 1f, 1f, al[a]));
            }
            if (!miss) PostHit(a); else PostMiss(a);
        }

        void PostHit(int a)
        {
            Vector3 near = ClawGeo.PxToWorld(archerFeetPx + ClawGeo.NearDX, ClawGeo.ContactPxY);
            Vector3 far = ClawGeo.PxToWorld(archerFeetPx + ClawGeo.FarDX, ClawGeo.ContactPxY);
            if (a == 0)
            {
                if (Live(front))
                {
                    // 9 rust sparks, thrown back toward the skeleton and up: axis 50 deg up-left, half-angle 65, emit radius 0.14
                    float axis = 130f;
                    for (int i = 0; i < 9; i++)
                    {
                        float ang = (axis + R(front, -65f, 65f)) * Mathf.Deg2Rad;
                        float sp = R(front, 2.0f, 4.2f);
                        float ra = R(front, 0f, 6.283f), rr = 0.14f * Mathf.Sqrt(R(front, 0f, 1f));
                        Emit(front, near + new Vector3(Mathf.Cos(ra) * rr, Mathf.Sin(ra) * rr, 0f), new Vector3(Mathf.Cos(ang), Mathf.Sin(ang), 0f) * sp,
                             R(front, 0.13f, 0.20f), R(front, 0.2f, 0.25f), Rust, R(front, 0f, 360f));
                    }
                }
                if (Live(backBits))
                {
                    for (int i = 0; i < 8; i++)
                    {
                        float ang = (7f + R(backBits, -18f, 18f)) * Mathf.Deg2Rad; float sp = R(backBits, 3.2f, 5f);
                        Color c = i < 3 ? Ghost : Bone;
                        Emit(backBits, far + new Vector3(0f, R(backBits, -0.1f, 0.1f), 0f), new Vector3(Mathf.Cos(ang), Mathf.Sin(ang), 0f) * sp,
                             R(backBits, 0.13f, 0.20f), R(backBits, 0.27f, 0.34f), c, R(backBits, 0f, 360f));
                    }
                }
                if (Live(backStreaks))
                {
                    for (int i = 0; i < 4; i++)
                    {
                        float angDeg = 7f + R(backStreaks, -6f, 6f); float ang = angDeg * Mathf.Deg2Rad; float sp = R(backStreaks, 3.4f, 5.2f);
                        Emit3(backStreaks, far + new Vector3(0f, R(backStreaks, -0.1f, 0.1f), 0f), new Vector3(Mathf.Cos(ang), Mathf.Sin(ang), 0f) * sp,
                              new Vector2(R(backStreaks, 0.16f, 0.22f), R(backStreaks, 0.05f, 0.07f)), R(backStreaks, 0.27f, 0.34f), new Color(Bone.r, Bone.g, Bone.b, 0.8f), angDeg);
                    }
                }
                if (Live(light2d)) { light2d.transform.position = P4; light2d.enabled = true; light2d.intensity = lightPeak; }
            }
            if (Live(light2d) && a > 0)
            {
                bool lon = a < 5; light2d.enabled = lon;
                if (lon) light2d.intensity = lightPeak * (1f - a / 5f);
            }
            // linger: ghost wisps rising on the Archer's two sides every other tick from C+2 to C+12, grave dust at the feet on C+2, C+6, C+11
            if (Live(wisps) && a >= 2 && a <= 12 && a % 2 == 0)
            {
                for (int i = 0; i < 2; i++)
                {
                    bool leftSide = (i == 0);
                    float x = archerFeetPx + (leftSide ? R(wisps, -115f, -70f) : R(wisps, 65f, 100f));
                    float y = ClawGeo.FeetPxY - R(wisps, 10f, 140f);
                    Emit(wisps, ClawGeo.PxToWorld(x, y), new Vector3(R(wisps, -0.05f, 0.05f), R(wisps, 0.25f, 0.45f), 0f), R(wisps, 0.14f, 0.22f), R(wisps, 0.6f, 0.75f), Ghost, 0f);
                }
            }
            if (Live(dust) && (a == 2 || a == 6 || a == 11))
            {
                Vector3 p = arFeet + new Vector3(-0.28f + R(dust, -0.08f, 0.08f), -0.04f, 0f);
                Emit(dust, p, new Vector3(R(dust, -0.05f, 0.05f), R(dust, 0.1f, 0.25f), 0f), R(dust, 0.24f, 0.30f), R(dust, 0.55f, 0.75f), GraveDust, R(dust, 0f, 360f));
            }
        }

        void PostMiss(int a)
        {
            if (Live(missStreaks) && a <= 2)
            {
                Vector3 tg = (Arc(1.05f) - Arc(0.95f)).normalized;
                float angDeg = Mathf.Atan2(tg.y, tg.x) * Mathf.Rad2Deg;
                int n = a == 0 ? 2 : 1;
                for (int i = 0; i < n; i++)
                {
                    Vector3 perp = new Vector3(-tg.y, tg.x, 0f) * R(missStreaks, -0.2f, 0.2f);
                    Vector3 p = P4 + tg * (0.22f * ClawGeo.H) + perp;
                    Emit3(missStreaks, p, tg * R(missStreaks, 0.4f, 0.7f), new Vector2(R(missStreaks, 1.0f, 1.3f), R(missStreaks, 0.2f, 0.3f)),
                          R(missStreaks, 0.12f, 0.17f), new Color(Bone.r, Bone.g, Bone.b, 0.7f), angDeg);
                }
            }
            if (Live(missMotes) && a == 0)
            {
                for (int i = 0; i < 4; i++)
                {
                    Vector3 p = P4 + new Vector3(R(missMotes, -0.3f, 0.3f), R(missMotes, -0.5f, 0.2f), 0f);
                    Emit(missMotes, p, new Vector3(R(missMotes, -0.1f, 0.1f), R(missMotes, 0.3f, 0.6f), 0f), R(missMotes, 0.12f, 0.2f), R(missMotes, 0.3f, 0.45f), new Color(Ghost.r, Ghost.g, Ghost.b, 0.8f), 0f);
                }
            }
            if (Live(missSoil) && a == 2)
            {
                // one small soil puff on the ground under the cleaver head's low position (frame 5), ticks C+2..C+8
                Vector3 p = new Vector3(P5.x, skFeet.y + 0.04f, 0f);
                Emit(missSoil, p, new Vector3(0f, 0.18f, 0f), 0.28f, 0.23f, Soil, 0f);
            }
        }
    }
}
