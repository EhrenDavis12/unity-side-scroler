using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Annie.BattleScene1Full
{
    /// <summary>
    /// The v2 Archer-vs-Archer hit effect (a copy of AvaEffect's logic) for battle scene 1, where 1 world unit = 1 screen px.
    /// Built from the unchanged v2 prefab Fx_Spark by the builder: every field is copied across by name, so the layers and values are v2's.
    /// Differences from AvaEffect, all units: W (100) scales every length; rate over distance is divided by W; the ring clamp reads AVA coordinates;
    /// the arrow and trail head are placed by the director each 48 fps tick; sort orders come from the director.
    /// </summary>
    public class SfEffect : MonoBehaviour
    {
        public string typeName;
        public AnnieLayers layers;
        public Texture2D arrowTex, flashTex, ringTex, coreTex, shardTex;
        public SpriteRenderer arrow, flash, core, ring;
        public SpriteRenderer[] shards;
        public Transform trailHead;
        public TrailRenderer trailRenderer;
        public ParticleSystem pathPs;
        public bool pathOnGround;
        public ParticleSystem front, back;
        public ParticleSystem[] linger;
        public bool lingerAtFeet;
        public Light2D light2d;
        public int coreMode;
        public int coreCols, coreRows, coreFrames;
        public float coreScale = 1f;
        public float bodyThick = 0.8f;
        public Color ringColor = Color.white, flashColor = Color.white, lightColor = Color.white;
        public float lightPeak = 1f;
        public float ringEnd = 1.6f;
        public bool busy;
        public float W = 100f;
        public const float TrailWidth = 0.05f;

        Sprite[] flashSp, coreSp, shardSp;
        Sprite arrowSp, ringSp;
        class PsBase { public ParticleSystem ps; public float speed, size, grav, radius, rateDist; public Vector3 shapeScale; public uint seed; }
        readonly List<PsBase> psb = new List<PsBase>();
        int shotCount;
        Vector3 grip, chest, feetT, u;
        float k; int dir, arrowStart, A, tgtOrder, endTick; float centerYAva;
        public int ImpactCount { get; private set; }

        public static Sprite[] Grid(Texture2D t, int cols, int rows, int n)
        {
            var r = new Sprite[n];
            float w = t.width / (float)cols, h = t.height / (float)rows;
            for (int i = 0; i < n; i++)
            {
                var rect = new Rect((i % cols) * w, (rows - 1 - i / cols) * h, w, h);
                r[i] = Sprite.Create(t, rect, new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
                r[i].name = t.name + "_" + i;
            }
            return r;
        }

        void Awake()
        {
            arrowSp = Grid(arrowTex, 1, 1, 1)[0];
            ringSp = Grid(ringTex, 1, 1, 1)[0];
            flashSp = Grid(flashTex, 2, 2, 4);
            coreSp = Grid(coreTex, coreCols, coreRows, coreFrames);
            if (shards != null && shards.Length > 0) shardSp = Grid(shardTex, 2, 2, 4);
            arrow.sprite = arrowSp; ring.sprite = ringSp; flash.sprite = flashSp[1]; core.sprite = coreSp[0];
            if (shards != null) for (int i = 0; i < shards.Length; i++) shards[i].sprite = shardSp[i % 4];
            foreach (var ps in GetComponentsInChildren<ParticleSystem>(true))
            {
                var m = ps.main; var sh = ps.shape; var em = ps.emission;
                var b = new PsBase { ps = ps, speed = m.startSpeedMultiplier, size = m.startSizeMultiplier, grav = m.gravityModifierMultiplier, radius = sh.radius, shapeScale = sh.scale, seed = ps.randomSeed, rateDist = em.rateOverDistanceMultiplier };
                psb.Add(b);
                em.rateOverDistanceMultiplier = b.rateDist / W;   // per world unit: AVA unit = 100 px
            }
            if (trailRenderer != null) trailRenderer.minVertexDistance = 0.02f * W;
            HideAll();
        }

        public void HideAll()
        {
            busy = false;
            arrow.enabled = false; flash.enabled = false; core.enabled = false; ring.enabled = false;
            if (shards != null) foreach (var s in shards) s.enabled = false;
            if (light2d != null) light2d.enabled = false;
            if (trailRenderer != null) { trailRenderer.emitting = false; trailRenderer.Clear(); }
            foreach (var p in psb) p.ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        void ApplyScale(float kk)
        {
            foreach (var p in psb)
            {
                var m = p.ps.main; m.startSpeedMultiplier = p.speed * kk; m.startSizeMultiplier = p.size * kk; m.gravityModifierMultiplier = p.grav * kk;
                var sh = p.ps.shape; sh.radius = p.radius * kk; sh.scale = p.shapeScale * kk;
            }
            if (trailRenderer != null) trailRenderer.widthMultiplier = TrailWidth * kk;
        }

        void Fire(ParticleSystem ps, Vector3 pos, Vector3 dirVec)
        {
            if (ps == null || !ps.gameObject.activeInHierarchy) return;
            ps.transform.position = pos;
            if (dirVec.sqrMagnitude > 0) ps.transform.rotation = Quaternion.LookRotation(dirVec.normalized, Vector3.up == dirVec.normalized ? Vector3.right : Vector3.forward);
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.useAutoRandomSeed = false;
            PsBase b = psb.Find(x => x.ps == ps);
            ps.randomSeed = (b != null ? b.seed : 1u) + (uint)(shotCount * 101);
            ps.Play(true);
        }

        static void SetOrder(ParticleSystem ps, int o) { if (ps != null) ps.GetComponent<ParticleSystemRenderer>().sortingOrder = o; }

        /// <summary>World units are screen px (x right, y up = -image y). kEff = 100 x the target's row factor (row 1 = 1).</summary>
        public void Begin(Vector3 grip_, Vector3 chest_, Vector3 feetT_, float kEff, float arrowScale, int dir_, int arrowStart_, int A_, int targetOrder, int arrowOrder)
        {
            grip = grip_; chest = chest_; feetT = feetT_; k = kEff; dir = dir_;
            arrowStart = arrowStart_; A = A_; tgtOrder = targetOrder;
            u = (chest - grip); u.z = 0; u.Normalize();
            endTick = A + 70; busy = true; shotCount++;
            layers.Apply();
            ApplyScale(k);
            arrow.transform.localScale = new Vector3(arrowScale, arrowScale, 1f);
            arrow.transform.rotation = Quaternion.Euler(0, 0, Mathf.Atan2(u.y, u.x) * Mathf.Rad2Deg);
            arrow.sortingOrder = arrowOrder;
            if (trailRenderer != null) trailRenderer.sortingOrder = arrowOrder - 2;
            if (pathPs != null) SetOrder(pathPs, arrowOrder - 3);
            SetOrder(back, targetOrder - 5); SetOrder(front, targetOrder + 6);
            if (linger != null) for (int i = 0; i < linger.Length; i++) SetOrder(linger[i], targetOrder + 4 + (i == 0 ? 0 : -1));
        }

        public Vector3 Dir { get { return u; } }

        public void Tick(int t)
        {
            if (!busy || t < arrowStart) return;
            if (t < A) { Flight(t); return; }
            if (t == A) Impact();
            Post(t - A);
            if (t >= endTick) HideAll();
        }

        /// <summary>First flight step only: seeds the trail and the path emitter. The director moves the arrow and the trail head every 48 fps tick.</summary>
        void Flight(int t)
        {
            int n = t - arrowStart;
            arrow.enabled = true;
            if (n == 0)
            {
                Vector3 nock = grip - u * (ArrowLenPx * 0.5f);
                if (trailRenderer != null)
                {
                    trailHead.position = nock; trailRenderer.Clear();
                    trailRenderer.emitting = layers != null && trailRenderer.gameObject.activeInHierarchy;
                    if (trailRenderer.emitting) { trailRenderer.AddPosition(nock - u * (ArrowLenPx * 0.35f)); trailRenderer.AddPosition(nock); }
                }
                if (pathPs != null && pathPs.gameObject.activeInHierarchy)
                {
                    pathPs.transform.position = nock; pathPs.transform.rotation = Quaternion.Euler(0, 0, Mathf.Atan2(u.y, u.x) * Mathf.Rad2Deg); pathPs.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    pathPs.useAutoRandomSeed = false; pathPs.randomSeed = 7u + (uint)(shotCount * 101); pathPs.Play(true); pathPs.Emit(1);
                }
            }
        }
        public float ArrowLenPx = 100f;
        public bool InFlight { get { return busy && arrow.enabled; } }
        public void PlaceFlight(Vector3 mid)
        {
            arrow.transform.position = mid;
            Vector3 nock = mid - u * (ArrowLenPx * 0.5f);
            trailHead.position = nock;
            if (pathPs != null && pathPs.gameObject.activeInHierarchy) pathPs.transform.position = nock;
        }

        void Impact()
        {
            ImpactCount++;
            arrow.enabled = false;
            trailHead.position = chest;
            if (pathPs != null) pathPs.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            Vector3 far = chest + new Vector3(dir * bodyThick * k, 0, 0);
            float a45 = 35f * Mathf.Deg2Rad;
            Vector3 backDir = new Vector3(-dir * Mathf.Cos(a45), Mathf.Sin(a45), 0);
            Fire(front, chest, backDir);
            Fire(back, far, new Vector3(dir, 0.15f, 0));
            if (linger != null)
                foreach (var l in linger)
                {
                    Vector3 p = lingerAtFeet ? feetT + new Vector3(-dir * 0.1f * k, 0.1f * k, 0) : chest + new Vector3(0, -0.4f * k, 0);
                    Fire(l, p, Vector3.up);
                }
            flash.transform.position = chest; flash.transform.rotation = Quaternion.identity;
            flash.transform.localScale = Vector3.one * (3.0f * k); flash.color = flashColor; flash.sortingOrder = tgtOrder + 9;
            ring.transform.position = chest; ring.sortingOrder = tgtOrder + 7;
            core.transform.position = chest; core.sortingOrder = tgtOrder + 5;
            if (shards != null) foreach (var s in shards) s.sortingOrder = tgtOrder + 5;
            centerYAva = (540f + chest.y) / 100f;   // AVA world y of the chest (origin at the screen centre, 100 px a unit)
            if (light2d != null)
            {
                light2d.transform.position = chest; light2d.color = lightColor; light2d.pointLightOuterRadius = 7.2f * k;
                light2d.pointLightInnerRadius = 0f;
            }
        }

        void Post(int a)
        {
            flash.enabled = a == 0 && flash.gameObject.activeInHierarchy;
            if (coreMode == 0)
            {
                int f = (int)(a * 16f / 30f);
                bool on = f < coreFrames;
                core.enabled = on;
                if (on) { core.sprite = coreSp[f]; core.transform.localScale = Vector3.one * (coreScale * k); }
            }
            else if (coreMode == 1)
            {
                bool on = a <= 14; core.enabled = on;
                if (on)
                {
                    float sc = a <= 3 ? Mathf.Lerp(0.5f, 1f, a / 3f) : Mathf.Lerp(1f, 0.45f, (a - 3) / 11f);
                    float flick = 1f + 0.06f * Mathf.Sin(a * 2.3f);
                    core.transform.localScale = Vector3.one * (coreScale * k * sc * flick);
                    core.color = new Color(1, 1, 1, a < 8 ? 1f : 1f - (a - 8) / 7f);
                }
            }
            else if (coreMode == 2 && shards != null)
            {
                bool on = a <= 16;
                for (int i = 0; i < shards.Length; i++)
                {
                    var s = shards[i]; s.enabled = on;
                    if (!on) continue;
                    float ang = (i * 360f / shards.Length + 15f);
                    float rad = Mathf.Lerp(0.12f, 0.17f, Mathf.Clamp01(a / 4f)) * 2.4f * k;
                    float pop = Mathf.Clamp01((a + 1) / 3f);
                    Vector3 dv = new Vector3(Mathf.Cos(ang * Mathf.Deg2Rad), Mathf.Sin(ang * Mathf.Deg2Rad), 0);
                    s.transform.position = chest + dv * rad;
                    s.transform.rotation = Quaternion.Euler(0, 0, ang);
                    s.transform.localScale = Vector3.one * (coreScale * k * pop);
                    s.color = new Color(1, 1, 1, a < 9 ? 1f : 1f - (a - 9) / 8f);
                }
            }
            if (a <= 8)
            {
                float f = a / 8f; float e = 1f - (1f - f) * (1f - f);
                ring.enabled = ring.gameObject.activeInHierarchy;
                float kRel = k / W;
                float rsAva = Mathf.Lerp(0.15f, ringEnd, e) * kRel;
                rsAva = Mathf.Min(rsAva, Mathf.Max(0.15f, (centerYAva + 5.35f) / 1.28f));
                ring.transform.localScale = Vector3.one * (rsAva * W);
                ring.color = new Color(ringColor.r, ringColor.g, ringColor.b, 1f - f * f);
            }
            else ring.enabled = false;
            if (light2d != null)
            {
                bool on = a < 6;
                light2d.enabled = on;
                if (on) light2d.intensity = lightPeak * (1f - a / 6f);
            }
        }
    }
}
