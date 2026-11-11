using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Annie.ArrowMiss
{
    /// <summary>
    /// One missed arrow: it flies a parabola at the approved arc speed, sticks in the ground at tick L, quivers, and fades at L+60..L+70.
    /// The arrow is not a layer; the four layers are L2_Core (entry mound), L3_ParticlesFront (clods), L5_Trail (thin streak), L5_Linger (dust).
    /// Driven by 30 fps ticks from the director. Authored at row 0.
    /// </summary>
    public class AmEffect : MonoBehaviour
    {
        public AnnieLayers layers;
        public Texture2D arrowTex, clumpTex, puffTex;
        public Transform groundPlane;                      // collision plane for the clods, moved to the landing height
        public Color[] moundColor;
        public SpriteRenderer arrow;
        public SpriteRenderer[] mound;
        public float[] moundOffX, moundOffY, moundPx;      // image px offsets from the landing point, quad size px
        public Transform trailHead;
        public TrailRenderer trail;
        public ParticleSystem front, linger;
        public Color trailColor = Color.white;
        public int arrowStart = 19;
        public uint frontSeed = 71, lingerSeed = 72;

        public int L;                       // tick the tip reaches the ground (real value, logged)
        public Vector2 landPx;              // landing point, image px
        public float landAngleImg;          // arrow angle at landing, deg, image axes (y down, positive = descending)
        public string takeName;
        public List<string> log = new List<string>();
        public StringBuilder pathJson = new StringBuilder();

        AmPath path; double theta, kCurve;
        Sprite flySp, stuckSp, soilA;
        double startAngle;                 // angle of the arrow on its first flight frame: lying along the arm, as in the approved hit
        const int TrailStart = 2;          // first flight index with a trail: the tail is clear of the body from here
        static readonly float[] Quiver = { 5f, -3.5f, 2.5f, -1.5f, 1f, -0.5f, 0f };
        float halfLen, bury, shaftLen;

        public static void TakeParams(string take, out double th, out double k, out int L)
        {
            if (take == "short") { th = 13.0; k = 0.0013335641120941386; L = 23; }
            else { th = 33.28709154542787; k = 0.0007941491468029527; L = 33; }
        }

        void Awake()
        {
            flySp = Sprite.Create(arrowTex, new Rect(0, 0, 490, 148), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect); flySp.name = "arrow_fly";
            stuckSp = Sprite.Create(arrowTex, new Rect(0, 0, 376, 148), new Vector2(1f, 0.5f), 100f, 0, SpriteMeshType.FullRect); stuckSp.name = "arrow_stuck_cut376";
            soilA = Sprite.Create(puffTex, new Rect(128, 256, 128, 128), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect); soilA.name = "dust_puff_tile5_as_soil";
            mound[0].sprite = soilA; mound[1].sprite = soilA; mound[1].flipX = true;
            halfLen = AmGeo.ArrowLenCell * AmGeo.S * 0.5f;
            bury = 75f * 0.5f * AmGeo.S;                 // texture px past the cut x 376, at the arrow's screen scale
            shaftLen = (376f - 37f) * 0.5f * AmGeo.S;    // cut end to the fletching tip
            HideAll();
        }

        public void HideAll()
        {
            arrow.enabled = false;
            foreach (var m in mound) m.enabled = false;
            if (trail != null) { trail.emitting = false; trail.Clear(); }
            foreach (var ps in new[] { front, linger }) if (ps != null) ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        /// <summary>Choose the take: sets L and the landing point from the path.</summary>
        public void Setup(string take)
        {
            takeName = take; int l; TakeParams(take, out theta, out kCurve, out l); L = l;
            var g = AmGeo.GripPx;
            path = new AmPath(g.x, g.y, theta, kCurve);
            double cx, cy, ux, uy; path.Pose(AmGeo.Speed * (L - arrowStart), out cx, out cy, out ux, out uy);
            // the flight pose at L has its tip 'bury' px past the ground; the cut end sits where the tip meets the ground
            landPx = new Vector2((float)(cx + (halfLen - bury) * ux), (float)(cy + (halfLen - bury) * uy));
            landAngleImg = (float)(System.Math.Atan2(uy, ux) * 180.0 / System.Math.PI);
            log.Add("take " + take + " theta=" + theta + " k=" + kCurve + " L=" + L + " landPx=(" + landPx.x.ToString("F1") + "," + landPx.y.ToString("F1") + ") landAngle=" + landAngleImg.ToString("F1"));
            // first-frame angle: the nock on the arm line of the approved hit, the tip on the parabola
            {
                double ccx, ccy, tux, tuy; path.Pose(0, out ccx, out ccy, out tux, out tuy);
                var chest = new Vector2(AmGeo.TargetFeet.x - (300f - AmGeo.FeetCellX) * AmGeo.S, AmGeo.TargetFeet.y - (AmGeo.FeetCellY - 238f) * AmGeo.S);
                Vector2 v2d = (chest - g).normalized;
                double nockAx = g.x - v2d.x * halfLen, nockAy = g.y - v2d.y * halfLen;
                startAngle = System.Math.Atan2(ccy + tuy * halfLen - nockAy, ccx + tux * halfLen - nockAx);
                log.Add("first-frame arrow angle (image deg, + = descending) " + (startAngle * 180.0 / System.Math.PI).ToString("F2"));
            }
            // trail
            trail.widthMultiplier = 0.05f; trail.sortingOrder = 1890; ApplyTrailAlpha(1f);
            arrow.transform.localScale = new Vector3(0.5f * AmGeo.S, 0.5f * AmGeo.S, 1f);
            pathJson.Append("{\"take\":\"").Append(take).Append("\",\"L\":").Append(L).Append(",\"land\":[").Append(landPx.x).Append(',').Append(landPx.y).Append("],\"frames\":[\n");
        }

        void ApplyTrailAlpha(float f)
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(trailColor, 0), new GradientColorKey(trailColor, 1) },
                      new[] { new GradientAlphaKey(0.35f * f, 0f), new GradientAlphaKey(0.2f * f, 0.5f), new GradientAlphaKey(0f, 1f) });
            trail.colorGradient = g;
        }

        static void Fire(ParticleSystem ps, Vector3 pos, Vector3 dir, uint seed)
        {
            if (ps == null || !ps.gameObject.activeInHierarchy) return;
            ps.transform.position = pos;
            ps.transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.forward);
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.useAutoRandomSeed = false; ps.randomSeed = seed;
            ps.Play(true);
        }

        public void Tick(int t)
        {
            if (t < arrowStart) return;
            if (t < L) { Flight(t - arrowStart, t); return; }
            if (t == L) Land();
            Post(t - L, t);
        }

        void Flight(int n, int t)
        {
            double cx, cy, ux, uy; path.Pose(AmGeo.Speed * n, out cx, out cy, out ux, out uy);
            // the tip stays on the parabola; the arrow's angle eases from the arm line to the path tangent over the first two steps
            double tipX = cx + ux * halfLen, tipY = cy + uy * halfLen;
            double aPath = System.Math.Atan2(uy, ux);
            double e = n >= 2 ? 1.0 : (n / 2.0) * (n / 2.0) * (3.0 - 2.0 * (n / 2.0));
            double a = startAngle + (aPath - startAngle) * e;
            double dx = System.Math.Cos(a), dy = System.Math.Sin(a);
            double acx = tipX - dx * halfLen, acy = tipY - dy * halfLen;
            arrow.sprite = flySp; arrow.sortingOrder = 1900; arrow.enabled = true; arrow.color = Color.white;
            arrow.transform.position = AmGeo.PxToWorld(acx, acy);
            float ang = (float)(a * 180.0 / System.Math.PI);
            arrow.transform.rotation = Quaternion.Euler(0, 0, -ang);
            double nx = acx - dx * halfLen, ny = acy - dy * halfLen;
            Vector3 nockW = AmGeo.PxToWorld(nx, ny);
            trailHead.position = nockW;
            if (n == TrailStart && trail != null)
            {
                trail.Clear();
                trail.emitting = trail.gameObject.activeInHierarchy;
                if (trail.emitting)
                {
                    Vector3 wu = new Vector3((float)dx, (float)-dy, 0f);
                    trail.AddPosition(nockW - wu * 0.2f); trail.AddPosition(nockW);
                }
            }
            log.Add(t + " fly n=" + n + " centre=(" + acx.ToString("F1") + "," + acy.ToString("F1") + ") ang=" + ang.ToString("F1") + " tip=(" + tipX.ToString("F1") + "," + tipY.ToString("F1") + ")");
            Sample(t, "fly", acx, acy, dx, dy, nx, ny, tipX, tipY, ang);
        }

        void Land()
        {
            arrow.sprite = stuckSp; arrow.sortingOrder = 104;
            if (trail != null)
            {
                // the trail's head ends at the stuck arrow's tail (quiver 0) on L, drawn under the arrow
                double ar0 = landAngleImg * System.Math.PI / 180.0;
                trailHead.position = AmGeo.PxToWorld(landPx.x - System.Math.Cos(ar0) * shaftLen, landPx.y - System.Math.Sin(ar0) * shaftLen);
                trail.sortingOrder = 103;
            }
            Vector3 pw = AmGeo.PxToWorld(landPx);
            for (int i = 0; i < mound.Length; i++)
            {
                var m = mound[i]; m.sortingOrder = 106 + i;   // distinct orders: equal ones draw in an unstable order between runs
                m.transform.position = AmGeo.PxToWorld(landPx.x + moundOffX[i], landPx.y + moundOffY[i]);
                m.transform.localScale = Vector3.one * (moundPx[i] / 128f);
                m.transform.rotation = Quaternion.Euler(0, 0, i == 0 ? 8f : -12f);
            }
            // clods: axis up, tilted 25 deg away from the shooter (the shooter is on the left)
            float a25 = (takeName == "short" ? -5f : 25f) * Mathf.Deg2Rad;   // short: tilted slightly toward the shooter so no clod reaches the target's bow
            if (takeName == "short") { var shp = front.shape; shp.angle = 24f; }   // short: narrower cone, the clods must stay clear of the target's bow
            Fire(front, pw + new Vector3(0f, 0.02f, 0f), new Vector3(Mathf.Sin(a25), Mathf.Cos(a25), 0f), frontSeed);
            if (groundPlane != null) groundPlane.position = pw + new Vector3(0f, 0.02f, 0f);
            Fire(linger, pw + new Vector3(0f, 0.05f, 0f), Vector3.up, lingerSeed);
            log.Add(L + " LAND buried sprite at (" + landPx.x.ToString("F1") + "," + landPx.y.ToString("F1") + ") angleImg=" + landAngleImg.ToString("F2"));
        }

        void Post(int a, int t)
        {
            float alpha = a <= 60 ? 1f : Mathf.Clamp01(1f - (a - 60) / 10f);
            bool on = a < 70;
            // stuck arrow with the quiver about the buried end
            float q = a < Quiver.Length ? Quiver[a] : 0f;
            arrow.enabled = on; arrow.color = new Color(1, 1, 1, alpha);
            arrow.transform.position = AmGeo.PxToWorld(landPx);
            arrow.transform.rotation = Quaternion.Euler(0, 0, -landAngleImg + q);
            for (int mi = 0; mi < mound.Length; mi++) { var m = mound[mi]; m.enabled = on && m.gameObject.activeInHierarchy; var mc = moundColor[mi]; m.color = new Color(mc.r, mc.g, mc.b, mc.a * alpha); }
            if (a == 1 && trail != null) trail.emitting = false;
            // trail: stops at L, alpha to 0 over L..L+3, then cleared
            if (trail != null && a <= 3)
            {
                ApplyTrailAlpha((3 - a) / 3f);
                if (a == 3) trail.Clear();
            }
            float angImg = landAngleImg - q; double ar = angImg * System.Math.PI / 180.0;
            double tx = landPx.x - System.Math.Cos(ar) * shaftLen, ty = landPx.y - System.Math.Sin(ar) * shaftLen;
            if (a <= 12 || a % 10 == 0 || a >= 58) log.Add(t + " stuck a=" + a + " quiver=" + q + " fletchEnd=(" + tx.ToString("F1") + "," + ty.ToString("F1") + ")");
            Sample(t, "stuck", landPx.x, landPx.y, 0, 0, tx, ty, landPx.x, landPx.y, angImg);
            if (a >= 70) { arrow.enabled = false; }
        }

        void Sample(int t, string st, double cx, double cy, double ux, double uy, double nx, double ny, double tipx, double tipy, double ang)
        {
            pathJson.Append("{\"t\":").Append(t).Append(",\"s\":\"").Append(st).Append("\",\"c\":[").Append(cx.ToString("F2")).Append(',').Append(cy.ToString("F2"))
                .Append("],\"tail\":[").Append(nx.ToString("F2")).Append(',').Append(ny.ToString("F2")).Append("],\"tip\":[").Append(tipx.ToString("F2")).Append(',').Append(tipy.ToString("F2"))
                .Append("],\"ang\":").Append(ang.ToString("F2")).Append("},\n");
        }
    }
}
