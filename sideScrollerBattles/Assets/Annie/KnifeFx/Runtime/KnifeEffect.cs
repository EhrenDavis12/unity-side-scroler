using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Annie.KnifeFx
{
    /// <summary>
    /// Knife hit / knife miss overlay. Layers are child objects wired into AnnieLayers. Everything is keyed to the Assassin's attack sheet frame
    /// (f2 trail and dust kick, f5 contact, f8 trail off), never to time. The effect never moves a body.
    /// </summary>
    public class KnifeEffect : MonoBehaviour
    {
        public bool miss;
        public AnnieLayers layers;
        public Texture2D flashTex, coreTex;
        public SpriteRenderer flash, core;
        public Transform trailHead; public TrailRenderer trail;
        public ParticleSystem front, backBits, backStreaks, lingerPuffs, lingerStreaks, lingerMotes;
        public GroundDust dust;
        public Light2D light2d;
        public float lightPeak = 0.5f;

        // wedge texture 512x128: visible x 1..495, thick end left; visible thickness at the thick end 68 px
        const float CoreVisLen = 4.94f, CoreThinOff = 2.39f, CoreSy = 0.353f;   // visible length (units at scale 1), thin end offset from the centre, thickness 0.1 H
        static readonly float[] HitAlpha = { 1f, 0.9f, 0.65f, 0.42f, 0.22f, 0.08f };
        static readonly float[] MissAlpha = { 0.6f, 0.55f, 0.38f, 0.22f, 0.09f };

        public Vector3 DustOffset = new Vector3(-0.22f, 0.05f, 0f);
        Vector3 asFeet, arFeet; float farX; int targetOrder, asOrder;
        int lastF = -1; public int contactTick = -1; int trailOffTick = -1;
        Sprite flashSp, coreSp;
        public readonly List<string> log = new List<string>();

        Vector3 Tip(int f) { return KnifeGeo.AssassinCell(KnifeGeo.TipCell(f), asFeet); }
        static bool Live(Component c) { return c != null && c.gameObject.activeInHierarchy; }

        public void Setup(Vector3 asFeet_, Vector3 arFeet_, float farEdgeX, int targetOrder_, int assassinOrder)
        {
            asFeet = asFeet_; arFeet = arFeet_; farX = farEdgeX; targetOrder = targetOrder_; asOrder = assassinOrder;
            var mat = flash != null ? flash.sharedMaterial : core.sharedMaterial;
            if (flash != null)
            {
                flashSp = Sprite.Create(flashTex, new Rect(64, 64, 64, 64), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);   // 2x2 grid, cell 1 = the star (top right)
                flash.sprite = flashSp; flash.sortingOrder = targetOrder + 9; flash.enabled = false;
            }
            coreSp = Sprite.Create(coreTex, new Rect(0, 0, coreTex.width, coreTex.height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            core.sprite = coreSp; core.sortingOrder = targetOrder + 5; core.enabled = false;
            if (trail != null) { trail.sortingOrder = targetOrder + 4; trail.emitting = false; trailHead.position = Tip(1); trail.Clear(); }
            if (light2d != null) light2d.enabled = false;
            Order(front, targetOrder + 6); Order(backBits, targetOrder - 5); Order(backStreaks, targetOrder - 5);
            Order(lingerPuffs, targetOrder - 1); Order(lingerStreaks, targetOrder + 4); Order(lingerMotes, targetOrder + 4);
            foreach (var ps in GetComponentsInChildren<ParticleSystem>(true)) ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (dust != null) dust.Hide();
        }

        static void Order(ParticleSystem ps, int o) { if (ps != null) ps.GetComponent<ParticleSystemRenderer>().sortingOrder = o; }

        public void Tick(int t, int f)
        {
            if (f != lastF)
            {
                if (f >= 0) OnFrame(t, f);
                lastF = f;
            }
            if (trail != null && f < 2) trailHead.position = Tip(1);   // park the head on the start point so the trail has no vertex at the origin
            if (f >= 2 && f <= 7 && Live(trail)) trailHead.position = Tip(f);
            FadeTrail(t);
            if (contactTick >= 0) Post(t - contactTick, f);
        }

        // after the blade pulls back the whole trail fades out over 4 ticks, so no stub of it outlives the tip
        void FadeTrail(int t)
        {
            if (trail == null || trailOffTick < 0 || !Live(trail)) return;
            float k = 1f - (t - trailOffTick + 1) / 4f;
            if (k <= 0f) { trail.Clear(); return; }
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(new Color(0.93f, 0.96f, 1f), 0), new GradientColorKey(new Color(0.93f, 0.96f, 1f), 1) },
                      new[] { new GradientAlphaKey(0.35f * k, 0f), new GradientAlphaKey(0.2f * k, 0.5f), new GradientAlphaKey(0f, 1f) });
            trail.colorGradient = g;
        }

        void OnFrame(int t, int f)
        {
            if (f == 2)
            {
                if (Live(trail))
                {
                    trail.Clear();
                    trailHead.position = Tip(1); trail.emitting = true; trail.AddPosition(Tip(1));
                    trailHead.position = Tip(2); trail.AddPosition(Tip(2));
                    log.Add(t + ": trail on");
                }
                if (Live(dust))
                {
                    dust.Play(KnifeGeo.AssassinCell(KnifeGeo.RearBootCell, asFeet) + DustOffset, 1f, asOrder - 1);
                    log.Add(t + ": dust kick");
                }
            }
            if (f == 5 && contactTick < 0) Contact(t);
            if (f == 8 && trail != null) { trail.emitting = false; trailOffTick = t; }
        }

        void Contact(int t)
        {
            contactTick = t;
            Vector3 tip = Tip(5); float ang = KnifeGeo.BladeAngle(5); Vector3 dir = KnifeGeo.BladeDir(5);
            log.Add(t + ": contact (C) tip=" + tip.x.ToString("F3") + "," + tip.y.ToString("F3"));
            if (!miss)
            {
                float a35 = 50f * Mathf.Deg2Rad;
                Fire(front, tip, new Vector3(-Mathf.Cos(a35), Mathf.Sin(a35), 0f));
                Vector3 far = new Vector3(farX, tip.y, 0f);
                Fire(backBits, far, new Vector3(1f, 0.12f, 0f));
                FireAligned(backStreaks, far, 7f);
                if (Live(light2d)) { light2d.transform.position = tip; light2d.enabled = true; light2d.intensity = lightPeak; }
            }
            else
            {
                // streaks centred 0.3 H past the tip along the blade (lengths 0.3-0.42 H); motes a little closer
                FireAligned(lingerStreaks, tip + dir * (0.22f * KnifeGeo.H), ang);
                Fire(lingerMotes, tip + dir * (0.2f * KnifeGeo.H), dir);
            }
        }

        void Post(int a, int f)
        {
            if (flash != null) flash.enabled = !miss && a == 0 && Live(flash);
            if (flash != null && flash.enabled)
            {
                flash.transform.position = Tip(5); flash.transform.localScale = Vector3.one * 1.8f; flash.color = Color.white;
            }
            var al = miss ? MissAlpha : HitAlpha;
            bool on = a < al.Length && Live(core);
            core.enabled = on;
            if (on)
            {
                // thick end at the fist (a little behind the blade root), thin end 0.25 H (hit) / 0.35 H (miss) past the tip
                Vector3 dir = KnifeGeo.BladeDir(f);
                Vector3 thin = Tip(f) + dir * ((miss ? 0.35f : 0.25f) * KnifeGeo.H);
                Vector3 fist = KnifeGeo.AssassinCell(new Vector2(KnifeGeo.BaseX[KnifeGeo.Fi(f)], KnifeGeo.BaseY[KnifeGeo.Fi(f)]), asFeet) - dir * 0.1f;
                float len = Vector3.Dot(thin - fist, dir);
                float sx = len / CoreVisLen;
                core.transform.position = thin - dir * (CoreThinOff * sx);
                core.transform.rotation = Quaternion.Euler(0f, 0f, KnifeGeo.BladeAngle(f));
                core.transform.localScale = new Vector3(sx, CoreSy, 1f);
                core.color = new Color(0.93f, 0.96f, 1f, al[a]);
            }
            if (!miss)
            {
                if (a == 2) Fire(lingerPuffs, arFeet + new Vector3(-0.28f, -0.04f, 0f), Vector3.up);
                if (light2d != null && Live(light2d))
                {
                    bool lon = a < 4; light2d.enabled = lon;
                    if (lon) light2d.intensity = lightPeak * (1f - a / 4f);
                }
            }
        }

        void Fire(ParticleSystem ps, Vector3 pos, Vector3 dirVec)
        {
            if (!Live(ps)) return;
            ps.transform.position = pos;
            Vector3 d = dirVec.normalized;
            ps.transform.rotation = Quaternion.LookRotation(d, Mathf.Abs(d.y) > 0.99f ? Vector3.right : Vector3.forward);
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.Play(true);
        }

        void FireAligned(ParticleSystem ps, Vector3 pos, float angDeg)
        {
            if (!Live(ps)) return;
            ps.transform.position = pos;
            ps.transform.rotation = Quaternion.Euler(0f, 0f, angDeg);
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.Play(true);
        }
    }
}
